using System.IO.Compression;

namespace DarkAges350;

/// <summary>A 32-bit RGBA canvas with a minimal, dependency-free PNG writer.</summary>
public sealed class Canvas
{
    public readonly int W, H;
    public readonly byte[] Rgba;

    public Canvas(int w, int h)
    {
        W = w; H = h; Rgba = new byte[w * h * 4];
    }

    public void Fill(byte r, byte g, byte b, byte a = 255)
    {
        for (int i = 0; i < W * H; i++)
        {
            int o = i * 4; Rgba[o] = r; Rgba[o + 1] = g; Rgba[o + 2] = b; Rgba[o + 3] = a;
        }
    }

    /// <summary>Blit an indexed EPF frame at (x,y). Index 0 is transparent unless opaque.</summary>
    public void Blit(EpfFrame f, Palette pal, int x, int y, bool opaque = false, int transparent = 0)
    {
        for (int py = 0; py < f.Height; py++)
        {
            int dy = y + py;
            if (dy < 0 || dy >= H) continue;
            for (int px = 0; px < f.Width; px++)
            {
                int dx = x + px;
                if (dx < 0 || dx >= W) continue;
                byte idx = f.Pixels[py * f.Width + px];
                if (!opaque && idx == transparent) continue;
                var (r, g, b) = pal.Colors[idx];
                int o = (dy * W + dx) * 4;
                Rgba[o] = r; Rgba[o + 1] = g; Rgba[o + 2] = b; Rgba[o + 3] = 255;
            }
        }
    }

    /// <summary>Draw a rectangle outline (for the region-map overlay).</summary>
    public void Outline(int l, int t, int r, int b, byte cr, byte cg, byte cb, int th = 2)
    {
        void Px(int x, int y)
        {
            if (x < 0 || x >= W || y < 0 || y >= H) return;
            int o = (y * W + x) * 4; Rgba[o] = cr; Rgba[o + 1] = cg; Rgba[o + 2] = cb; Rgba[o + 3] = 255;
        }
        for (int k = 0; k < th; k++)
        {
            for (int x = l; x < r; x++) { Px(x, t + k); Px(x, b - 1 - k); }
            for (int y = t; y < b; y++) { Px(l + k, y); Px(r - 1 - k, y); }
        }
    }

    public void SavePng(string path)
    {
        using var fs = File.Create(path);
        Span<byte> sig = stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        fs.Write(sig);

        // IHDR
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)W); WriteBE(ihdr, 4, (uint)H);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // colour type RGBA
        WriteChunk(fs, "IHDR", ihdr);

        // IDAT: filter-0 per scanline, zlib-compressed
        using var raw = new MemoryStream();
        for (int y = 0; y < H; y++)
        {
            raw.WriteByte(0);
            raw.Write(Rgba, y * W * 4, W * 4);
        }
        using var comp = new MemoryStream();
        using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true))
            raw.WriteTo(z);
        WriteChunk(fs, "IDAT", comp.ToArray());

        WriteChunk(fs, "IEND", Array.Empty<byte>());
    }

    private static void WriteBE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    private static void WriteChunk(Stream fs, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        len[0] = (byte)(data.Length >> 24); len[1] = (byte)(data.Length >> 16);
        len[2] = (byte)(data.Length >> 8); len[3] = (byte)data.Length;
        fs.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        fs.Write(typeBytes);
        fs.Write(data);
        uint crc = Crc32(typeBytes, data);
        Span<byte> c = stackalloc byte[4];
        c[0] = (byte)(crc >> 24); c[1] = (byte)(crc >> 16); c[2] = (byte)(crc >> 8); c[3] = (byte)crc;
        fs.Write(c);
    }

    private static readonly uint[] CrcTable = BuildCrc();
    private static uint[] BuildCrc()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
    private static uint Crc32(byte[] a, byte[] b)
    {
        uint c = 0xFFFFFFFF;
        foreach (var x in a) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in b) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
}
