using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using AIUsage.Core;

namespace AIUsage.App.Tray;

public enum TrayIconKind
{
    Value,
    Pending,
    Error,
}

public sealed record TrayIconState(TrayIconKind Kind, double Percent = 0, UsageLevel Level = UsageLevel.Normal, bool Dimmed = false);

/// <summary>Draws the ring gauge icon. Returns an HICON owned by the caller.</summary>
internal static class TrayIconRenderer
{
    public static nint Render(TrayIconState state, int size, bool lightTaskbar)
    {
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Draw(g, state, size, lightTaskbar);
        }

        return bitmap.GetHicon();
    }

    private static void Draw(Graphics g, TrayIconState state, int size, bool light)
    {
        var alpha = state.Dimmed ? 110 : 255;
        var foreground = Color.FromArgb(alpha, light ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.White);
        var track = light ? Color.FromArgb(state.Dimmed ? 35 : 70, 0, 0, 0) : Color.FromArgb(state.Dimmed ? 45 : 95, 255, 255, 255);

        var stroke = Math.Max(2f, size / 6.5f);
        var ring = new RectangleF(stroke / 2, stroke / 2, size - stroke, size - stroke);
        using (var trackPen = new Pen(track, stroke))
        {
            g.DrawEllipse(trackPen, ring);
        }

        string text;
        if (state.Kind == TrayIconKind.Value)
        {
            var percent = Math.Clamp(state.Percent, 0, 100);
            if (percent > 0)
            {
                using var arcPen = new Pen(Color.FromArgb(alpha, LevelColor(state.Level, light)), stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                // Round caps overshoot by half the stroke; keep tiny values visible as a dot.
                g.DrawArc(arcPen, ring, -90, (float)Math.Max(percent * 3.6, 1));
            }

            text = Math.Round(state.Percent) >= 100 ? "100" : Math.Round(percent).ToString("0");
        }
        else
        {
            text = state.Kind == TrayIconKind.Pending ? "?" : "!";
        }

        DrawCenteredText(g, text, foreground, size, stroke);
    }

    private static void DrawCenteredText(Graphics g, string text, Color color, int size, float stroke)
    {
        using var path = new GraphicsPath();
        using var family = new FontFamily("Segoe UI");
        path.AddString(text, family, (int)FontStyle.Bold, 100, PointF.Empty, StringFormat.GenericTypographic);
        var bounds = path.GetBounds();

        // Fit the glyphs into the ring's inner square, wider for 3 digits.
        var inner = size - 2 * stroke;
        var maxWidth = inner * (text.Length >= 3 ? 1.05f : 0.92f);
        var maxHeight = inner * 0.74f;
        var scale = Math.Min(maxWidth / bounds.Width, maxHeight / bounds.Height);

        using var matrix = new Matrix();
        matrix.Translate(size / 2f, size / 2f);
        matrix.Scale(scale, scale);
        matrix.Translate(-(bounds.Left + bounds.Width / 2), -(bounds.Top + bounds.Height / 2));
        path.Transform(matrix);

        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    private static Color LevelColor(UsageLevel level, bool light) => level switch
    {
        UsageLevel.Critical => light ? Color.FromArgb(0xC4, 0x2B, 0x1C) : Color.FromArgb(0xFF, 0x6B, 0x5E),
        UsageLevel.Warning => light ? Color.FromArgb(0xC7, 0x6A, 0x00) : Color.FromArgb(0xFF, 0xB0, 0x3A),
        _ => light ? Color.FromArgb(0x10, 0x7C, 0x10) : Color.FromArgb(0x6C, 0xCB, 0x5F),
    };
}
