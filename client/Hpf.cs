namespace DarkAges350;

/// <summary>Reader for .hpf wall/static-object tiles (stc*.hpf), ported from DALib. Each tile is a
/// single 28-px-wide palettized image, optionally compressed with the adaptive-Huffman scheme
/// (signature 0xFF02AA55). Height = pixels / 28. Index 0 is transparent.</summary>
public sealed class HpfTile
{
    public const int Width = 28;
    public int Height => Pixels.Length / Width;
    public readonly byte[] Pixels;

    private HpfTile(byte[] pixels) => Pixels = pixels;

    public static HpfTile FromArchive(DatArchive dat, string name)
    {
        var d = dat.Read(name.EndsWith(".hpf", StringComparison.OrdinalIgnoreCase) ? name : name + ".hpf");
        if (d.Length >= 4 && BitConverter.ToUInt32(d, 0) == 0xFF02AA55)
            d = Decompress(d);
        return new HpfTile(d.Length > 8 ? d[8..] : Array.Empty<byte>());   // skip 8 header bytes
    }

    public static HpfTile ForId(DatArchive dat, int id) => FromArchive(dat, $"stc{id:D5}.hpf");

    /// <summary>Lazily loads + caches wall/object tiles by id (the map's left/right-wall values).</summary>
    public sealed class WallCache
    {
        private readonly DatArchive _dat;
        private readonly Dictionary<int, HpfTile?> _cache = new();
        public WallCache(DatArchive dat) => _dat = dat;
        public HpfTile? Get(int id)
        {
            if (id <= 0 || id >= 7249) return null;
            if (!_cache.TryGetValue(id, out var t))
            {
                try { t = HpfTile.ForId(_dat, id); } catch { t = null; }
                _cache[id] = t;
            }
            return t;
        }
    }

    /// <summary>Adaptive-Huffman decompressor (Eru/illuvatar's method, per DALib.Compression.DecompressHpf).</summary>
    private static byte[] Decompress(byte[] buffer)
    {
        uint k = 7, val = 0, l = 0;
        int m = 0;
        var raw = new byte[buffer.Length * 10];
        var intOdd = new uint[256];
        var intEven = new uint[256];
        var bytePair = new byte[513];
        for (uint i = 0; i < 256; i++)
        {
            intOdd[i] = 2 * i + 1;
            intEven[i] = 2 * i + 2;
            bytePair[i * 2 + 1] = (byte)i;
            bytePair[i * 2 + 2] = (byte)i;
        }
        while (val != 0x100)
        {
            val = 0;
            while (val <= 0xFF)
            {
                if (k == 7) { l++; k = 0; } else k++;
                val = (buffer[4 + (int)l - 1] & (1 << (int)k)) != 0 ? intEven[val] : intOdd[val];
            }
            uint val3 = val, val2 = bytePair[val];
            while (val3 != 0 && val2 != 0)
            {
                uint i = bytePair[val2];
                uint j = intOdd[i];
                if (j == val2) { j = intEven[i]; intEven[i] = val3; }
                else intOdd[i] = val3;
                if (intOdd[val2] == val3) intOdd[val2] = j; else intEven[val2] = j;
                bytePair[val3] = (byte)i;
                bytePair[j] = (byte)val2;
                val3 = i;
                val2 = bytePair[val3];
            }
            val = (val + 0xFFFFFF00) & 0xFFFFFFFF;
            if (val == 0x100) continue;
            raw[m++] = (byte)val;
        }
        return raw[..m];
    }
}
