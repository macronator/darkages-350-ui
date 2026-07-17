namespace DarkAges350;

/// <summary>The Dark Ages bitmap font (eng00.fnt / eng01.fnt): 94 glyphs for printable ASCII
/// 0x21..0x7E, each 12 rows of 8-bit row bitmaps (MSB = leftmost pixel). Fixed 8x12 cell,
/// rendered proportionally (trailing empty columns trimmed).</summary>
public sealed class DaFont
{
    public const int CellH = 12, CellW = 8, First = 0x21;
    private readonly byte[][] _glyphs;   // [glyph][12 rows]
    private readonly int[] _widths;

    public DaFont(byte[] data)
    {
        int n = data.Length / CellH;
        _glyphs = new byte[n][];
        _widths = new int[n];
        for (int i = 0; i < n; i++)
        {
            var rows = data[(i * CellH)..(i * CellH + CellH)];
            _glyphs[i] = rows;
            int used = 0;
            foreach (var r in rows)
                for (int c = 0; c < CellW; c++)
                    if ((r & (0x80 >> c)) != 0) used = Math.Max(used, c + 1);
            _widths[i] = used == 0 ? 3 : used;
        }
    }

    public static DaFont Load(DatArchive dat, string name = "eng00.fnt") => new(dat.Read(name));

    public int Measure(string text, int tracking = 1, int space = 4, int scale = 1)
    {
        int w = 0;
        foreach (var ch in text)
        {
            int gi = ch - First;
            w += (ch == ' ' || gi < 0 || gi >= _glyphs.Length ? space : _widths[gi]) + tracking;
        }
        return w * scale;
    }

    public int Height(int scale = 1) => CellH * scale;

    /// <summary>Draw text into a canvas at (x,y) in the given colour.</summary>
    public void Draw(Canvas c, string text, int x, int y, (byte r, byte g, byte b) col,
                     int tracking = 1, int space = 4, int scale = 1)
    {
        int penX = x;
        foreach (var ch in text)
        {
            int gi = ch - First;
            if (ch == ' ' || gi < 0 || gi >= _glyphs.Length) { penX += (space + tracking) * scale; continue; }
            var rows = _glyphs[gi];
            for (int ry = 0; ry < CellH; ry++)
            {
                byte row = rows[ry];
                for (int cx = 0; cx < _widths[gi]; cx++)
                {
                    if ((row & (0x80 >> cx)) == 0) continue;
                    for (int sy = 0; sy < scale; sy++)
                        for (int sx = 0; sx < scale; sx++)
                            Plot(c, penX + cx * scale + sx, y + ry * scale + sy, col);
                }
            }
            penX += (_widths[gi] + tracking) * scale;
        }
    }

    private static void Plot(Canvas c, int x, int y, (byte r, byte g, byte b) col)
    {
        if (x < 0 || x >= c.W || y < 0 || y >= c.H) return;
        int o = (y * c.W + x) * 4;
        c.Rgba[o] = col.r; c.Rgba[o + 1] = col.g; c.Rgba[o + 2] = col.b; c.Rgba[o + 3] = 255;
    }
}
