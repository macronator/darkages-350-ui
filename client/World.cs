namespace DarkAges350;

/// <summary>The floor-tile atlas (tilea.bmp): raw 8-bit-indexed 56x27 isometric diamond tiles,
/// concatenated. Tile 0 is blank; index 0 within a tile is the transparent diamond corner.</summary>
public sealed class TileAtlas
{
    public const int TW = 56, TH = 27;
    private const int TileBytes = TW * TH;
    private readonly byte[] _data;
    public int Count => _data.Length / TileBytes;

    public TileAtlas(byte[] data) => _data = data;
    public static TileAtlas FromArchive(DatArchive dat, string name = "tilea.bmp") => new(dat.Read(name));

    public ReadOnlySpan<byte> Tile(int id) =>
        (id > 0 && id < Count) ? _data.AsSpan(id * TileBytes, TileBytes) : ReadOnlySpan<byte>.Empty;

    private (byte r, byte g, byte b)[]? _avg;

    /// <summary>Average RGB of a floor tile's non-transparent pixels (for the minimap). Cached.</summary>
    public (byte r, byte g, byte b) Average(int id, Palette pal)
    {
        _avg ??= new (byte, byte, byte)[Count];
        if (id <= 0 || id >= Count) return (0, 0, 0);
        var c = _avg[id];
        if (c is not (0, 0, 0)) return c;
        long r = 0, g = 0, b = 0; int n = 0;
        var t = Tile(id);
        foreach (var idx in t)
        {
            if (idx == 0) continue;
            var (pr, pg, pb) = pal.Colors[idx];
            r += pr; g += pg; b += pb; n++;
        }
        c = n == 0 ? ((byte)0, (byte)0, (byte)0) : ((byte)(r / n), (byte)(g / n), (byte)(b / n));
        _avg[id] = c;
        return c;
    }
}

/// <summary>A map's cell grid (from a lod*.map: 6-byte cells = floor, leftWall, rightWall u16 LE)
/// and an isometric floor renderer.</summary>
public sealed class WorldMap
{
    public readonly int Width, Height;
    private readonly ushort[] _floor;

    public WorldMap(byte[] mapBytes, int width = 0)
    {
        int n = mapBytes.Length / 6;
        Width = width > 0 ? width : (int)Math.Round(Math.Sqrt(n));
        Height = Width > 0 ? n / Width : 0;
        _floor = new ushort[n];
        for (int i = 0; i < n; i++)
            _floor[i] = (ushort)(mapBytes[i * 6] | (mapBytes[i * 6 + 1] << 8));
    }

    public static WorldMap FromFile(string path, int width = 0) => new(File.ReadAllBytes(path), width);

    public ushort FloorAt(int mx, int my) =>
        (mx >= 0 && my >= 0 && mx < Width && my < Height) ? _floor[my * Width + mx] : (ushort)0;

    /// <summary>Draw a top-down minimap into a box: each cell = its floor tile's average colour,
    /// the camera cell marked. Scales the whole map to fit.</summary>
    public void DrawMinimap(Canvas c, TileAtlas atlas, Palette tilePal, int bx0, int by0, int bx1, int by1, int camX, int camY)
    {
        int bw = bx1 - bx0, bh = by1 - by0;
        if (bw <= 0 || bh <= 0) return;
        // fit the map into the box, preserving aspect; center it
        double s = Math.Min((double)bw / Width, (double)bh / Height);
        int mapW = (int)(Width * s), mapH = (int)(Height * s);
        int ox = bx0 + (bw - mapW) / 2, oy = by0 + (bh - mapH) / 2;
        for (int py = 0; py < mapH; py++)
        {
            int my = Math.Min(Height - 1, (int)(py / s));
            for (int px = 0; px < mapW; px++)
            {
                int mx = Math.Min(Width - 1, (int)(px / s));
                var (r, g, b) = atlas.Average(_floor[my * Width + mx], tilePal);
                Plot(c, ox + px, oy + py, r, g, b);
            }
        }
        // player marker
        int mxs = ox + (int)(camX * s), mys = oy + (int)(camY * s);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                Plot(c, mxs + dx, mys + dy, 255, 230, 120);
    }

    private static void Plot(Canvas c, int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || x >= c.W || y < 0 || y >= c.H) return;
        int o = (y * c.W + x) * 4; c.Rgba[o] = r; c.Rgba[o + 1] = g; c.Rgba[o + 2] = b; c.Rgba[o + 3] = 255;
    }

    /// <summary>Draw the floor layer into a viewport rectangle, camera-centered on cell (camX,camY).</summary>
    public void DrawFloor(Canvas c, TileAtlas atlas, Palette pal, int vx0, int vy0, int vx1, int vy1, int camX, int camY)
    {
        const int halfW = TileAtlas.TW / 2, stepY = 13;
        int cox = vx0 + (vx1 - vx0) / 2 - TileAtlas.TW / 2;
        int coy = vy0 + (vy1 - vy0) / 2 - TileAtlas.TH / 2;

        // back-to-front by (mx+my)
        var order = Enumerable.Range(0, _floor.Length)
            .OrderBy(i => (i % Width) + (i / Width));
        foreach (int i in order)
        {
            int mx = i % Width, my = i / Width;
            var tile = atlas.Tile(_floor[i]);
            if (tile.IsEmpty) continue;
            int sx = (mx - my - (camX - camY)) * halfW + cox;
            int sy = (mx + my - (camX + camY)) * stepY + coy;
            if (sx > vx1 || sy > vy1 || sx + TileAtlas.TW < vx0 || sy + TileAtlas.TH < vy0) continue;
            for (int y = 0; y < TileAtlas.TH; y++)
            {
                int dy = sy + y;
                if (dy < vy0 || dy >= vy1) continue;
                int row = y * TileAtlas.TW;
                for (int x = 0; x < TileAtlas.TW; x++)
                {
                    byte idx = tile[row + x];
                    if (idx == 0) continue;
                    int dx = sx + x;
                    if (dx < vx0 || dx >= vx1) continue;
                    var (r, g, b) = pal.Colors[idx];
                    int o = (dy * c.W + dx) * 4;
                    c.Rgba[o] = r; c.Rgba[o + 1] = g; c.Rgba[o + 2] = b; c.Rgba[o + 3] = 255;
                }
            }
        }
    }
}
