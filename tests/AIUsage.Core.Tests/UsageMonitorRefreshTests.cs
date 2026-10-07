using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AIUsage.Core.Tests;

public class UsageMonitorRefreshTests
{
    private sealed class CountingProvider : IUsageProvider
    {
        public int Fetches;
        public string Id => "p";
        public string DisplayName => "P";

        public Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Fetches);
            return Task.FromResult(new UsageSnapshot(Id, DisplayName, null, [], UsageStatus.Ok, null));
        }
    }

    [Fact]
    public async Task RefreshAsync_CompletesAfterFetch_AndReturnsImmediatelyWhenSuppressed()
    {
        var provider = new CountingProvider();
        var time = new FakeTimeProvider();
        using var monitor = new UsageMonitor([provider], () => TimeSpan.FromSeconds(180), NullLogger<UsageMonitor>.Instance, time);
        var timeout = TimeSpan.FromSeconds(5);

        var first = monitor.RefreshAsync();
        monitor.Start();
        await first.WaitAsync(timeout, TestContext.Current.CancellationToken);
        Assert.Equal(1, provider.Fetches);

        // Within the minimum manual gap: completes without fetching.
        await monitor.RefreshAsync().WaitAsync(timeout, TestContext.Current.CancellationToken);
        Assert.Equal(1, provider.Fetches);

        time.Advance(TimeSpan.FromSeconds(20));
        await monitor.RefreshAsync().WaitAsync(timeout, TestContext.Current.CancellationToken);
        Assert.Equal(2, provider.Fetches);
    }
}
