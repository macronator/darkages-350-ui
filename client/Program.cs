using DarkAges350;

// Spec-driven reconstruction of the Dark Ages v3.50 play screen, in the production language.
// Mirrors the Python pipeline: read DarkAges.dat, read ui-350.json, composite the shell at the
// recovered coordinates, and write a PNG. This is the disk->screen path a real client needs;
// the game loop (input, networking, world render) layers on top.
//
// Usage: da350 <DarkAges.dat> <ui-350.json> [out.png] [--map]
//   --map also draws the recovered HUD region outlines.

if (args.Length < 2)
{
    Console.WriteLine("usage: da350 <DarkAges.dat> <ui-350.json> [out.png] [--map]");
    return 1;
}

string datPath = args[0];
string specPath = args[1];
string outPath = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : "play_screen.png";
bool drawMap = args.Contains("--map");

var dat = new DatArchive(datPath);
var spec = UiSpec.Load(specPath);
var pal = new Palette(dat.Read(spec.DefaultUiPalette));

var canvas = new Canvas(spec.Space.Width, spec.Space.Height);

// 1) full-screen background chrome (opaque)
var bg = new Epf(dat.Read(spec.Constants.BackgroundAsset));
canvas.Blit(bg.FirstDrawable, pal, 0, 0, opaque: true);

// 2) HP / MP orbs (frame 0 = full) at their exact recovered rects
DrawOrb(spec.Constants.OrbHpAsset, spec.Constants.OrbHpRect);
DrawOrb(spec.Constants.OrbMpAsset, spec.Constants.OrbMpRect);

// 3) optional region-map overlay
if (drawMap)
{
    void Box(Rect? r, byte cr, byte cg, byte cb) { if (r != null) canvas.Outline(r.Left, r.Top, r.Right, r.Bottom, cr, cg, cb); }
    Box(spec.Constants.ChatPanelRect, 120, 230, 120);
    Box(spec.Constants.MinimapRect, 200, 120, 220);
    Box(spec.Constants.OrbHpRect, 255, 90, 90);
    Box(spec.Constants.OrbMpRect, 110, 150, 255);
}

canvas.SavePng(outPath);
Console.WriteLine($"wrote {outPath} ({canvas.W}x{canvas.H})  bg={spec.Constants.BackgroundAsset} pal={spec.DefaultUiPalette}");
Console.WriteLine($"  HP orb {spec.Constants.OrbHpRect}  MP orb {spec.Constants.OrbMpRect}  minimap {spec.Constants.MinimapRect}");
return 0;

void DrawOrb(string asset, Rect? rect)
{
    if (rect == null) return;
    try
    {
        var epf = new Epf(dat.Read(asset));
        canvas.Blit(epf.FirstDrawable, pal, rect.Left, rect.Top); // index 0 transparent
    }
    catch (KeyNotFoundException)
    {
        Console.Error.WriteLine($"  (orb asset {asset} not found in archive; skipped)");
    }
}
