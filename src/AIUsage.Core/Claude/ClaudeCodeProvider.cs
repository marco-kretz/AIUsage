using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AIUsage.Core.Claude;

/// <summary>Reads Claude Code's local OAuth token and queries the usage endpoint. Never refreshes or logs the token.</summary>
public sealed class ClaudeCodeProvider(
    HttpClient http,
    Func<string?> credentialsPathOverride,
    ILogger<ClaudeCodeProvider> logger,
    TimeProvider time) : IUsageProvider
{
    private static readonly Uri UsageUri = new("https://api.anthropic.com/api/oauth/usage");

    public static string DefaultCredentialsPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    public string Id => "claude-code";
    public string DisplayName => "Claude Code";

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var credentials = await ReadCredentialsAsync(cancellationToken);
        if (credentials is null)
        {
            return Snapshot(UsageStatus.NoCredentials, null);
        }

        if (credentials.ExpiresAt is { } expiresAt && expiresAt <= time.GetUtcNow())
        {
            return Snapshot(UsageStatus.AuthFailed, credentials.Plan);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUri);
        request.Headers.Authorization = new("Bearer", credentials.AccessToken);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    return Snapshot(UsageStatus.Ok, credentials.Plan, ClaudeUsageParser.Parse(json), time.GetUtcNow());
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    logger.LogWarning("Usage request rejected with {StatusCode}", (int)response.StatusCode);
                    return Snapshot(UsageStatus.AuthFailed, credentials.Plan);
                case HttpStatusCode.TooManyRequests:
                    var retryAfter = GetRetryAfter(response);
                    logger.LogWarning("Usage request rate limited, retry after {RetryAfter}", retryAfter);
                    return Snapshot(UsageStatus.RateLimited, credentials.Plan) with { RetryAfter = retryAfter };
                default:
                    logger.LogWarning("Usage request failed with {StatusCode}", (int)response.StatusCode);
                    return Snapshot(UsageStatus.Offline, credentials.Plan);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("Usage request failed: {Error}", ex.Message);
            return Snapshot(UsageStatus.Offline, credentials.Plan);
        }
    }

    private UsageSnapshot Snapshot(UsageStatus status, string? plan, IReadOnlyList<UsageWindow>? windows = null, DateTimeOffset? updatedAt = null) =>
        new(Id, DisplayName, plan, windows ?? [], status, updatedAt);

    private TimeSpan? GetRetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - time.GetUtcNow(),
        _ => null,
    };

    private async Task<Credentials?> ReadCredentialsAsync(CancellationToken cancellationToken)
    {
        var path = credentialsPathOverride() is { Length: > 0 } custom ? custom : DefaultCredentialsPath;
        try
        {
            await using var stream = File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ParseCredentials(doc.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning("Cannot read credentials from {Path}: {Error}", path, ex.GetType().Name);
            return null;
        }
    }

    internal static Credentials? ParseCredentials(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("claudeAiOauth", out var oauth) || oauth.ValueKind != JsonValueKind.Object
            || !oauth.TryGetProperty("accessToken", out var token) || token.ValueKind != JsonValueKind.String
            || token.GetString() is not { Length: > 0 } accessToken)
        {
            return null;
        }

        DateTimeOffset? expiresAt = oauth.TryGetProperty("expiresAt", out var exp) && exp.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;
        return new Credentials(accessToken, expiresAt, GetPlan(oauth));
    }

    // subscriptionType is e.g. "max"/"pro"; rateLimitTier e.g. "default_claude_max_5x" carries the multiplier.
    private static string? GetPlan(JsonElement oauth)
    {
        var type = oauth.TryGetProperty("subscriptionType", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        if (string.IsNullOrEmpty(type))
        {
            return null;
        }

        var plan = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(type);
        var tier = oauth.TryGetProperty("rateLimitTier", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        var multiplier = tier?.Split('_').LastOrDefault(p => p.Length > 1 && p.EndsWith('x') && char.IsDigit(p[0]));
        return multiplier is null ? plan : $"{plan} {multiplier}";
    }

    internal sealed record Credentials(string AccessToken, DateTimeOffset? ExpiresAt, string? Plan)
    {
        public override string ToString() => $"Credentials {{ ExpiresAt = {ExpiresAt}, Plan = {Plan} }}";
    }
}
