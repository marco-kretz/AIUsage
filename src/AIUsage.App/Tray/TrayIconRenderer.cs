using AIUsage.Core;
using static AIUsage.App.Interop.GdiPlus;

namespace AIUsage.App.Tray;

public enum TrayIconKind
{
    Value,
    Pending,
    Error,
}

public sealed record TrayIconState(TrayIconKind Kind, double Percent = 0, UsageLevel Level = UsageLevel.Normal, bool Dimmed = false);

/// <summary>Draws the ring gauge icon with GDI+. Returns an HICON owned by the caller.</summary>
internal static class TrayIconRenderer
{
    public static nint Render(TrayIconState state, int size, bool lightTaskbar)
    {
        EnsureStarted();
        Check(GdipCreateBitmapFromScan0(size, size, 0, PixelFormat32bppPArgb, 0, out var bitmap));
        try
        {
            Check(GdipGetImageGraphicsContext(bitmap, out var graphics));
            try
            {
                GdipSetSmoothingMode(graphics, SmoothingModeAntiAlias);
                GdipSetPixelOffsetMode(graphics, PixelOffsetModeHighQuality);
                Draw(graphics, state, size, lightTaskbar);
            }
            finally
            {
                GdipDeleteGraphics(graphics);
            }

            Check(GdipCreateHICONFromBitmap(bitmap, out var icon));
            return icon;
        }
        finally
        {
            GdipDisposeImage(bitmap);
        }
    }

    private static void Draw(nint g, TrayIconState state, int size, bool light)
    {
        var alpha = state.Dimmed ? 110u : 255u;
        var foreground = Argb(alpha, light ? 0x1A1A1Au : 0xFFFFFFu);
        var track = light ? Argb(state.Dimmed ? 35u : 70u, 0x000000) : Argb(state.Dimmed ? 45u : 95u, 0xFFFFFF);

        var stroke = MathF.Max(2f, size / 6.5f);
        var (x, y, d) = (stroke / 2, stroke / 2, size - stroke);
        Check(GdipCreatePen1(track, stroke, UnitWorld, out var trackPen));
        GdipDrawEllipse(g, trackPen, x, y, d, d);
        GdipDeletePen(trackPen);

        string text;
        if (state.Kind == TrayIconKind.Value)
        {
            var percent = Math.Clamp(state.Percent, 0, 100);
            if (percent > 0)
            {
                Check(GdipCreatePen1(Argb(alpha, LevelColor(state.Level, light)), stroke, UnitWorld, out var arcPen));
                GdipSetPenStartCap(arcPen, LineCapRound);
                GdipSetPenEndCap(arcPen, LineCapRound);
                // Round caps overshoot by half the stroke; keep tiny values visible as a dot.
                GdipDrawArc(g, arcPen, x, y, d, d, -90, (float)Math.Max(percent * 3.6, 1));
                GdipDeletePen(arcPen);
            }

            text = Math.Round(state.Percent) >= 100 ? "100" : Math.Round(percent).ToString("0");
        }
        else
        {
            text = state.Kind == TrayIconKind.Pending ? "?" : "!";
        }

        DrawCenteredText(g, text, foreground, size, stroke);
    }

    private static void DrawCenteredText(nint g, string text, uint color, int size, float stroke)
    {
        Check(GdipCreateFontFamilyFromName("Segoe UI", 0, out var family));
        Check(GdipStringFormatGetGenericTypographic(out var format));
        Check(GdipCreatePath(0, out var path));
        Check(GdipCreateMatrix(out var matrix));
        Check(GdipCreateSolidFill(color, out var brush));
        try
        {
            GdipAddPathString(path, text, text.Length, family, FontStyleBold, 100, default, format);
            GdipGetPathWorldBounds(path, out var bounds, 0, 0);

            // Fit the glyphs into the ring's inner square, wider for 3 digits.
            var inner = size - 2 * stroke;
            var maxWidth = inner * (text.Length >= 3 ? 1.05f : 0.92f);
            var maxHeight = inner * 0.74f;
            var scale = MathF.Min(maxWidth / bounds.Width, maxHeight / bounds.Height);

            GdipTranslateMatrix(matrix, size / 2f, size / 2f, MatrixOrderPrepend);
            GdipScaleMatrix(matrix, scale, scale, MatrixOrderPrepend);
            GdipTranslateMatrix(matrix, -(bounds.X + bounds.Width / 2), -(bounds.Y + bounds.Height / 2), MatrixOrderPrepend);
            GdipTransformPath(path, matrix);
            GdipFillPath(g, brush, path);
        }
        finally
        {
            GdipDeleteBrush(brush);
            GdipDeleteMatrix(matrix);
            GdipDeletePath(path);
            GdipDeleteStringFormat(format);
            GdipDeleteFontFamily(family);
        }
    }

    private static uint Argb(uint alpha, uint rgb) => alpha << 24 | rgb;

    private static uint LevelColor(UsageLevel level, bool light) => level switch
    {
        UsageLevel.Critical => light ? 0xC42B1Cu : 0xFF6B5Eu,
        UsageLevel.Warning => light ? 0xC76A00u : 0xFFB03Au,
        _ => light ? 0x107C10u : 0x6CCB5Fu,
    };
}
