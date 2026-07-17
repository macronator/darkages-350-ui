# da350 — C# render harness

A minimal, dependency-free .NET client that reconstructs the Dark Ages v3.50 **play screen** from the
recovered spec — the production-language counterpart to the Python tooling. It reads `DarkAges.dat` and
`ui-350.json`, composites the background chrome plus the HP/MP orbs at their exact recovered coordinates, and
writes a PNG. This is the disk→screen path a real client needs; input, networking, and the world render layer
build on top.

## Run

```sh
cd client
dotnet run -c Release -- <DarkAges.dat> ../docs/ui-350.json play_screen.png --map
```

- `<DarkAges.dat>` — your own copy (not included in this repo).
- `--map` (optional) overlays the recovered HUD region outlines (chat, minimap, HP/MP orbs).

## What's inside

| file | role |
|---|---|
| `Assets.cs` | `DatArchive` (archive TOC), `Epf` (sprite frames), `Palette` (256-colour, DAC-aware) |
| `Png.cs` | `Canvas` — indexed-sprite blit + a from-scratch PNG writer (uses `System.IO.Compression.ZLibStream`) |
| `UiSpec.cs` | loads `ui-350.json` (screen constants + rects) via `System.Text.Json` |
| `Program.cs` | composites the play screen from the spec |

Everything is deliberately dependency-free so the harness builds and runs offline. Ports of the same format
readers used by the Python `scripts/`.

## Next steps toward a live client

- Swap the still-frame compositor for a **MonoGame** game loop (or SkiaSharp for 2D), reusing these readers —
  or drop in **DALib** for the full asset layer (MPF/SPF monster sprites, EFA effects).
- Drive the HP/MP orb fill from live values by selecting the `orb001/002.epf` frame by percentage (16 frames,
  frame 0 = full).
- Reproduce the procedurally-generated layouts (`ui-350.json` → `runtime_layout`): index×stride grids,
  edge-anchored coords, and parent-relative insets.
- Wire the window-open dispatch (`docs/ui-dispatch-350.md`) to the toolbar/keyboard.
- Connect the reversed 7.41 protocol so the reconstructed skin renders live server state.
