using System.Globalization;
using System.Text.Json;
using AIUsage.Core.Claude;

namespace AIUsage.Core.Tests;

public class ClaudeUsageParserTests
{
    public ClaudeUsageParserTests() => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");

    private static IReadOnlyList<UsageWindow> ParseFixture(string name) =>
        ClaudeUsageParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    [Fact]
    public void RealResponse_ParsesKnownWindowsAndScopedWeeklyLimit()
    {
        var windows = ParseFixture("real-2026-10.json");

        Assert.Equal(["five_hour", "seven_day", "seven_day_fable"], windows.Select(w => w.Id));
        Assert.Equal(8.0, windows[0].UsedPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-10-07T17:00:00.526662Z", CultureInfo.InvariantCulture), windows[0].ResetsAt);
        Assert.Equal("5-Stunden-Limit", windows[0].Label);
        Assert.Equal("Wochenlimit Fable", windows[2].Label);
    }

    [Fact]
    public void NullAndWronglyTypedFields_AreSkipped()
    {
        var windows = ParseFixture("nulls.json");

        var window = Assert.Single(windows);
        Assert.Equal("seven_day", window.Id);
        Assert.Equal(42.5, window.UsedPercent);
        Assert.Null(window.ResetsAt);
    }

    [Fact]
    public void MissingFields_AreTolerated()
    {
        var window = Assert.Single(ParseFixture("missing.json"));

        Assert.Equal(97.3, window.UsedPercent);
        Assert.Null(window.ResetsAt);
    }

    [Fact]
    public void UnknownFields_AreIgnored_AndScopedLimitsDoNotDuplicateNamedWindows()
    {
        var windows = ParseFixture("extras.json");

        Assert.Equal(["five_hour", "seven_day_opus", "extra_usage"], windows.Select(w => w.Id));
        Assert.Equal(81.2, windows[1].UsedPercent);
        Assert.Null(windows[2].ResetsAt);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{not json")]
    public void NonObjectPayload_Throws(string json) =>
        Assert.ThrowsAny<JsonException>(() => ClaudeUsageParser.Parse(json));

    [Theory]
    [InlineData("""{"claudeAiOauth":{"accessToken":"t","subscriptionType":"max","rateLimitTier":"default_claude_max_5x"}}""", "Max 5x")]
    [InlineData("""{"claudeAiOauth":{"accessToken":"t","subscriptionType":"pro","rateLimitTier":null}}""", "Pro")]
    [InlineData("""{"claudeAiOauth":{"accessToken":"t"}}""", null)]
    public void Credentials_DerivePlan(string json, string? plan)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(plan, ClaudeCodeProvider.ParseCredentials(doc.RootElement)!.Plan);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"claudeAiOauth":{"accessToken":null}}""")]
    [InlineData("""{"claudeAiOauth":{"accessToken":""}}""")]
    public void Credentials_WithoutToken_AreNull(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Null(ClaudeCodeProvider.ParseCredentials(doc.RootElement));
    }

    [Fact]
    public void Credentials_ToString_DoesNotLeakToken()
    {
        using var doc = JsonDocument.Parse("""{"claudeAiOauth":{"accessToken":"secret-token"}}""");
        Assert.DoesNotContain("secret-token", ClaudeCodeProvider.ParseCredentials(doc.RootElement)!.ToString());
    }
}
