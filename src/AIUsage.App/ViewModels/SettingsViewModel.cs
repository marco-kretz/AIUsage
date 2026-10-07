using System.Collections.ObjectModel;
using AIUsage.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIUsage.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public SettingsViewModel(AppSettings settings, IReadOnlyList<UsageSnapshot> snapshots)
    {
        _settings = settings;
        PollIntervalSeconds = settings.PollIntervalSeconds;
        WarningThreshold = settings.WarningThreshold;
        CriticalThreshold = settings.CriticalThreshold;
        NotificationsEnabled = settings.NotificationsEnabled;
        CredentialsPath = settings.CredentialsPath ?? "";
        LanguageIndex = settings.Language == "en" ? 1 : 0;
        StartWithWindows = Autostart.IsEnabled;
        foreach (var snapshot in snapshots)
        {
            foreach (var window in snapshot.Windows)
            {
                var key = AppSettings.WindowKey(snapshot.ProviderId, window.Id);
                IconWindows.Add(new IconWindowOption(key, $"{snapshot.DisplayName}: {window.Label}")
                {
                    IsSelected = settings.IconWindows.Count == 0 || settings.IconWindows.Contains(key),
                });
            }
        }
    }

    public ObservableCollection<IconWindowOption> IconWindows { get; } = [];

    [ObservableProperty]
    public partial double PollIntervalSeconds { get; set; }

    [ObservableProperty]
    public partial double WarningThreshold { get; set; }

    [ObservableProperty]
    public partial double CriticalThreshold { get; set; }

    [ObservableProperty]
    public partial bool NotificationsEnabled { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial string CredentialsPath { get; set; }

    [ObservableProperty]
    public partial int LanguageIndex { get; set; }

    public string DefaultCredentialsPath => Core.Claude.ClaudeCodeProvider.DefaultCredentialsPath;

    public event Action? Closed;

    /// <summary>Raised after saving so the app can re-render and re-poll.</summary>
    public event Action? Saved;

    [RelayCommand]
    private void Save()
    {
        // NumberBox yields NaN when cleared; fall back to the current values.
        _settings.PollIntervalSeconds = (int)Math.Max(UsageMonitor.MinPollInterval.TotalSeconds, double.IsNaN(PollIntervalSeconds) ? _settings.PollIntervalSeconds : PollIntervalSeconds);
        var warning = double.IsNaN(WarningThreshold) ? _settings.WarningThreshold : Math.Clamp(WarningThreshold, 1, 100);
        var critical = double.IsNaN(CriticalThreshold) ? _settings.CriticalThreshold : Math.Clamp(CriticalThreshold, 1, 100);
        (_settings.WarningThreshold, _settings.CriticalThreshold) = (Math.Min(warning, critical), Math.Max(warning, critical));
        _settings.NotificationsEnabled = NotificationsEnabled;
        _settings.CredentialsPath = string.IsNullOrWhiteSpace(CredentialsPath) ? null : CredentialsPath.Trim().Trim('"');
        _settings.Language = LanguageIndex == 1 ? "en" : "de";
        _settings.IconWindows = IconWindows.All(w => w.IsSelected) || IconWindows.All(w => !w.IsSelected)
            ? []
            : IconWindows.Where(w => w.IsSelected).Select(w => w.Key).ToList();
        _settings.Save();
        if (Autostart.IsEnabled != StartWithWindows)
        {
            Autostart.IsEnabled = StartWithWindows;
        }

        Saved?.Invoke();
        Closed?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Closed?.Invoke();
}

public sealed partial class IconWindowOption(string key, string label) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
