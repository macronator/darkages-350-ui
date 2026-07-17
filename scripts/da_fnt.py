"""da_fnt.py - Dark Ages bitmap font (.fnt) reader + text renderer (pure stdlib, no PIL).

Format (recovered from eng00.fnt / eng01.fnt):
    A flat array of glyphs, no header. Each glyph is CELL_H (12) bytes; each byte is one
    row's bitmap, MSB = leftmost pixel, so the cell is 8x12. The glyphs cover printable
    ASCII starting at 0x21 ('!'), so glyph index = ord(ch) - 0x21. eng00.fnt = 1128 bytes
    = 94 glyphs. Space (0x20) is a blank advance. Rendered proportionally by trimming each
    glyph's trailing empty columns.

    (han00-03.fnt are the larger Korean fonts and use a different cell size; not decoded here.)

Usage:
    python da_fnt.py <archive.dat> <font.fnt> "text to render" [out.png] [scale]
    e.g. python da_fnt.py DarkAges.dat eng00.fnt "Dark Ages 3.50" out.png 2
"""
import struct
import sys
import zlib
from da_assets import Dat

CELL_H = 12
CELL_W = 8
FIRST = 0x21


class Font:
    def __init__(self, data):
        n = len(data) // CELL_H
        self.glyphs = [data[i * CELL_H:(i + 1) * CELL_H] for i in range(n)]
        self.widths = [self._width(g) for g in self.glyphs]

    @staticmethod
    def _width(rows):
        used = 0
        for r in rows:
            for c in range(CELL_W):
                if r & (0x80 >> c):
                    used = max(used, c + 1)
        return used or 3

    def measure(self, text, tracking=1, space=4):
        w = 0
        for ch in text:
            gi = ord(ch) - FIRST
            w += (space if ch == " " or not (0 <= gi < len(self.glyphs)) else self.widths[gi]) + tracking
        return w

    def render(self, text, tracking=1, space=4):
        """Return (w, h, mask) where mask[y][x] is 1 for an 'on' pixel."""
        w, h = self.measure(text, tracking, space), CELL_H
        mask = [[0] * w for _ in range(h)]
        x = 0
        for ch in text:
            gi = ord(ch) - FIRST
            if ch == " " or not (0 <= gi < len(self.glyphs)):
                x += space + tracking
                continue
            rows = self.glyphs[gi]
            for ry in range(CELL_H):
                for cx in range(self.widths[gi]):
                    if rows[ry] & (0x80 >> cx) and x + cx < w:
                        mask[ry][x + cx] = 1
            x += self.widths[gi] + tracking
        return w, h, mask


def _png(path, w, h, rgba):
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw += rgba[y * w * 4:(y + 1) * w * 4]
    open(path, "wb").write(b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b""))


def render_png(font, text, out_path, scale=2, fg=(235, 215, 150), bg=(18, 16, 20)):
    w, h, mask = font.render(text)
    W, H = w * scale, h * scale
    rgba = bytearray(W * H * 4)
    for y in range(H):
        for x in range(W):
            on = mask[y // scale][x // scale]
            r, g, b = fg if on else bg
            o = (y * W + x) * 4
            rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b; rgba[o + 3] = 255
    _png(out_path, W, H, rgba)
    return W, H


if __name__ == "__main__":
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(0)
    dat, fnt, text = sys.argv[1], sys.argv[2], sys.argv[3]
    out = sys.argv[4] if len(sys.argv) > 4 else "text.png"
    scale = int(sys.argv[5]) if len(sys.argv) > 5 else 2
    f = Font(Dat(dat).read(fnt))
    print("%s: %d glyphs" % (fnt, len(f.glyphs)))
    w, h = render_png(f, text, out, scale)
    print("wrote %s (%dx%d)" % (out, w, h))
