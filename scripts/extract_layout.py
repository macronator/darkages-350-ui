# -*- coding: utf-8 -*-
"""extract_layout.py - recover the Dark Ages client's UI layout from DarkAges.exe.

The client places every UI rectangle through a single constructor:

    sub_42c856(rectPtr, Left, Top, Right, Bottom)   # in the v3.50 build

which fills a 16-byte RECT-like struct ([+0]=Top [+4]=Left [+8]=Bottom [+0xC]=Right).
This script finds every call site, decodes the four Left/Top/Right/Bottom immediates
(args are pushed right-to-left, so the last five pushes are [B, R, T, L, rectPtr]), and
attributes each rect to the nearest preceding .epf/.pal name push in the same function.
The result is a per-asset table of on-screen rectangles for 640x480 UI space.

If your build differs, pass the constructor VA as the 3rd argument.

Usage:
    python extract_layout.py <DarkAges.exe> [out.md] [rect_ctor_va_hex]
    e.g. python extract_layout.py DarkAges.exe ../docs/ui-layout-350.md 42c856
"""
import sys
from collections import defaultdict
from da_re import DA

EXE = sys.argv[1] if len(sys.argv) > 1 else "DarkAges.exe"
OUT = sys.argv[2] if len(sys.argv) > 2 else "ui-layout.md"
RECT = int(sys.argv[3], 16) if len(sys.argv) > 3 else 0x42c856

da = DA(EXE)


def is_asset(va):
    if not (da.IB < va < da.IB + len(da.img)):
        return None
    s = da.cstring(va, 40)
    if s and s.lower().endswith((".epf", ".pal", ".spf", ".mpf")) and all(0x20 <= ord(c) < 0x7f for c in s):
        return s
    return None


def fmt(v):
    return str(v) if isinstance(v, int) else "·"   # middle dot = runtime/register value


sites = da.find_call_xrefs(RECT)
rows = []
for site in sites:
    fb = da.func_bounds(site)
    fstart = fb[0] if fb else site - 0x120
    asset = None
    pushes = []
    for ins in da.disasm(fstart, site - fstart + 6):
        if ins.address > site:
            break
        if ins.mnemonic == "push":
            try:
                v = int(ins.op_str, 16)
            except ValueError:
                v = None
            if v is not None:
                a = is_asset(v)
                if a:
                    asset = a
            pushes.append(v)
        if ins.address == site:
            break
    a = pushes[-5:]
    if len(a) < 5:
        continue
    # pushes[-5:] == [Bottom, Right, Top, Left, rectPtr] (right-to-left cdecl)
    B, R, T, L = a[0], a[1], a[2], a[3]
    rows.append((asset or "(dynamic/none)", site, fstart, L, T, R, B))

byasset = defaultdict(list)
for asset, site, fn, L, T, R, B in rows:
    byasset[asset].append((site, fn, L, T, R, B))

keys = sorted(byasset, key=lambda a: (a.startswith("("), a.lower()))

lines = []
lines.append("# Dark Ages UI layout (reversed from DarkAges.exe)\n")
lines.append("Recovered by static analysis with `scripts/da_re.py` + `scripts/extract_layout.py`.\n")
lines.append("**Layout primitive:** `sub_%x(rectPtr, Left, Top, Right, Bottom)` — the UI rectangle " % RECT)
lines.append("constructor. It fills a 16-byte `RECT`-like struct (`[+0]=Top [+4]=Left [+8]=Bottom [+0xC]=Right`); ")
lines.append("the four call args are a **Left/Top/Right/Bottom** bounding box. **%d call sites.** " % len(sites))
lines.append("`W`/`H` below are derived (`R-L`, `B-T`). `·` = value computed at runtime (register/memory — ")
lines.append("anchored to screen size or a sibling offset). Coordinates are in the client's 640×480 UI space.\n")
lines.append("Assets load via the EPF loader; the rect calls that carry an asset name place that asset, ")
lines.append("while unnamed rects position sub-widgets (text fields, buttons, list rows) within a dialog.\n")
lines.append("| asset | fn | L | T | R | B | W | H | site |")
lines.append("|---|---|--:|--:|--:|--:|--:|--:|---|")
for k in keys:
    for site, fn, L, T, R, B in byasset[k]:
        w = (R - L) if isinstance(L, int) and isinstance(R, int) else None
        h = (B - T) if isinstance(T, int) and isinstance(B, int) else None
        lines.append("| `%s` | %08x | %s | %s | %s | %s | %s | %s | %08x |" % (
            k, fn, fmt(L), fmt(T), fmt(R), fmt(B),
            fmt(w) if w is not None else "·", fmt(h) if h is not None else "·", site))

open(OUT, "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("wrote %s: %d named windows, %d rects" % (
    OUT, sum(1 for k in keys if not k.startswith("(")), len(rows)))
