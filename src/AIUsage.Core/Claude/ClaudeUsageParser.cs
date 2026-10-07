using System.Globalization;
using System.Text.Json;
using AIUsage.Core.Resources;

namespace AIUsage.Core.Claude;

/// <summary>
/// Parses the undocumented /api/oauth/usage response. Every field may be null, missing or of an
/// unexpected type; such entries are skipped instead of failing the whole response.
/// </summary>
public static class ClaudeUsageParser
{
    /// <exception cref="JsonException">The payload is not a JSON object.</exception>
    public static IReadOnlyList<UsageWindow> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Usage response is not a JSON object.");
        }

        var windows = new List<UsageWindow>();
        AddWindow(windows, root, "five_hour", Strings.Window_FiveHour);
        AddWindow(windows, root, "seven_day", Strings.Window_SevenDay);
        AddWindow(windows, root, "seven_day_opus", Format(Strings.Window_SevenDayModel, "Opus"));
        AddWindow(windows, root, "seven_day_sonnet", Format(Strings.Window_SevenDayModel, "Sonnet"));
        AddScopedWeeklyLimits(windows, root);
        AddWindow(windows, root, "extra_usage", Strings.Window_ExtraUsage);
        return windows;
    }

    private static void AddWindow(List<UsageWindow> windows, JsonElement root, string id, string label)
    {
        if (!root.TryGetProperty(id, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (TryGetDouble(window, "utilization", out var utilization))
        {
            windows.Add(new UsageWindow(id, label, utilization, GetDate(window, "resets_at")));
        }
    }

    // Model-scoped weekly limits (e.g. a new model family) only show up in "limits", not as seven_day_* fields.
    private static void AddScopedWeeklyLimits(List<UsageWindow> windows, JsonElement root)
    {
        if (!root.TryGetProperty("limits", out var limits) || limits.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var limit in limits.EnumerateArray())
        {
            if (limit.ValueKind != JsonValueKind.Object
                || GetString(limit, "kind") != "weekly_scoped"
                || !limit.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object
                || !scope.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.Object
                || GetString(model, "display_name") is not { Length: > 0 } modelName
                || !TryGetDouble(limit, "percent", out var percent))
            {
                continue;
            }

            var id = "seven_day_" + modelName.ToLowerInvariant();
            if (windows.All(w => w.Id != id))
            {
                windows.Add(new UsageWindow(id, Format(Strings.Window_SevenDayModel, modelName), percent, GetDate(limit, "resets_at")));
            }
        }
    }

    private static bool TryGetDouble(JsonElement obj, string name, out double value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out value);
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static DateTimeOffset? GetDate(JsonElement obj, string name) =>
        DateTimeOffset.TryParse(GetString(obj, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    private static string Format(string format, string arg) => string.Format(CultureInfo.CurrentCulture, format, arg);
}
