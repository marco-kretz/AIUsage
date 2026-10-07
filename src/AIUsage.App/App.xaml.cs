using System.Globalization;
using AIUsage.App.Tray;
using AIUsage.App.ViewModels;
using AIUsage.App.Views;
using AIUsage.Core;
using AIUsage.Core.Claude;
using AIUsage.Core.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace AIUsage.App;

public partial class App : Application
{
    private readonly EventWaitHandle _showSignal;
    private readonly AppSettings _settings = AppSettings.Load();
    private ServiceProvider _services = null!;
    private ILogger<App> _logger = null!;
    private UsageMonitor _monitor = null!;
    private TrayIcon _tray = null!;
    private DispatcherQueue _dispatcher = null!;
    private FlyoutWindow _flyout = null!;
    private UsageNotifier _notifier = null!;
    private SettingsWindow? _settingsWindow;

    public App(EventWaitHandle showSignal)
    {
        _showSignal = showSignal;
        var culture = CultureInfo.GetCultureInfo(_settings.Language is "en" ? "en" : "de");
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _services = ConfigureServices();
        _logger = _services.GetRequiredService<ILogger<App>>();
        UnhandledException += (_, e) => _logger.LogError(e.Exception, "Unhandled UI exception");
        _logger.LogInformation("Starting AIUsage");

        _tray = _services.GetRequiredService<TrayIcon>();
        _tray.MenuItems = () =>
        [
            (TrayIcon.MenuCommand.Refresh, Strings.Menu_Refresh, false),
            (TrayIcon.MenuCommand.Settings, Strings.Menu_Settings, false),
            (TrayIcon.MenuCommand.Autostart, Strings.Menu_Autostart, Autostart.IsEnabled),
            (TrayIcon.MenuCommand.Exit, Strings.Menu_Exit, false),
        ];
        _tray.MenuCommandInvoked += OnMenuCommand;
        _monitor = _services.GetRequiredService<UsageMonitor>();
        var flyoutViewModel = _services.GetRequiredService<FlyoutViewModel>();
        flyoutViewModel.SettingsRequested += ShowSettings;
        _flyout = new FlyoutWindow(flyoutViewModel, _tray.GetIconRect);
        _tray.Clicked += _flyout.Toggle;

        _notifier = _services.GetRequiredService<UsageNotifier>();
        _notifier.Invoked += () => _dispatcher.TryEnqueue(_flyout.Show);

        _monitor.SnapshotChanged += _ => _dispatcher.TryEnqueue(() =>
        {
            OnSnapshotsChanged();
            _notifier.Process(_monitor.Snapshots);
        });
        OnSnapshotsChanged();
        _monitor.Start();

        ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => _dispatcher.TryEnqueue(_flyout.Show), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information)
            .AddFile(Path.Combine(AppSettings.Directory, "logs", "aiusage-{Date}.log"), retainedFileCountLimit: 7));
        services.AddSingleton(_settings);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
        // Register further providers here; the UI renders whatever snapshots the monitor publishes.
        services.AddSingleton<IUsageProvider>(sp => ActivatorUtilities.CreateInstance<ClaudeCodeProvider>(sp, () => _settings.CredentialsPath));
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<UsageMonitor>(sp, () => TimeSpan.FromSeconds(_settings.PollIntervalSeconds)));
        services.AddSingleton<TrayIcon>();
        services.AddSingleton<FlyoutViewModel>();
        services.AddSingleton<UsageNotifier>();
        return services.BuildServiceProvider();
    }

    private void OnSnapshotsChanged()
    {
        var snapshots = _monitor.Snapshots;
        _tray.Update(BuildTrayState(snapshots), BuildTooltip(snapshots));
        _flyout.ViewModel.Update(snapshots, animateBars: _flyout.IsOpen);
    }

    private TrayIconState BuildTrayState(IReadOnlyList<UsageSnapshot> snapshots)
    {
        var windows = snapshots.SelectMany(s => UsageFormat.IconWindows(s, _settings)).ToList();
        if (windows.Count == 0)
        {
            return new(snapshots.All(s => s.Status == UsageStatus.Pending) ? TrayIconKind.Pending : TrayIconKind.Error);
        }

        var max = windows.Max(w => w.UsedPercent);
        var dimmed = snapshots.Any(s => s.Status is UsageStatus.Stale or UsageStatus.RateLimited);
        return new(TrayIconKind.Value, max, UsageFormat.Level(max, _settings), dimmed);
    }

    private static string BuildTooltip(IReadOnlyList<UsageSnapshot> snapshots)
    {
        var now = DateTimeOffset.Now;
        var lines = new List<string>();
        foreach (var s in snapshots)
        {
            lines.Add(s.Plan is null ? s.DisplayName : $"{s.DisplayName} ({s.Plan})");
            lines.AddRange(s.Windows.Select(w => UsageFormat.Format(Strings.Tooltip_Window, w.Label, w.UsedPercent,
                w.ResetsAt is { } at ? UsageFormat.Duration(at - now) : "–")));
            if (UsageFormat.StatusMessage(s.Status) is { } message)
            {
                lines.Add(message);
            }
        }

        return string.Join('\n', lines);
    }

    private void OnMenuCommand(TrayIcon.MenuCommand command)
    {
        switch (command)
        {
            case TrayIcon.MenuCommand.Refresh:
                _monitor.RefreshNow();
                break;
            case TrayIcon.MenuCommand.Settings:
                ShowSettings();
                break;
            case TrayIcon.MenuCommand.Autostart:
                Autostart.IsEnabled = !Autostart.IsEnabled;
                break;
            case TrayIcon.MenuCommand.Exit:
                ExitApp();
                break;
        }
    }

    private void ShowSettings()
    {
        _flyout.Hide();
        if (_settingsWindow is null)
        {
            var viewModel = new SettingsViewModel(_settings, _monitor.Snapshots);
            viewModel.Saved += () =>
            {
                OnSnapshotsChanged();
                _monitor.RefreshNow();
            };
            _settingsWindow = new SettingsWindow(viewModel);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Activate();
    }

    private void ExitApp()
    {
        _settingsWindow?.Close();
        _monitor.Dispose();
        _tray.Dispose();
        _services.Dispose();
        Exit();
    }
}
