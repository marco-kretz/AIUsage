using System.Globalization;
using AIUsage.Core.Resources;

namespace AIUsage.Core;

public enum UsageLevel
{
    Normal,
    Warning,
    Critical,
}

/// <summary>Provider-agnostic presentation rules shared by tray icon, tooltip and flyout.</summary>
public static class UsageFormat
{
    public static UsageLevel Level(double percent, AppSettings settings) =>
        percent >= settings.CriticalThreshold ? UsageLevel.Critical
        : percent >= settings.WarningThreshold ? UsageLevel.Warning
        : UsageLevel.Normal;

    /// <summary>"Zurücksetzung in 2 Std 14 Min"; empty if the reset time is unknown.</summary>
    public static string ResetText(DateTimeOffset? resetsAt, DateTimeOffset now) =>
        resetsAt is { } at
            ? at - now < TimeSpan.FromMinutes(1) ? Strings.Reset_Soon : Format(Strings.Reset_In, Duration(at - now))
            : "";

    public static string Duration(TimeSpan span)
    {
        // Round up so "in 0 Min" never shows while time remains.
        var minutes = (int)Math.Ceiling(span.TotalMinutes);
        var (d, h, m) = (minutes / 1440, minutes / 60 % 24, minutes % 60);
        return d > 0 ? Format(Strings.Duration_DaysHours, d, h)
            : h > 0 ? Format(Strings.Duration_HoursMinutes, h, m)
            : Format(Strings.Duration_Minutes, m);
    }

    public static string? StatusMessage(UsageStatus status) => status switch
    {
        UsageStatus.Pending => Strings.Status_Pending,
        UsageStatus.NoCredentials => Strings.Status_NoCredentials,
        UsageStatus.AuthFailed => Strings.Status_AuthFailed,
        UsageStatus.RateLimited => Strings.Status_RateLimited,
        UsageStatus.Offline => Strings.Status_Offline,
        UsageStatus.Stale => Strings.Status_Stale,
        _ => null,
    };

    /// <summary>Windows that drive the tray icon according to the settings (all if none selected or none match).</summary>
    public static IEnumerable<UsageWindow> IconWindows(UsageSnapshot snapshot, AppSettings settings)
    {
        if (settings.IconWindows.Count == 0)
        {
            return snapshot.Windows;
        }

        var selected = snapshot.Windows.Where(w => settings.IconWindows.Contains(AppSettings.WindowKey(snapshot.ProviderId, w.Id))).ToList();
        return selected.Count > 0 ? selected : snapshot.Windows;
    }

    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
