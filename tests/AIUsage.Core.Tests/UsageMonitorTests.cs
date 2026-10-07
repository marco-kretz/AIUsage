using System.Globalization;

namespace AIUsage.Core.Tests;

public class UsageMonitorTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(180);
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static UsageSnapshot Snapshot(UsageStatus status, params UsageWindow[] windows) =>
        new("p", "P", null, windows, status, windows.Length > 0 ? Now : null);

    [Fact]
    public void Merge_Offline_KeepsPreviousValuesAsStale()
    {
        var previous = Snapshot(UsageStatus.Ok, new UsageWindow("w", "W", 10, null));

        var merged = UsageMonitor.Merge(previous, Snapshot(UsageStatus.Offline));

        Assert.Equal(UsageStatus.Stale, merged.Status);
        Assert.Equal(previous.Windows, merged.Windows);
        Assert.Equal(Now, merged.UpdatedAt);
    }

    [Fact]
    public void Merge_RateLimited_KeepsPreviousValues()
    {
        var previous = Snapshot(UsageStatus.Ok, new UsageWindow("w", "W", 10, null));

        var merged = UsageMonitor.Merge(previous, Snapshot(UsageStatus.RateLimited));

        Assert.Equal(UsageStatus.RateLimited, merged.Status);
        Assert.Single(merged.Windows);
    }

    [Theory]
    [InlineData(UsageStatus.AuthFailed)]
    [InlineData(UsageStatus.NoCredentials)]
    public void Merge_AuthProblems_DropValues(UsageStatus status)
    {
        var previous = Snapshot(UsageStatus.Ok, new UsageWindow("w", "W", 10, null));

        Assert.Empty(UsageMonitor.Merge(previous, Snapshot(status)).Windows);
    }

    [Fact]
    public void Merge_OfflineWithoutHistory_StaysOffline() =>
        Assert.Equal(UsageStatus.Offline, UsageMonitor.Merge(Snapshot(UsageStatus.Pending), Snapshot(UsageStatus.Offline)).Status);

    [Theory]
    [InlineData(0, null, 180)]
    [InlineData(1, null, 180)]
    [InlineData(2, null, 360)]
    [InlineData(3, 60, 720)]
    [InlineData(1, 900, 900)]
    [InlineData(20, null, 1800)]
    public void NextDelay_BacksOffAndHonorsRetryAfter(int failures, int? retryAfterSeconds, int expectedSeconds)
    {
        var fetched = Snapshot(UsageStatus.RateLimited) with
        {
            RetryAfter = retryAfterSeconds is { } s ? TimeSpan.FromSeconds(s) : null,
        };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), UsageMonitor.NextDelay(fetched, failures, Interval));
    }

    [Fact]
    public void Clamp_EnforcesMinimumInterval() =>
        Assert.Equal(Interval, UsageMonitor.Clamp(TimeSpan.FromSeconds(30)));

    [Theory]
    [InlineData(134, "2 Std 14 Min")]
    [InlineData(0.5, "1 Min")]
    [InlineData(60 * 24 * 2 + 61, "2 T 1 Std")]
    public void Duration_IsFormattedInGerman(double minutes, string expected)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
        Assert.Equal(expected, UsageFormat.Duration(TimeSpan.FromMinutes(minutes)));
    }
}
