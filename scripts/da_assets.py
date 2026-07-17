"""
da_assets.py - Dark Ages asset readers (for asset extraction / a standalone client).

Formats recovered from the game data files:

DAT archive (Legend.dat, hades.dat, misc.dat, ...):
    u32 count
    count x { u32 offset ; char name[13] }      # 17 bytes/entry; last entry is a sentinel
    [file data]                                  # file size = next.offset - this.offset

PAL palette (.pal): 768 bytes = 256 x RGB (one byte each).  (DAC values; some are 0-63.)

EPF sprite (.epf):
    u16 frameCount ; u16 width ; u16 height ; u16 pad ; u32 tocOffset
    [pixel data]                                 # 8-bit palette indices
    frame table @ (12 + tocOffset): frameCount x {
        u16 top, left, bottom, right             # bounding box; w=right-left, h=bottom-top
        u32 startOffset, endOffset               # into pixel data; size = w*h = end-start
    }

MPF (.mpf) / SPF (.spf) are related sprite formats (monster / misc); headers differ.
HPF (.hpf) = sound. TBL (.tbl) = lookup tables. MAP (.map) = map tile arrays.
"""
import struct


class Dat:
    def __init__(self, path):
        self.raw = open(path, "rb").read()
        n = struct.unpack_from("<I", self.raw, 0)[0]
        self.entries = []
        for i in range(n):
            off = struct.unpack_from("<I", self.raw, 4 + i * 17)[0]
            name = self.raw[4 + i * 17 + 4: 4 + i * 17 + 17].split(b"\x00")[0].decode("latin1")
            self.entries.append((off, name))

    def files(self):
        out = []
        for i in range(len(self.entries) - 1):
            off, name = self.entries[i]
            size = self.entries[i + 1][0] - off
            if name:
                out.append((name, off, size))
        return out

    def read(self, name):
        for i, (off, nm) in enumerate(self.entries):
            if nm.lower() == name.lower():
                end = self.entries[i + 1][0] if i + 1 < len(self.entries) else len(self.raw)
                return self.raw[off:end]
        raise KeyError(name)


def parse_pal(data):
    """768 bytes -> list of 256 (r,g,b)."""
    return [tuple(data[i * 3:i * 3 + 3]) for i in range(256)]


def parse_map(data):
    """lod###.map: raw array of cells, each 6 bytes = floor:u16, leftWall:u16,
    rightWall:u16 (little-endian). No header; width*height come from the 0x15 MapInfo
    packet. Returns list of (floor, left_wall, right_wall)."""
    return [struct.unpack_from("<HHH", data, i * 6) for i in range(len(data) // 6)]


def _rgb_triple(ln):
    """(r,g,b) if ln is a comma-separated numeric triple, else None."""
    p = ln.split(",")
    if len(p) == 3 and all(c.strip().lstrip("-").isdigit() for c in p):
        return tuple(int(c) for c in p)
    return None


def parse_color_tbl(data):
    """color.tbl / color0.tbl: a colour-remap table. ASCII: a leading number then
    lines of comma-separated numeric triples. Returns [(a,b,c), ...] for every
    numeric-triple line (comment/`;` and non-triple lines are skipped).

    NOTE: most triples read as ordinary 0-255 RGB, but a few channels exceed 255
    (e.g. `255,491,71`), so the exact semantics of the leading count and those
    lines are NOT fully verified -- treat the values as raw, not guaranteed RGB."""
    out = []
    for ln in data.decode("latin1", "replace").splitlines():
        t = _rgb_triple(ln.strip())
        if t is not None:
            out.append(t)
    return out


def parse_tbl(data):
    """Parse a .tbl. Two ASCII formats exist and this auto-detects:
      - lookup tables (skill.tbl, MobTile.tbl, itempal.tbl): 'spriteId paletteId'
        per line -> returns a dict {id: id}.
      - colour tables (color.tbl, color0.tbl): comma-separated numeric triples ->
        returns a list of (r,g,b) (delegates to parse_color_tbl).
    Detection requires >=2 numeric triples in the first lines, so a stray comma in
    a comment (e.g. skill.tbl's Korean header) does not misroute a lookup table."""
    text = data.decode("latin1", "replace")
    if sum(_rgb_triple(ln.strip()) is not None for ln in text.splitlines()[:8]) >= 2:
        return parse_color_tbl(data)
    out = {}
    for line in text.splitlines():
        p = line.split()
        if len(p) >= 2 and p[0].lstrip("-").isdigit():
            out[int(p[0])] = int(p[1]) if p[1].lstrip("-").isdigit() else p[1]
    return out


def parse_epf(data):
    fc, w, h, _pad = struct.unpack_from("<HHHH", data, 0)
    toc = struct.unpack_from("<I", data, 8)[0]
    base = 12 + toc
    frames = []
    for i in range(fc):
        top, left, bottom, right, start, end = struct.unpack_from("<HHHHII", data, base + i * 16)
        fw, fh = right - left, bottom - top
        pixels = data[12 + start: 12 + start + fw * fh]   # 8-bit palette indices
        frames.append({"top": top, "left": left, "w": fw, "h": fh, "pixels": pixels})
    return {"frame_count": fc, "width": w, "height": h, "frames": frames}


if __name__ == "__main__":
    import sys
    if len(sys.argv) >= 2:
        d = Dat(sys.argv[1])
        fs = d.files()
        print("%s: %d files" % (sys.argv[1], len(fs)))
        for name, off, size in fs[:30]:
            print("  %-16s off=0x%-8x size=%d" % (name, off, size))
        if len(fs) > 30:
            print("  ... (%d more)" % (len(fs) - 30))
    else:
        # demo on misc.dat
        d = Dat("../../misc.dat")
        for name, off, size in d.files():
            print("%-16s %d bytes" % (name, size))
        epf = parse_epf(d.read("wu16501.epf"))
        print("EPF wu16501: %d frames, canvas %dx%d; frame0 %dx%d (%d px)" % (
            epf["frame_count"], epf["width"], epf["height"],
            epf["frames"][0]["w"], epf["frames"][0]["h"], len(epf["frames"][0]["pixels"])))
