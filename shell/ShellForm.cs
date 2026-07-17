using System.Drawing;
using System.Windows.Forms;

namespace DarkAges350;

/// <summary>A live 640x480 Dark Ages v3.50 client shell: background chrome, HP/MP orbs whose
/// fill you drive from the keyboard, and draggable window sprites you open with the number keys.
/// All geometry comes from ui-350.json; all pixels from DarkAges.dat.</summary>
public sealed class ShellForm : Form
{
    private readonly DatArchive _dat;
    private readonly UiSpec _spec;
    private readonly Palette _pal;
    private readonly Bitmap _bg;
    private readonly Bitmap[] _hpFrames, _mpFrames;
    private readonly DaFont _font;
    private static readonly (byte r, byte g, byte b) Amber = (235, 215, 150);
    private readonly Dictionary<string, Bitmap> _winCache = new(StringComparer.OrdinalIgnoreCase);

    // world (viewport) — walkable via the arrow keys
    private WorldMap? _worldMap;
    private TileAtlas? _worldAtlas;
    private Palette? _tilePal;
    private Bitmap? _worldBmp;
    private int _camX, _camY;
    private static readonly Rectangle Viewport = new(2, 2, 608, 313);
    private readonly List<(int mx, int my, MpfSprite spr)> _monsters = new();

    // animated weather overlay (falling rain), scrolled by a timer
    private Bitmap? _weather;
    private int _weatherY;
    private bool _weatherOn = true;
    private System.Windows.Forms.Timer? _rainTimer;

    private int _hp = 100, _mp = 100;
    private readonly List<OpenWin> _open = new();
    private OpenWin? _drag;
    private Point _dragOffset;

    // number keys 1..9 -> window asset
    private static readonly string[] Slots =
        { "stat001.epf", "equip01.epf", "spell001.epf", "skill001.epf", "exchange.epf",
          "friend.epf", "gset01.epf", "macro01.epf", "option01.epf" };

    private sealed class OpenWin { public required string Asset; public int X, Y; public required Bitmap Bmp; }

    public ShellForm(DatArchive dat, UiSpec spec)
    {
        _dat = dat; _spec = spec;
        _pal = new Palette(dat.Read(spec.DefaultUiPalette));

        _font = DaFont.Load(dat, "eng00.fnt");

        var bgCanvas = new Canvas(spec.Space.Width, spec.Space.Height);
        bgCanvas.Blit(new Epf(dat.Read(spec.Constants.BackgroundAsset)).FirstDrawable, _pal, 0, 0, opaque: true);

        // load the walkable world if a map + tile atlas are available (DA_MAP env or lod1.map next to
        // the exe). Rendered per-camera in RenderWorld(); falls back to a black viewport.
        try
        {
            string? mapPath = Environment.GetEnvironmentVariable("DA_MAP");
            if (string.IsNullOrEmpty(mapPath) || !File.Exists(mapPath))
            {
                var cand = Path.Combine(AppContext.BaseDirectory, "lod1.map");
                if (File.Exists(cand)) mapPath = cand;
            }
            if (!string.IsNullOrEmpty(mapPath) && File.Exists(mapPath))
            {
                _worldAtlas = TileAtlas.FromArchive(dat);
                _tilePal = new Palette(dat.Read("field001.pal"));
                _worldMap = WorldMap.FromFile(mapPath);
                _camX = _worldMap.Width / 2; _camY = _worldMap.Height / 2;
                // scatter a few creatures near the start so you meet them while walking
                foreach (var (nm, dx, dy) in new[] { ("mns001", 2, -1), ("mns010", -3, 1), ("mns030", 1, 3), ("mns050", 4, 2), ("mns010", -2, -3) })
                    try { _monsters.Add((_camX + dx, _camY + dy, MpfSprite.FromArchive(dat, nm))); } catch { }
            }
        }
        catch { _worldMap = null; }

        // static chat lines drawn into the chat panel with the real DA font
        if (spec.Constants.ChatPanelRect is Rect cp)
        {
            string[] chat = { "[System] Welcome to Temuair.", "Deoxys: reconstruction online!", "Aisling the Wizard has entered." };
            int y = cp.Top + 6;
            foreach (var line in chat) { _font.Draw(bgCanvas, line, cp.Left + 10, y, Amber); y += _font.Height() + 3; }
        }
        _bg = Gdi.ToBitmap(bgCanvas);
        RenderWorld();

        // weather overlay (rain), animated by a timer
        try
        {
            var rain = new Epf(dat.Read("rain01.epf")).FirstDrawable;
            _weather = Gdi.FrameToBitmap(rain, _pal);            // index 0 transparent
            _rainTimer = new System.Windows.Forms.Timer { Interval = 90 };
            _rainTimer.Tick += (_, _) => { if (_weatherOn && _weather != null) { _weatherY = (_weatherY + 4) % _weather.Height; Invalidate(Viewport); } };
            _rainTimer.Start();
        }
        catch { _weather = null; }

        _hpFrames = LoadFrames(spec.Constants.OrbHpAsset);
        _mpFrames = LoadFrames(spec.Constants.OrbMpAsset);

        Text = "Dark Ages 3.50 — client shell";
        ClientSize = new Size(spec.Space.Width, spec.Space.Height);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        DoubleBuffered = true;
        BackColor = Color.Black;
        KeyPreview = true;
        UpdateTitle();
    }

