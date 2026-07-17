# darkages-350-ui

Tools and reverse-engineering notes for the **Dark Ages v3.50** (May 2000) client interface — an
asset-extraction pipeline plus the recovered UI layout of the year-2000 client, produced for a
from-scratch reconstruction of the 3.50 look.

> **No game files are included.** This repository contains only original tooling and analysis. The
> Dark Ages client, `DarkAges.dat`, and all sprites/fonts/palettes are © Nexon/KRU and are **not**
> redistributed here. Point the scripts at your own legally-obtained copy. Reverse engineering is
> for interoperability and preservation of an interface you already own — check your local law and
> the game's terms before use.

## What's here

```
scripts/
  da_assets.py      DAT archive + EPF sprite + PAL palette + MAP + TBL readers (pure stdlib)
  da_epf2png.py     render a single .epf's frames to PNGs
  render_ui.py      render a UI .epf to a contact-sheet PNG (all frames in a grid)
  is3_extract.py    extract DarkAges.exe/.dat from a 1997 InstallShield 3 `_SETUP.1` (no VM needed)
  da_re.py          static-analysis toolkit for the 32-bit client PE (pefile + capstone)
  extract_layout.py recover the UI rectangle layout from DarkAges.exe -> Markdown table
docs/
  ui-layout-350.md  the recovered layout: 691 UI rects across 54 named windows
  ui-350.json       machine-readable UI spec (windows + rects + screen constants)
```

## The formats (recovered)

- **DAT archive** — `u32 count`, then `count × { u32 offset, char name[13] }`; file size = next entry's
  offset minus this one. The v3.50 `DarkAges.dat` holds 11,751 entries (EPF sprites, HPF sound, MPF
  monster sprites, TBL lookup tables, 6 FNT fonts, 10 PAL palettes).
- **EPF sprite** — `u16 frameCount, width, height, pad; u32 tocOffset`, then 8-bit palette-indexed
  pixels, then a frame table of `{ u16 top,left,bottom,right; u32 start,end }` bounding boxes.
- **PAL palette** — 768 bytes = 256 RGB triples. Some are DAC values (0–63); `render_ui.py` auto-scales.
  Index 0 is transparent. UI sprites pair with `legend.pal`; intro art with `Backpal1–6.pal`.

## The UI layout

The client places every interface rectangle through one constructor,
`sub_42c856(rectPtr, Left, Top, Right, Bottom)`, which fills a 16-byte `RECT`-like struct. `extract_layout.py`
finds all **691** call sites, decodes the Left/Top/Right/Bottom immediates, and attributes each rect to the
asset loaded just before it — yielding on-screen geometry for **54 named windows** (game shell, orbs,
stat panel, login/creation dialogs, equipment, exchange, merchant, message board, and more) in the
client's 640×480 UI space. See [`docs/ui-layout-350.md`](docs/ui-layout-350.md) for the human-readable
table and [`docs/ui-350.json`](docs/ui-350.json) for a machine-readable spec (per-window rects plus
screen constants — 640×480 space, 32px tiles, the full-screen background blit, the world-viewport draw
rect, chat-panel and toolbar rects, and the HP/MP orb loader) that a client can load directly.

It's a DirectDraw client (`ddraw.dll` + GDI), built without ASLR, so virtual addresses equal runtime
addresses. Constructor VA is for the v3.50 build; pass a different VA as the 3rd arg to `extract_layout.py`
for other builds.

About a third of the rects (213/691) have a coordinate computed at runtime rather than a literal — these
are **procedurally generated**, not missing data. `ui-350.json`'s `runtime_layout` block documents the three
generators a client reproduces instead of reading a constant: **index×stride** grids/lists (cell = base +
index·pitch), **edge-anchored** coords (edge − offset), and **parent-relative** insets (child positioned from
a parent widget's rect, stored at `+0x38`/`+0x3C`/`+0x40`/`+0x44` = L/T/R/B). Window-open dispatch (which
button/index opens which window) is written up in [`docs/ui-dispatch-350.md`](docs/ui-dispatch-350.md).

## Quickstart

```sh
pip install -r requirements.txt

# 1. (optional) pull the exe/dat out of the InstallShield installer's temp _SETUP.1
python scripts/is3_extract.py path/to/_SETUP.1 ./out

# 2. render UI sprites to contact sheets
python scripts/render_ui.py DarkAges.dat legend.pal ./render backgrnd.epf panel01.epf stat001.epf

# 3. recover the UI layout table from the client
python scripts/extract_layout.py DarkAges.exe docs/ui-layout-350.md
```

`da_assets.py`, `da_epf2png.py`, `render_ui.py`, and `is3_extract.py` are pure standard library;
`da_re.py` and `extract_layout.py` need `pefile` and `capstone`.

## License

Original code and documentation in this repository are released under the MIT License (see `LICENSE`).
This does not extend to any Dark Ages game data, which is not included and remains the property of its
rights holders.
