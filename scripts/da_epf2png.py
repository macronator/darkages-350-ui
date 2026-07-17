"""
da_epf2png.py - render Dark Ages .epf sprite frames to PNG (pure stdlib, no PIL).

Pixels are 8-bit palette indices; supply a matching .pal (256 RGB). Index 0 is treated
as transparent (typical for DA sprites). Frames are exported as <prefix>_<n>.png.

Usage:
    python da_epf2png.py <epf_dat> <epf_name> <pal_dat> <pal_name> [out_dir] [transp_index]
    e.g. python da_epf2png.py ../../misc.dat wu16501.epf ../../khanpal.dat palb00.pal out
"""
import os
import struct
import sys
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


def frame_to_rgba(frame, pal, transparent=0):
    w, h = frame["w"], frame["h"]
    px = frame["pixels"]
    rgba = bytearray(w * h * 4)
    for i in range(min(len(px), w * h)):
        idx = px[i]
        r, g, b = pal[idx]
        o = i * 4
        rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b
        rgba[o + 3] = 0 if idx == transparent else 255
    return w, h, bytes(rgba)


def export(epf_dat, epf_name, pal_dat, pal_name, out_dir="epf_out", transparent=0):
    epf = parse_epf(Dat(epf_dat).read(epf_name))
    pal = parse_pal(Dat(pal_dat).read(pal_name))
    os.makedirs(out_dir, exist_ok=True)
    base = os.path.splitext(epf_name)[0]
    n = 0
    for i, fr in enumerate(epf["frames"]):
        if fr["w"] <= 0 or fr["h"] <= 0:
            continue
        w, h, rgba = frame_to_rgba(fr, pal, transparent)
        out = os.path.join(out_dir, "%s_%02d.png" % (base, i))
        write_png(out, w, h, rgba)
        n += 1
    return n, epf["frame_count"], out_dir


if __name__ == "__main__":
    if len(sys.argv) < 5:
        print(__doc__)
        sys.exit(0)
    epf_dat, epf_name, pal_dat, pal_name = sys.argv[1:5]
    out_dir = sys.argv[5] if len(sys.argv) > 5 else "epf_out"
    transp = int(sys.argv[6]) if len(sys.argv) > 6 else 0
    n, total, d = export(epf_dat, epf_name, pal_dat, pal_name, out_dir, transp)
    print("wrote %d/%d frames to %s/" % (n, total, d))
