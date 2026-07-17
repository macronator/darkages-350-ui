using System.Windows.Forms;

namespace DarkAges350;

internal static class ShellProgram
{
    // Usage:
    //   da350-shell [DarkAges.dat] [ui-350.json]             launch the live window
    //   da350-shell ... --shot out.png [--hp N] [--mp N] [--open a,b]   render one frame headless and exit
    // With no paths given it looks for DarkAges.dat (env DA_DAT, exe dir, or cwd) and ui-350.json
    // (env DA_SPEC, exe dir, ../docs, or cwd), so it can be launched by double-click.
    [STAThread]
    private static int Main(string[] args)
    {
        // positional args = the leading non-flag args (a flag value like "--shot x.png" must never
        // be mistaken for the dat/spec path).
        var positional = args.TakeWhile(a => !a.StartsWith("--")).ToArray();
        string? datPath = positional.Length >= 1 ? positional[0] : FindDat();
        string? specPath = positional.Length >= 2 ? positional[1] : FindSpec();

        if (datPath == null || specPath == null)
        {
            string msg = "Could not locate the game data.\n\n"
                + (datPath == null ? "• DarkAges.dat not found — pass it as the 1st argument, set DA_DAT, or put it next to this exe.\n" : "")
                + (specPath == null ? "• ui-350.json not found — pass it as the 2nd argument, set DA_SPEC, or put it in ../docs.\n" : "");
            try { MessageBox.Show(msg, "Dark Ages 3.50 shell"); } catch { Console.Error.WriteLine(msg); }
            return 1;
        }

        DatArchive dat;
        UiSpec spec;
        try { dat = new DatArchive(datPath); spec = UiSpec.Load(specPath); }
        catch (Exception ex)
        {
            try { MessageBox.Show($"Failed to load data:\n{ex.Message}", "Dark Ages 3.50 shell"); }
            catch { Console.Error.WriteLine(ex.Message); }
            return 1;
        }

        int shot = Array.IndexOf(args, "--shot");
        if (shot >= 0)
        {
            string outPath = (shot + 1 < args.Length) ? args[shot + 1] : "shell_frame.png";
            RenderFrameHeadless(dat, spec, outPath, args);
            return 0;
        }

        // Capture the *actual* Form paint (OnPaint + Gdi.cs) to a PNG, without a message loop.
        int gshot = Array.IndexOf(args, "--shot-gdi");
        if (gshot >= 0)
        {
            string outPath = (gshot + 1 < args.Length) ? args[gshot + 1] : "shell_gdi.png";
            RenderFormHeadless(dat, spec, outPath, args);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new ShellForm(dat, spec));
        return 0;
    }

    private static string? FindDat()
    {
        foreach (var c in new[]
        {
            Environment.GetEnvironmentVariable("DA_DAT"),
            Path.Combine(AppContext.BaseDirectory, "DarkAges.dat"),
            Path.Combine(Directory.GetCurrentDirectory(), "DarkAges.dat"),
        })
            if (!string.IsNullOrEmpty(c) && File.Exists(c)) return c;
        return null;
    }

    private static string? FindSpec()
    {
        var b = AppContext.BaseDirectory;
        foreach (var c in new[]
        {
            Environment.GetEnvironmentVariable("DA_SPEC"),
            Path.Combine(b, "ui-350.json"),
            Path.Combine(b, "docs", "ui-350.json"),
            Path.Combine(b, "..", "..", "..", "..", "docs", "ui-350.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "ui-350.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "docs", "ui-350.json"),
        })
            if (!string.IsNullOrEmpty(c) && File.Exists(c)) return c;
        return null;
    }

    // Headless render of the same composition the window shows on first paint — lets the shell's
    // render path be verified without an interactive desktop session.
    private static void RenderFrameHeadless(DatArchive dat, UiSpec spec, string outPath, string[] args)
    {
        int Val(string flag, int def)
        {
            int i = Array.IndexOf(args, flag);
            return (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v)) ? v : def;
        }
        int hp = Val("--hp", 100), mp = Val("--mp", 100);
        int oi = Array.IndexOf(args, "--open");
        string[] open = (oi >= 0 && oi + 1 < args.Length)
            ? args[oi + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();

        var pal = new Palette(dat.Read(spec.DefaultUiPalette));
        var c = new Canvas(spec.Space.Width, spec.Space.Height);
        c.Blit(new Epf(dat.Read(spec.Constants.BackgroundAsset)).FirstDrawable, pal, 0, 0, opaque: true);
        Orb(spec.Constants.OrbHpAsset, spec.Constants.OrbHpRect, hp);
        Orb(spec.Constants.OrbMpAsset, spec.Constants.OrbMpRect, mp);
        foreach (var n in open)
        {
            string asset = n.EndsWith(".epf", StringComparison.OrdinalIgnoreCase) ? n : n + ".epf";
            var (x, y) = WindowCatalog.PositionOf(asset);
            try { c.Blit(new Epf(dat.Read(asset)).FirstDrawable, pal, x, y); } catch (KeyNotFoundException) { }
        }
        c.SavePng(outPath);
        Console.WriteLine($"wrote {outPath} ({c.W}x{c.H})  HP={hp}% MP={mp}%");

        void Orb(string asset, Rect? r, int pct)
        {
            if (r == null) return;
            try { c.Blit(new Epf(dat.Read(asset)).FrameForFill(pct), pal, r.Left, r.Top); } catch (KeyNotFoundException) { }
        }
    }

    private static void RenderFormHeadless(DatArchive dat, UiSpec spec, string outPath, string[] args)
    {
        int Val(string flag, int def)
        {
            int i = Array.IndexOf(args, flag);
            return (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v)) ? v : def;
        }
        int hp = Val("--hp", 100), mp = Val("--mp", 100);
        int oi = Array.IndexOf(args, "--open");
        string[] open = (oi >= 0 && oi + 1 < args.Length)
            ? args[oi + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();

        ApplicationConfiguration.Initialize();
        using var form = new ShellForm(dat, spec);
        form.SetLevels(hp, mp);
        form.PreOpen(open);
        form.CreateControl();               // realise the handle so OnPaint can run
        var sz = form.ClientSize;
        using var bmp = new System.Drawing.Bitmap(sz.Width, sz.Height);
        form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, sz.Width, sz.Height));
        bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"wrote {outPath} ({sz.Width}x{sz.Height}) via Form paint  HP={hp}% MP={mp}%");
    }
}
