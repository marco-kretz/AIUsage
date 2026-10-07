namespace AIUsage.Core;

public enum UsageStatus
{
    Pending,
    Ok,
    NoCredentials,
    AuthFailed,
    RateLimited,
    Offline,
    Stale,
}

/// <param name="UsedPercent">Percent of the window's quota already consumed (0–100, may exceed 100).</param>
public sealed record UsageWindow(string Id, string Label, double UsedPercent, DateTimeOffset? ResetsAt);

/// <param name="UpdatedAt">When <see cref="Windows"/> were fetched; null if never.</param>
/// <param name="RetryAfter">Server-requested delay before the next request, if any.</param>
public sealed record UsageSnapshot(
    string ProviderId,
    string DisplayName,
    string? Plan,
    IReadOnlyList<UsageWindow> Windows,
    UsageStatus Status,
    DateTimeOffset? UpdatedAt,
    TimeSpan? RetryAfter = null)
{
    public static UsageSnapshot Pending(IUsageProvider provider) =>
        new(provider.Id, provider.DisplayName, null, [], UsageStatus.Pending, null);
}

public interface IUsageProvider
{
    string Id { get; }
    string DisplayName { get; }

    /// <summary>Fetches current usage. Must not throw for expected failures; report them via <see cref="UsageSnapshot.Status"/>.</summary>
    Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken);
}