    /// <summary>Open a set of windows up front (used for headless frame capture).</summary>
    public void PreOpen(IEnumerable<string> assets)
    {
        foreach (var n in assets)
            ToggleWindow(n.EndsWith(".epf", StringComparison.OrdinalIgnoreCase) ? n : n + ".epf");
    }

    /// <summary>Set orb levels programmatically (used for headless frame capture).</summary>
    public void SetLevels(int hp, int mp) { _hp = Math.Clamp(hp, 0, 100); _mp = Math.Clamp(mp, 0, 100); UpdateTitle(); Invalidate(); }

    /// <summary>Set the world camera cell (used for headless frame capture).</summary>
    public void SetCamera(int x, int y) { _camX = x; _camY = y; RenderWorld(); UpdateTitle(); Invalidate(); }

    /// <summary>Render the world floor at the current camera into a transparent viewport bitmap.</summary>
    private void RenderWorld()
    {
        if (_worldMap == null || _worldAtlas == null || _tilePal == null) return;
        _camX = Math.Clamp(_camX, 0, _worldMap.Width - 1);
        _camY = Math.Clamp(_camY, 0, _worldMap.Height - 1);
        var c = new Canvas(_spec.Space.Width, _spec.Space.Height);   // 640x480, transparent (matches the composite)
        _worldMap.DrawFloor(c, _worldAtlas, _tilePal,
            Viewport.Left, Viewport.Top, Viewport.Right, Viewport.Bottom, _camX, _camY);
        // creatures on the map, depth-sorted (farther first) so nearer ones overlap correctly
        foreach (var m in _monsters.OrderBy(m => m.mx + m.my))
        {
            if (m.spr.Frames.Count == 0) continue;
            var (sx, sy) = WorldMap.TileTopLeft(m.mx, m.my, Viewport.Left, Viewport.Top, Viewport.Right, Viewport.Bottom, _camX, _camY);
            WorldMap.DrawSprite(c, m.spr.Frames[0], _pal, sx + TileAtlas.TW / 2, sy + 24,
                Viewport.Left, Viewport.Top, Viewport.Right, Viewport.Bottom);
        }
        // minimap in its socket, marking the current position
        if (_spec.Constants.MinimapRect is Rect mm)
            _worldMap.DrawMinimap(c, _worldAtlas, _tilePal, mm.Left, mm.Top, mm.Right, mm.Bottom, _camX, _camY);
        _worldBmp?.Dispose();
        _worldBmp = Gdi.ToBitmap(c);
    }

    private Bitmap[] LoadFrames(string asset)
    {
        try { return new Epf(_dat.Read(asset)).Drawable.Select(f => Gdi.FrameToBitmap(f, _pal)).ToArray(); }
        catch (KeyNotFoundException) { return Array.Empty<Bitmap>(); }
    }

    private static Bitmap? Fill(Bitmap[] frames, int pct)
    {
        if (frames.Length == 0) return null;
        int idx = (int)Math.Round((100 - Math.Clamp(pct, 0, 100)) / 100.0 * (frames.Length - 1));
        return frames[Math.Clamp(idx, 0, frames.Length - 1)];
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(_bg, 0, 0);
        if (_worldBmp != null) g.DrawImageUnscaled(_worldBmp, 0, 0);   // world tiles over the black viewport
        if (_weatherOn && _weather != null)                           // scrolling rain, clipped to the viewport
        {
            var save = g.Clip;
            g.SetClip(Viewport);
            g.DrawImageUnscaled(_weather, Viewport.Left, Viewport.Top - _weatherY);
            g.DrawImageUnscaled(_weather, Viewport.Left, Viewport.Top - _weatherY + _weather.Height);
            g.Clip = save;
        }
        DrawOrb(g, _hpFrames, _hp, _spec.Constants.OrbHpRect);
        DrawOrb(g, _mpFrames, _mp, _spec.Constants.OrbMpRect);
        foreach (var w in _open) g.DrawImageUnscaled(w.Bmp, w.X, w.Y);
        // live status text (updates as HP/MP change) drawn with the DA font, top-left of the viewport
        using var status = TextBitmap($"Deoxys the Wizard   HP {_hp}%  MP {_mp}%", Amber);
        g.DrawImageUnscaled(status, 6, 6);
    }

