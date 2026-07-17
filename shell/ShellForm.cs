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
        // static chat lines drawn into the chat panel with the real DA font
        if (spec.Constants.ChatPanelRect is Rect cp)
        {
            string[] chat = { "[System] Welcome to Temuair.", "Deoxys: reconstruction online!", "Aisling the Wizard has entered." };
            int y = cp.Top + 6;
            foreach (var line in chat) { _font.Draw(bgCanvas, line, cp.Left + 10, y, Amber); y += _font.Height() + 3; }
        }
        _bg = Gdi.ToBitmap(bgCanvas);

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

    private void ToggleWindow(string asset)
    {
        var existing = _open.FirstOrDefault(w => w.Asset.Equals(asset, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { _open.Remove(existing); Invalidate(); return; }
        var bmp = WindowBitmap(asset);
        if (bmp == null) return;
        var (x, y) = WindowCatalog.PositionOf(asset);
        _open.Add(new OpenWin { Asset = asset, X = x, Y = y, Bmp = bmp });
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up: _hp = Math.Min(100, _hp + 5); break;
            case Keys.Down: _hp = Math.Max(0, _hp - 5); break;
            case Keys.Right: _mp = Math.Min(100, _mp + 5); break;
            case Keys.Left: _mp = Math.Max(0, _mp - 5); break;
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

    protected override void OnMouseUp(MouseEventArgs e) => _drag = null;

    private void UpdateTitle() =>
        Text = $"Dark Ages 3.50 — client shell   HP {_hp}%  MP {_mp}%   [1-9 open · ↑↓ HP · ←→ MP · Esc close]";
}
