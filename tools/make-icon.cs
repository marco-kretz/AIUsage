#:package System.Drawing.Common@10.0.12
#:property TargetFramework=net10.0-windows

// Generates the static app icon (exe, toasts): dotnet run tools/make-icon.cs src/AIUsage.App/Assets/AIUsage.ico
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

int[] sizes = [16, 20, 24, 32, 48, 64, 256];
var images = sizes.Select(Render).ToList();

using var file = File.Create(args[0]);
using var w = new BinaryWriter(file);
w.Write((short)0);
w.Write((short)1);
w.Write((short)sizes.Length);
var offset = 6 + 16 * sizes.Length;
for (var i = 0; i < sizes.Length; i++)
{
    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
    w.Write((byte)0);
    w.Write((byte)0);
    w.Write((short)1);
    w.Write((short)32);
    w.Write(images[i].Length);
    w.Write(offset);
    offset += images[i].Length;
}

images.ForEach(w.Write);

static byte[] Render(int size)
{
    using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bitmap))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var stroke = Math.Max(2f, size / 6.5f);
        var ring = new RectangleF(stroke / 2, stroke / 2, size - stroke, size - stroke);
        using var track = new Pen(Color.FromArgb(0x55, 0x80, 0x80, 0x80), stroke);
        using var arc = new Pen(Color.FromArgb(0x6C, 0xCB, 0x5F), stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var dot = new SolidBrush(Color.FromArgb(0xFF, 0xB0, 0x3A));
        g.DrawEllipse(track, ring);
        g.DrawArc(arc, ring, -90, 240);
        var r = size * 0.16f;
        g.FillEllipse(dot, size / 2f - r, size / 2f - r, 2 * r, 2 * r);
    }

    using var png = new MemoryStream();
    bitmap.Save(png, ImageFormat.Png);
    return png.ToArray();
}
