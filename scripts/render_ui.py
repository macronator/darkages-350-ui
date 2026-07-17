# -*- coding: utf-8 -*-
"""render_ui.py - render Dark Ages UI .epf sprites to per-file contact-sheet PNGs.

Each EPF's frames are laid out in a grid on a dark background. Palette index 0 is
transparent. DAC (0-63) palettes are auto-scaled to full 0-255 range.

Pure stdlib (no PIL). Reads assets straight from a .dat archive via da_assets.py.

Usage:
    python render_ui.py <archive.dat> <palette_name> <out_dir> <epf1> [epf2 ...]
    e.g. python render_ui.py DarkAges.dat legend.pal out backgrnd.epf panel01.epf
"""
import sys
import os
import struct
import zlib
from da_assets import Dat, parse_pal, parse_epf


def write_png(path, w, h, rgba):
    def chunk(typ, data):
        return (struct.pack(">I", len(data)) + typ + data
                + struct.pack(">I", zlib.crc32(typ + data) & 0xFFFFFFFF))
    raw = bytearray()
    for y in range(h):
        raw.append(0)                                   # filter type 0
        raw += rgba[y * w * 4:(y + 1) * w * 4]
    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
           + chunk(b"IEND", b""))
    open(path, "wb").write(png)


def norm_pal(pal):
    """DAC palettes store 0-63 per channel; scale to 0-255 when detected."""
    if max(max(c) for c in pal) <= 63:
        return [(r << 2, g << 2, b << 2) for r, g, b in pal]
    return pal


def contact_sheet(dat, epf_name, pal, out_path, max_cols=8, pad=4, bg=(40, 40, 48)):
    epf = parse_epf(dat.read(epf_name))
    frames = [f for f in epf["frames"] if f["w"] > 0 and f["h"] > 0]
    if not frames:
        return None
    cols = min(max_cols, len(frames))
    rows = (len(frames) + cols - 1) // cols
    cw = [0] * cols
    rh = [0] * rows
    for i, f in enumerate(frames):
        c, r = i % cols, i // cols
        cw[c] = max(cw[c], f["w"])
        rh[r] = max(rh[r], f["h"])
    W = sum(cw) + pad * (cols + 1)
    H = sum(rh) + pad * (rows + 1)
    canvas = bytearray()
    for _ in range(W * H):
        canvas += bytes((bg[0], bg[1], bg[2], 255))
    xs = [pad + sum(cw[:c]) + pad * c for c in range(cols)]
    ys = [pad + sum(rh[:r]) + pad * r for r in range(rows)]
    for i, f in enumerate(frames):
        c, r = i % cols, i // cols
        x0, y0 = xs[c], ys[r]
        px = f["pixels"]
        for y in range(f["h"]):
            for x in range(f["w"]):
                idx = px[y * f["w"] + x]
                if idx == 0:                            # transparent
                    continue
                rr, gg, bb = pal[idx]
                o = ((y0 + y) * W + (x0 + x)) * 4
                canvas[o] = rr; canvas[o + 1] = gg; canvas[o + 2] = bb; canvas[o + 3] = 255
    write_png(out_path, W, H, bytes(canvas))
    return len(frames), epf["frame_count"], W, H


if __name__ == "__main__":
    if len(sys.argv) < 5:
        print(__doc__)
        sys.exit(0)
    dat_path, pal_name, out_dir = sys.argv[1:4]
    dat = Dat(dat_path)
    os.makedirs(out_dir, exist_ok=True)
    pal = norm_pal(parse_pal(dat.read(pal_name)))
    for epf_name in sys.argv[4:]:
        base = os.path.splitext(epf_name)[0]
        out = os.path.join(out_dir, "%s__%s.png" % (base, os.path.splitext(pal_name)[0]))
        try:
            res = contact_sheet(dat, epf_name, pal, out)
            if res:
                print("%-14s %d/%d frames -> %dx%d %s" % (epf_name, res[0], res[1], res[2], res[3], out))
            else:
                print("%-14s no drawable frames" % epf_name)
        except Exception as e:
            print("%-14s ERROR: %s" % (epf_name, e))
