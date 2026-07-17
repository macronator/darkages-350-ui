using System.Text;

namespace DarkAges350;

/// <summary>Reader for a Dark Ages .dat archive (DarkAges.dat, Legend.dat, ...).
/// Layout: u32 count; count x { u32 offset; char name[13] }; then file data.
/// A file's size is the next entry's offset minus its own.</summary>
public sealed class DatArchive
{
    private readonly byte[] _raw;
    private readonly List<(uint off, string name)> _entries = new();

    public DatArchive(string path)
    {
        _raw = File.ReadAllBytes(path);
        int n = (int)BitConverter.ToUInt32(_raw, 0);
        for (int i = 0; i < n; i++)
        {
            int p = 4 + i * 17;
            uint off = BitConverter.ToUInt32(_raw, p);
            string name = Encoding.Latin1.GetString(_raw, p + 4, 13);
            int z = name.IndexOf('\0');
            if (z >= 0) name = name[..z];
            _entries.Add((off, name));
        }
    }

    public byte[] Read(string name)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].name, name, StringComparison.OrdinalIgnoreCase))
            {
                uint end = (i + 1 < _entries.Count) ? _entries[i + 1].off : (uint)_raw.Length;
                return _raw[(int)_entries[i].off..(int)end];
            }
        }
        throw new KeyNotFoundException(name);
    }

    public IEnumerable<string> Names => _entries.Where(e => e.name.Length > 0).Select(e => e.name);
}

/// <summary>A 256-colour palette. DAC palettes store 0-63 per channel; those are scaled to 0-255.</summary>
public sealed class Palette
{
    public readonly (byte r, byte g, byte b)[] Colors = new (byte, byte, byte)[256];

    public Palette(byte[] data)
    {
        byte max = 0;
        for (int i = 0; i < 768; i++) if (data[i] > max) max = data[i];
        bool dac = max <= 63;
        for (int i = 0; i < 256; i++)
        {
            byte r = data[i * 3], g = data[i * 3 + 1], b = data[i * 3 + 2];
            Colors[i] = dac ? ((byte)(r << 2), (byte)(g << 2), (byte)(b << 2)) : (r, g, b);
        }
    }
}

public sealed class EpfFrame
{
    public int Top, Left, Width, Height;
    public byte[] Pixels = Array.Empty<byte>(); // 8-bit palette indices
}

/// <summary>Reader for an .epf sprite: u16 frameCount,width,height,pad; u32 tocOffset;
/// pixels; then a frame table of { u16 top,left,bottom,right; u32 start,end }.</summary>
public sealed class Epf
{
    public int FrameCount, Width, Height;
    public readonly List<EpfFrame> Frames = new();

    public Epf(byte[] d)
    {
        FrameCount = BitConverter.ToUInt16(d, 0);
        Width = BitConverter.ToUInt16(d, 2);
        Height = BitConverter.ToUInt16(d, 4);
        uint toc = BitConverter.ToUInt32(d, 8);
        int baseOff = 12 + (int)toc;
        for (int i = 0; i < FrameCount; i++)
        {
            int p = baseOff + i * 16;
            int top = BitConverter.ToUInt16(d, p);
            int left = BitConverter.ToUInt16(d, p + 2);
            int bottom = BitConverter.ToUInt16(d, p + 4);
            int right = BitConverter.ToUInt16(d, p + 6);
            uint start = BitConverter.ToUInt32(d, p + 8);
            int w = right - left, h = bottom - top;
            var f = new EpfFrame { Top = top, Left = left, Width = w, Height = h };
            if (w > 0 && h > 0)
            {
                int need = w * h, from = 12 + (int)start;
                f.Pixels = (from >= 0 && from + need <= d.Length) ? d[from..(from + need)] : new byte[need];
            }
            Frames.Add(f);
        }
    }

    public EpfFrame FirstDrawable => Frames.First(f => f.Width > 0 && f.Height > 0);
}
