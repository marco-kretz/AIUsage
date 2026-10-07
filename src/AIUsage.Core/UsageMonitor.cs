using Microsoft.Extensions.Logging;

namespace AIUsage.Core;

/// <summary>Polls every provider on its own loop, keeps the last good values on transient failures and backs off.</summary>
public sealed class UsageMonitor(
    IEnumerable<IUsageProvider> providers,
    Func<TimeSpan> pollInterval,
    ILogger<UsageMonitor> logger,
    TimeProvider time) : IDisposable
{
    public static readonly TimeSpan MinPollInterval = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MinManualRefreshGap = TimeSpan.FromSeconds(15);

    private readonly CancellationTokenSource _cts = new();
    private readonly List<Loop> _loops = providers.Select(p => new Loop(p)).ToList();

    /// <summary>Raised on a background thread whenever a provider's snapshot changes.</summary>
    public event Action<UsageSnapshot>? SnapshotChanged;

    public IReadOnlyList<UsageSnapshot> Snapshots => _loops.Select(l => l.Current).ToList();

    public void Start()
    {
        foreach (var loop in _loops)
        {
            _ = RunAsync(loop, _cts.Token);
        }
    }

    public void RefreshNow()
    {
        foreach (var loop in _loops)
        {
            loop.Refresh.TrySetResult();
        }
    }

    public void Dispose() => _cts.Cancel();

    private async Task RunAsync(Loop loop, CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            UsageSnapshot fetched;
            try
            {
                fetched = await loop.Provider.FetchAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Provider {Provider} failed", loop.Provider.Id);
                fetched = loop.Current with { Status = UsageStatus.Offline, Windows = [], RetryAfter = null };
            }

            var lastFetch = time.GetUtcNow();
            failures = fetched.Status is UsageStatus.RateLimited or UsageStatus.Offline ? failures + 1 : 0;
            loop.Current = Merge(loop.Current, fetched);
            logger.LogInformation("{Provider}: {Status}, {Count} windows", loop.Provider.Id, fetched.Status, fetched.Windows.Count);
            SnapshotChanged?.Invoke(loop.Current);

            var due = lastFetch + NextDelay(fetched, failures, Clamp(pollInterval()));
            // Manual refresh must not bypass a server-requested pause.
            var earliestManual = fetched.Status == UsageStatus.RateLimited ? due : lastFetch + MinManualRefreshGap;
            if (!await WaitAsync(loop, due, earliestManual, ct))
            {
                return;
            }
        }
    }

    private async Task<bool> WaitAsync(Loop loop, DateTimeOffset due, DateTimeOffset earliestManual, CancellationToken ct)
    {
        try
        {
            while (time.GetUtcNow() < due)
            {
                var refresh = loop.Refresh.Task;
                var completed = await Task.WhenAny(Task.Delay(due - time.GetUtcNow(), time, ct), refresh);
                await completed; // propagate cancellation
                if (completed == refresh)
                {
                    loop.Refresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    if (time.GetUtcNow() >= earliestManual)
                    {
                        break;
                    }
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    internal static TimeSpan Clamp(TimeSpan interval) => interval < MinPollInterval ? MinPollInterval : interval;

    internal static TimeSpan NextDelay(UsageSnapshot fetched, int failures, TimeSpan interval)
    {
        if (failures == 0)
        {
            return interval;
        }

        var backoff = TimeSpan.FromTicks(Math.Min(interval.Ticks << Math.Min(failures - 1, 8), MaxBackoff.Ticks));
        if (backoff < interval)
        {
            backoff = interval;
        }

        return fetched.RetryAfter is { } retryAfter && retryAfter > backoff ? retryAfter : backoff;
    }

    /// <summary>Transient failures keep the previous values instead of blanking the UI.</summary>
    internal static UsageSnapshot Merge(UsageSnapshot previous, UsageSnapshot fetched)
    {
        if (fetched.Status is not (UsageStatus.RateLimited or UsageStatus.Offline) || previous.Windows.Count == 0)
        {
            return fetched;
        }

        return fetched with
        {
            Windows = previous.Windows,
            Plan = fetched.Plan ?? previous.Plan,
            UpdatedAt = previous.UpdatedAt,
            Status = fetched.Status == UsageStatus.Offline ? UsageStatus.Stale : UsageStatus.RateLimited,
        };
    }

    private sealed class Loop(IUsageProvider provider)
    {
        public IUsageProvider Provider { get; } = provider;
        public volatile UsageSnapshot Current = UsageSnapshot.Pending(provider);
        public TaskCompletionSource Refresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