    private Bitmap TextBitmap(string s, (byte r, byte g, byte b) col, int scale = 1)
    {
        int w = Math.Max(1, _font.Measure(s, scale: scale) + 1);
        int h = Math.Max(1, _font.Height(scale) + 1);
        var c = new Canvas(w, h);                 // zeroed => transparent; DaFont sets alpha only on glyph pixels
        _font.Draw(c, s, 0, 0, col, scale: scale);
        return Gdi.ToBitmap(c);
    }

    private static void DrawOrb(Graphics g, Bitmap[] frames, int pct, Rect? rect)
    {
        if (rect == null) return;
        var bmp = Fill(frames, pct);
        if (bmp != null) g.DrawImageUnscaled(bmp, rect.Left, rect.Top);
    }

    private Bitmap? WindowBitmap(string asset)
    {
        if (_winCache.TryGetValue(asset, out var b)) return b;
        try
        {
            b = Gdi.FrameToBitmap(new Epf(_dat.Read(asset)).FirstDrawable, _pal);
            _winCache[asset] = b; return b;
        }
        catch (KeyNotFoundException) { return null; }
    }

    private readonly Dictionary<string, Point> _lastPos = new(StringComparer.OrdinalIgnoreCase);
    private int _cascade;

    private void ToggleWindow(string asset)
    {
        var existing = _open.FirstOrDefault(w => w.Asset.Equals(asset, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { _lastPos[asset] = new Point(existing.X, existing.Y); _open.Remove(existing); Invalidate(); return; }
        var bmp = WindowBitmap(asset);
        if (bmp == null) return;
        Point pos;
        if (_lastPos.TryGetValue(asset, out var remembered))
            pos = remembered;                                   // reopen where you left it
        else
        {
            // cascade fresh windows so they don't stack; keep them on-screen above the chat panel
            int step = (_cascade++ % 6);
            pos = new Point(20 + step * 26, 12 + step * 22);
        }
        _open.Add(new OpenWin { Asset = asset, X = pos.X, Y = pos.Y, Bmp = bmp });
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool worldOn = _worldMap != null;
        switch (e.KeyCode)
        {
            // arrow keys walk the world (pan the camera one iso cell); if no world, they nudge HP/MP
            case Keys.Up when worldOn: Walk(-1, -1); break;
            case Keys.Down when worldOn: Walk(+1, +1); break;
            case Keys.Left when worldOn: Walk(-1, +1); break;
            case Keys.Right when worldOn: Walk(+1, -1); break;
            case Keys.Up: _hp = Math.Min(100, _hp + 5); break;
            case Keys.Down: _hp = Math.Max(0, _hp - 5); break;
            case Keys.Right: _mp = Math.Min(100, _mp + 5); break;
            case Keys.Left: _mp = Math.Max(0, _mp - 5); break;
            // HP/MP on +/- and PageUp/PageDown when the arrows are busy walking
            case Keys.Oemplus or Keys.Add: _hp = Math.Min(100, _hp + 5); break;
            case Keys.OemMinus or Keys.Subtract: _hp = Math.Max(0, _hp - 5); break;
            case Keys.PageUp: _mp = Math.Min(100, _mp + 5); break;
            case Keys.PageDown: _mp = Math.Max(0, _mp - 5); break;
            case Keys.R: _weatherOn = !_weatherOn; break;   // toggle rain
            case Keys.Escape when _open.Count > 0: _open.RemoveAt(_open.Count - 1); break;
            default:
                if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
                {
                    int i = e.KeyCode - Keys.D1;
                    if (i < Slots.Length) ToggleWindow(Slots[i]);
                }
                break;
        }
        UpdateTitle();
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        for (int i = _open.Count - 1; i >= 0; i--)
        {
            var w = _open[i];
            if (e.X >= w.X && e.X < w.X + w.Bmp.Width && e.Y >= w.Y && e.Y < w.Y + w.Bmp.Height)
            {
                _drag = w; _dragOffset = new Point(e.X - w.X, e.Y - w.Y);
                _open.RemoveAt(i); _open.Add(w); // raise to top
                Invalidate();
                return;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag != null) { _drag.X = e.X - _dragOffset.X; _drag.Y = e.Y - _dragOffset.Y; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_drag != null) _lastPos[_drag.Asset] = new Point(_drag.X, _drag.Y);  // remember where it was dragged
        _drag = null;
    }

    private void Walk(int dx, int dy) { _camX += dx; _camY += dy; RenderWorld(); }

    private void UpdateTitle()
    {
        string controls = _worldMap != null
            ? "arrows walk · R rain · +/- HP · PgUp/PgDn MP · 1-9 open · Esc close"
            : "↑↓ HP · ←→ MP · R rain · 1-9 open · Esc close";
        string where = _worldMap != null ? $"  @({_camX},{_camY})" : "";
        Text = $"Dark Ages 3.50 — client shell   HP {_hp}%  MP {_mp}%{where}   [{controls}]";
    }
}
