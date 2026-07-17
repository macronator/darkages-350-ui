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
