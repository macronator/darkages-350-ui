using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DarkAges350;

/// <summary>Bridges the palette-indexed sprite readers to GDI+ Bitmaps for on-screen display.</summary>
public static class Gdi
{
    /// <summary>Render one EPF frame to a 32bpp ARGB Bitmap. Index 0 is transparent unless opaque.</summary>
    public static Bitmap FrameToBitmap(EpfFrame f, Palette pal, bool opaque = false, int transparent = 0)
    {
        int w = Math.Max(1, f.Width), h = Math.Max(1, f.Height);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var buf = new byte[w * h * 4]; // BGRA in memory for Format32bppArgb
        for (int i = 0; i < f.Width * f.Height && i < f.Pixels.Length; i++)
        {
            byte idx = f.Pixels[i];
            int o = i * 4;
            if (!opaque && idx == transparent) { buf[o + 3] = 0; continue; }
            var (r, g, b) = pal.Colors[idx];
            buf[o] = b; buf[o + 1] = g; buf[o + 2] = r; buf[o + 3] = 255;
        }
        CopyInto(bmp, buf, w, h);
        return bmp;
    }

    /// <summary>Render one MPF creature frame to a 32bpp ARGB Bitmap (index 0 transparent).</summary>
    public static Bitmap MpfToBitmap(MpfFrame f, Palette pal)
    {
        int w = Math.Max(1, f.Width), h = Math.Max(1, f.Height);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var buf = new byte[w * h * 4];
        for (int i = 0; i < f.Width * f.Height && i < f.Pixels.Length; i++)
        {
            byte idx = f.Pixels[i];
            int o = i * 4;
            if (idx == 0) { buf[o + 3] = 0; continue; }
            var (r, g, b) = pal.Colors[idx];
            buf[o] = b; buf[o + 1] = g; buf[o + 2] = r; buf[o + 3] = 255;
        }
        CopyInto(bmp, buf, w, h);
        return bmp;
    }

    /// <summary>Convert a composed RGBA Canvas to a Bitmap.</summary>
    public static Bitmap ToBitmap(Canvas c)
    {
        var buf = new byte[c.W * c.H * 4];
        for (int i = 0; i < c.W * c.H; i++)
        {
            int o = i * 4;
            buf[o] = c.Rgba[o + 2]; buf[o + 1] = c.Rgba[o + 1]; buf[o + 2] = c.Rgba[o]; buf[o + 3] = c.Rgba[o + 3];
        }
        var bmp = new Bitmap(c.W, c.H, PixelFormat.Format32bppArgb);
        CopyInto(bmp, buf, c.W, c.H);
        return bmp;
    }

    private static void CopyInto(Bitmap bmp, byte[] bgra, int w, int h)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            if (data.Stride == w * 4)
            {
                Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
            }
            else
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(bgra, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            }
        }
        finally { bmp.UnlockBits(data); }
    }
}
