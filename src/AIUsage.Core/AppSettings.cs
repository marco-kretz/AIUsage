using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIUsage.Core;

public sealed class AppSettings
{
    public int PollIntervalSeconds { get; set; } = 180;
    public double WarningThreshold { get; set; } = 50;
    public double CriticalThreshold { get; set; } = 80;

    /// <summary>"providerId/windowId" keys that drive the tray icon; empty means all windows.</summary>
    public List<string> IconWindows { get; set; } = [];

    public bool NotificationsEnabled { get; set; }
    public string? CredentialsPath { get; set; }

    /// <summary>UI culture ("de" or "en"); defaults to German on German systems, English elsewhere.</summary>
    public string Language { get; set; } = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "de" ? "de" : "en";

    /// <summary>"providerId/windowId/resetPeriod/threshold" keys already notified, so each threshold fires once per reset.</summary>
    public List<string> NotifiedKeys { get; set; } = [];

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsage");

    private static string FilePath => Path.Combine(Directory, "settings.json");

    public static AppSettings Load(string? path = null)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path ?? FilePath), AppSettingsJsonContext.Default.AppSettings) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save(string? path = null)
    {
        path ??= FilePath;
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings));
    }

    public static string WindowKey(string providerId, string windowId) => $"{providerId}/{windowId}";
}

// Source-generated so the trimmed app needs no reflection-based serialization.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
