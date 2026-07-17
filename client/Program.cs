using DarkAges350;

// Spec-driven reconstruction of the Dark Ages v3.50 play screen, in the production language.
// Renders a *client state*: the background chrome, HP/MP orbs filled to a given percentage, and
// any open windows composited at their coordinates. Reads DarkAges.dat + ui-350.json; writes a PNG.
// The game loop (input, networking, world render) layers on top of this disk->screen path.
//
// Usage:
//   da350 <DarkAges.dat> <ui-350.json> [out.png] [options]
// Options:
//   --hp N        HP orb fill percent (0-100, default 100)
//   --mp N        MP orb fill percent (0-100, default 100)
//   --open a,b,c  composite these window sprites (e.g. equip01,spell001,skill001)
//   --map         overlay the recovered HUD region outlines

if (args.Length < 2)
{
    Console.WriteLine("usage: da350 <DarkAges.dat> <ui-350.json> [out.png] [--hp N] [--mp N] [--open a,b] [--map]");
    Console.WriteLine("  known windows: " + string.Join(", ", WindowCatalog.Keys));
    return 1;
}

string datPath = args[0];
string specPath = args[1];
string outPath = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : "play_screen.png";
int hp = ArgInt("--hp", 100), mp = ArgInt("--mp", 100);
bool drawMap = args.Contains("--map");
string[] openWindows = ArgList("--open");

var dat = new DatArchive(datPath);
var spec = UiSpec.Load(specPath);
var pal = new Palette(dat.Read(spec.DefaultUiPalette));
var canvas = new Canvas(spec.Space.Width, spec.Space.Height);

// 1) full-screen background chrome (opaque)
canvas.Blit(new Epf(dat.Read(spec.Constants.BackgroundAsset)).FirstDrawable, pal, 0, 0, opaque: true);

// 1b) live isometric world in the viewport (--world <lod*.map> [--cam x,y])
int wIdx = Array.IndexOf(args, "--world");
if (wIdx >= 0 && wIdx + 1 < args.Length)
{
    var atlas = TileAtlas.FromArchive(dat);
    var tilePal = new Palette(dat.Read("field001.pal"));
    var map = WorldMap.FromFile(args[wIdx + 1]);
    int camX = map.Width / 2, camY = map.Height / 2;
    int ci = Array.IndexOf(args, "--cam");
    if (ci >= 0 && ci + 1 < args.Length)
    {
        var p = args[ci + 1].Split(',');
        if (p.Length == 2 && int.TryParse(p[0], out var a) && int.TryParse(p[1], out var b)) { camX = a; camY = b; }
    }
    map.DrawFloor(canvas, atlas, tilePal, 2, 2, 610, 315, camX, camY);
}

// 2) HP / MP orbs filled to the requested percentage, at their exact recovered rects
DrawOrb(spec.Constants.OrbHpAsset, spec.Constants.OrbHpRect, hp);
DrawOrb(spec.Constants.OrbMpAsset, spec.Constants.OrbMpRect, mp);

// 3) open windows composited at their positions
foreach (var name in openWindows)
{
    string asset = name.EndsWith(".epf", StringComparison.OrdinalIgnoreCase) ? name : name + ".epf";
    var (x, y) = WindowCatalog.PositionOf(asset);
    try { canvas.Blit(new Epf(dat.Read(asset)).FirstDrawable, pal, x, y); }
    catch (KeyNotFoundException) { Console.Error.WriteLine($"  (window {asset} not in archive; skipped)"); }
}

// 4) chat text rendered with the real DA bitmap font (eng00.fnt)
int sayIdx = Array.IndexOf(args, "--say");
string[] chatLines = (sayIdx >= 0 && sayIdx + 1 < args.Length)
    ? args[sayIdx + 1].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    : Array.Empty<string>();
if (chatLines.Length > 0 && spec.Constants.ChatPanelRect is Rect cp)
{
    var font = DaFont.Load(dat, "eng00.fnt");
    int y = cp.Top + 6;
    foreach (var line in chatLines)
    {
        font.Draw(canvas, line, cp.Left + 10, y, (235, 215, 150));
        y += font.Height() + 3;
    }
}

// 5) optional region-map overlay
if (drawMap)
{
    void Box(Rect? r, byte cr, byte cg, byte cb) { if (r != null) canvas.Outline(r.Left, r.Top, r.Right, r.Bottom, cr, cg, cb); }
    Box(spec.Constants.ChatPanelRect, 120, 230, 120);
    Box(spec.Constants.MinimapRect, 200, 120, 220);
    Box(spec.Constants.OrbHpRect, 255, 90, 90);
    Box(spec.Constants.OrbMpRect, 110, 150, 255);
}

canvas.SavePng(outPath);
Console.WriteLine($"wrote {outPath} ({canvas.W}x{canvas.H})  HP={hp}% MP={mp}%"
    + (openWindows.Length > 0 ? $"  open=[{string.Join(",", openWindows)}]" : ""));
return 0;

void DrawOrb(string asset, Rect? rect, int percent)
{
    if (rect == null) return;
    try { canvas.Blit(new Epf(dat.Read(asset)).FrameForFill(percent), pal, rect.Left, rect.Top); }
    catch (KeyNotFoundException) { Console.Error.WriteLine($"  (orb {asset} not in archive; skipped)"); }
}

int ArgInt(string flag, int def)
{
    int i = Array.IndexOf(args, flag);
    return (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v)) ? v : def;
}
string[] ArgList(string flag)
{
    int i = Array.IndexOf(args, flag);
    return (i >= 0 && i + 1 < args.Length)
        ? args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : Array.Empty<string>();
}
