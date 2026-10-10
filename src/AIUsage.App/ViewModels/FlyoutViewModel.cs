using System.Collections.ObjectModel;
using System.Numerics;
using AIUsage.Core;
using AIUsage.Core.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace AIUsage.App.ViewModels;

/// <summary>Provider-agnostic flyout state, built purely from <see cref="UsageSnapshot"/>s.</summary>
public sealed partial class FlyoutViewModel(AppSettings settings, UsageMonitor monitor) : ObservableObject
{
    public ObservableCollection<ProviderViewModel> Providers { get; } = [];

    [ObservableProperty]
    public partial string UpdatedText { get; set; } = Strings.Footer_NeverUpdated;

    public event Action? SettingsRequested;

    // AsyncRelayCommand disables the button while running; the minimum duration keeps the spinner from flickering.
    [RelayCommand]
    private Task Refresh() => Task.WhenAll(monitor.RefreshAsync(), Task.Delay(500));

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    public void Update(IReadOnlyList<UsageSnapshot> snapshots, bool animateBars)
    {
        var now = DateTimeOffset.Now;
        for (var i = 0; i < snapshots.Count; i++)
        {
            if (i >= Providers.Count)
            {
                Providers.Add(new ProviderViewModel());
            }

            Providers[i].Update(snapshots[i], settings, now, animateBars);
        }

        while (Providers.Count > snapshots.Count)
        {
            Providers.RemoveAt(Providers.Count - 1);
        }

        var updatedAt = snapshots.Max(s => s.UpdatedAt);
        UpdatedText = updatedAt is { } at ? UsageFormat.Format(Strings.Footer_UpdatedAt, at.ToLocalTime()) : Strings.Footer_NeverUpdated;
    }

    /// <summary>Empties the bars so they grow again on the next show.</summary>
    public void CollapseBars()
    {
        foreach (var window in Providers.SelectMany(p => p.Windows))
        {
            window.BarScale = new Vector3(0, 1, 1);
        }
    }

    public void UpdateCountdowns()
    {
        var now = DateTimeOffset.Now;
        foreach (var window in Providers.SelectMany(p => p.Windows))
        {
            window.UpdateCountdown(now);
        }
    }

    public void ExpandBars()
    {
        foreach (var window in Providers.SelectMany(p => p.Windows))
        {
            window.BarScale = window.TargetScale;
        }
    }
}

public sealed partial class ProviderViewModel : ObservableObject
{
    public ObservableCollection<WindowViewModel> Windows { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string? Plan { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; }

    // x:Bind skips function bindings whose argument is null, so IsOpen needs its own property.
    [ObservableProperty]
    public partial bool HasStatus { get; set; }

    public void Update(UsageSnapshot snapshot, AppSettings settings, DateTimeOffset now, bool animateBars)
    {
        Title = snapshot.DisplayName;
        Plan = snapshot.Plan;
        StatusMessage = UsageFormat.StatusMessage(snapshot.Status);
        HasStatus = !string.IsNullOrEmpty(StatusMessage);
        StatusSeverity = snapshot.Status switch
        {
            UsageStatus.Pending => InfoBarSeverity.Informational,
            UsageStatus.Stale or UsageStatus.RateLimited => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Error,
        };

        // Update in place so bars animate from their previous value.
        for (var i = 0; i < snapshot.Windows.Count; i++)
        {
            var window = snapshot.Windows[i];
            if (i >= Windows.Count || Windows[i].Id != window.Id)
            {
                Windows.Insert(i, new WindowViewModel(window.Id));
            }

            Windows[i].Update(window, settings, now, animateBars);
        }

        while (Windows.Count > snapshot.Windows.Count)
        {
            Windows.RemoveAt(Windows.Count - 1);
        }
    }
}

public sealed partial class WindowViewModel(string id) : ObservableObject
{
    private DateTimeOffset? _resetsAt;

    public string Id { get; } = id;

    [ObservableProperty]
    public partial string Label { get; set; } = "";

    [ObservableProperty]
    public partial string PercentText { get; set; } = "";

    [ObservableProperty]
    public partial string ResetText { get; set; } = "";

    [ObservableProperty]
    public partial UsageLevel Level { get; set; }

    [ObservableProperty]
    public partial Vector3 BarScale { get; set; } = new(0, 1, 1);

    public Vector3 TargetScale { get; private set; }

    public void Update(UsageWindow window, AppSettings settings, DateTimeOffset now, bool animateBars)
    {
        _resetsAt = window.ResetsAt;
        Label = window.Label;
        PercentText = UsageFormat.Format("{0:0} %", window.UsedPercent);
        Level = UsageFormat.Level(window.UsedPercent, settings);
        TargetScale = new Vector3((float)Math.Clamp(window.UsedPercent / 100, 0, 1), 1, 1);
        if (animateBars)
        {
            BarScale = TargetScale;
        }

        UpdateCountdown(now);
    }

    public void UpdateCountdown(DateTimeOffset now) => ResetText = UsageFormat.ResetText(_resetsAt, now);
}
