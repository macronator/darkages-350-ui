# da350-shell — live client shell (WinForms)

An interactive **640×480 window** reconstructing the Dark Ages v3.50 client shell: the background chrome,
HP/MP orbs whose fill you drive live, and draggable window sprites. All geometry comes from `ui-350.json`;
all pixels from `DarkAges.dat`. This is the still-frame [`../client`](../client) harness turned into a live
loop — the next step is swapping in the reversed 7.41 protocol so the skin shows real server state.

Windows-only (WinForms / GDI+), .NET 10. Reuses the console harness's asset readers and UI-spec loader
verbatim (linked in the `.csproj`), so both stay in sync.

## Run

From source (needs the .NET 10 SDK):

```sh
cd shell
dotnet run -c Release -- <DarkAges.dat> ../docs/ui-350.json
```

or from the repo root with `run-shell.cmd <DarkAges.dat>` (finds `docs/ui-350.json` automatically). With no
paths given, the shell also looks for `DarkAges.dat` in `DA_DAT` / next to the exe / the working directory,
and `ui-350.json` in `DA_SPEC` / next to the exe / `../docs` — so a published build with those two files
beside it launches by **double-click**.

Publish a standalone Windows executable (no SDK needed to run it):

```sh
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
# copy DarkAges.dat and docs/ui-350.json next to dist/da350-shell.exe, then double-click it
```

Controls: **1–9** open/close windows (stats, equipment, spells, skills, exchange, friends, game setting,
macros, options) · **↑/↓** HP fill · **←/→** MP fill · drag a window by its body · **Esc** close the top window.

## Headless verification

Two flags render one frame and exit without opening a window (used to test the render path in CI or a
session with no interactive desktop):

```sh
# compose via the shared Canvas/PNG writer
dotnet run -c Release -- <DarkAges.dat> ../docs/ui-350.json --shot frame.png --hp 70 --mp 45 --open equip01,skill001
# capture the actual WinForms Form paint (OnPaint + GDI bridge)
dotnet run -c Release -- <DarkAges.dat> ../docs/ui-350.json --shot-gdi frame.png --hp 70 --mp 45 --open equip01
```

## Files

| file | role |
|---|---|
| `ShellForm.cs` | the interactive `Form`: paint, keyboard (orb levels / open windows), mouse drag |
| `Gdi.cs` | bridges the palette-indexed sprite readers to GDI+ `Bitmap`s |
| `ShellProgram.cs` | entry point; live window, plus `--shot` / `--shot-gdi` headless capture |
| *(linked)* | `Assets.cs`, `Png.cs`, `UiSpec.cs`, `WindowCatalog.cs` from `../client` |
