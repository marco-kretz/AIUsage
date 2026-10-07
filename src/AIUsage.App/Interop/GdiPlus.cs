using System.Runtime.InteropServices;

namespace AIUsage.App.Interop;

/// <summary>GDI+ flat API (what System.Drawing wraps), used directly so the app stays Native AOT compatible.</summary>
internal static partial class GdiPlus
{
    public const int PixelFormat32bppPArgb = 0xE200B;
    public const int SmoothingModeAntiAlias = 4;
    public const int PixelOffsetModeHighQuality = 2;
    public const int LineCapRound = 2;
    public const int UnitWorld = 0;
    public const int FontStyleBold = 1;
    public const int MatrixOrderPrepend = 0;

    private static readonly Lazy<nint> Token = new(() =>
    {
        var input = new StartupInput { GdiplusVersion = 1 };
        Check(GdiplusStartup(out var token, input, 0));
        return token;
    });

    [StructLayout(LayoutKind.Sequential)]
    public struct RectF
    {
        public float X, Y, Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInput
    {
        public uint GdiplusVersion;
        public nint DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    public static void EnsureStarted() => _ = Token.Value;

    public static void Check(int status)
    {
        if (status != 0)
        {
            throw new InvalidOperationException($"GDI+ call failed with status {status}.");
        }
    }

    [LibraryImport("gdiplus.dll")]
    private static partial int GdiplusStartup(out nint token, in StartupInput input, nint output);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, nint scan0, out nint bitmap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGetImageGraphicsContext(nint image, out nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetSmoothingMode(nint graphics, int mode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPixelOffsetMode(nint graphics, int mode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePen1(uint argb, float width, int unit, out nint pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenStartCap(nint pen, int cap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenEndCap(nint pen, int cap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawEllipse(nint graphics, nint pen, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawArc(nint graphics, nint pen, float x, float y, float width, float height, float startAngle, float sweepAngle);

    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipCreateFontFamilyFromName(string name, nint fontCollection, out nint fontFamily);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipStringFormatGetGenericTypographic(out nint format);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePath(int fillMode, out nint path);

    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipAddPathString(nint path, string text, int length, nint fontFamily, int style, float emSize, in RectF layout, nint format);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGetPathWorldBounds(nint path, out RectF bounds, nint matrix, nint pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateMatrix(out nint matrix);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipTranslateMatrix(nint matrix, float offsetX, float offsetY, int order);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipScaleMatrix(nint matrix, float scaleX, float scaleY, int order);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipTransformPath(nint path, nint matrix);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateSolidFill(uint argb, out nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillPath(nint graphics, nint brush, nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateHICONFromBitmap(nint bitmap, out nint hicon);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteGraphics(nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDisposeImage(nint image);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePen(nint pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteBrush(nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePath(nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteMatrix(nint matrix);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteFontFamily(nint fontFamily);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteStringFormat(nint format);
}
