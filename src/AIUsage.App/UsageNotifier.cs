using AIUsage.Core;
using AIUsage.Core.Resources;
using Microsoft.Extensions.Logging;
using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace AIUsage.App;

/// <summary>Toasts when a window crosses 80 % / 95 %, once per window, reset period and threshold.</summary>
internal sealed class UsageNotifier(AppSettings settings, ILogger<UsageNotifier> logger)
{
    private static readonly int[] Thresholds = [80, 95];
    private const string AppUserModelId = "AIUsage.App";
    private ToastNotifier? _notifier;

    public event Action? Invoked;

    public void Process(IReadOnlyList<UsageSnapshot> snapshots)
    {
        // Only fresh data counts; stale values were already evaluated when they arrived.
        var fresh = snapshots.Where(s => s.Status == UsageStatus.Ok).ToList();
        if (fresh.Count == 0)
        {
            return;
        }

        var currentKeys = new HashSet<string>();
        var changed = false;
        foreach (var snapshot in fresh)
        {
            foreach (var window in snapshot.Windows)
            {
                // resets_at carries sub-second jitter between responses; 10-minute buckets keep the key stable.
                var period = window.ResetsAt is { } at ? Math.Round(at.ToUnixTimeSeconds() / 600.0) : 0;
                var keys = Thresholds.ToDictionary(t => t, t => $"{AppSettings.WindowKey(snapshot.ProviderId, window.Id)}/{period}/{t}");
                currentKeys.UnionWith(keys.Values);
                var crossed = Thresholds.Where(t => window.UsedPercent >= t && !settings.NotifiedKeys.Contains(keys[t])).ToList();
                if (crossed.Count == 0)
                {
                    continue;
                }

                // Mark every crossed threshold but only announce the highest one.
                settings.NotifiedKeys.AddRange(crossed.Select(t => keys[t]));
                changed = true;
                if (settings.NotificationsEnabled)
                {
                    Show(snapshot, window, crossed.Max());
                }
            }
        }

        // Forget keys from past reset periods so the list stays small.
        changed |= settings.NotifiedKeys.RemoveAll(k => !currentKeys.Contains(k)) > 0;
        if (changed)
        {
            settings.Save();
        }
    }

    private void Show(UsageSnapshot snapshot, UsageWindow window, int threshold)
    {
        try
        {
            _notifier ??= CreateNotifier();
            var lines = new[]
            {
                UsageFormat.Format(Strings.Toast_Title, snapshot.DisplayName, window.UsedPercent),
                UsageFormat.Format(Strings.Toast_Body, window.Label, threshold),
                UsageFormat.ResetText(window.ResetsAt, DateTimeOffset.Now),
            };
            var xml = new XmlDocument();
            xml.LoadXml($"<toast><visual><binding template=\"ToastGeneric\">{string.Concat(lines.Select(l => $"<text>{SecurityElement.Escape(l)}</text>"))}</binding></visual></toast>");
            var toast = new ToastNotification(xml);
            toast.Activated += (_, _) => Invoked?.Invoke();
            _notifier.Show(toast);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Showing notification failed");
        }
    }

    // Windows App SDK's AppNotificationManager needs the Singleton package, which self-contained apps lack.
    // A per-user AUMID registration lets the OS toast API work without package identity.
    private static ToastNotifier CreateNotifier()
    {
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}"))
        {
            key.SetValue("DisplayName", "AI Usage");
            key.SetValue("IconUri", Path.Combine(AppContext.BaseDirectory, "Assets", "AIUsage.ico"));
        }

        return ToastNotificationManager.CreateToastNotifier(AppUserModelId);
    }
}
