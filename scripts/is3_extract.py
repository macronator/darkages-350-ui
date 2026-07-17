"""is3_extract.py - extract files from an InstallShield 3 (1997) `_SETUP.1` library.

Solves the "16-bit SETUP.EXE won't run on 64-bit Windows" problem WITHOUT a VM:
run the self-extracting installer, grab the temp `~EXB*/DISK1/_SETUP.1`, and point
this at it. Reverse-engineered 2026-07-16 for the DA v3.50 reconstruction.

Library format (sig 0x8C655D13): file directory near EOF, plaintext filenames.
Per-entry fields (relative to the name-length byte P, name follows P):
    uncompressed_size u32 @ P-26 ; compressed_size u32 @ P-22 ; data_offset u32 @ P-18
    dos_date u16 @ P-14 ; dos_time u16 @ P-12 ; namelen u8 @ P.
File data is PKWARE DCL "implode" (byte0=lit-flag, byte1=dict 4/5/6) -> explode() below.

Usage: python is3_extract.py <_SETUP.1> <out_dir> [name1 name2 ...]
       (no names => extracts DarkAges.exe + DarkAges.dat)
"""
import struct, os, sys, time
MAXBITS = 13
LENLEN = bytes([2,35,36,53,38,23]); DISTLEN = bytes([2,20,53,230,247,151,248])
BASE = [3,2,4,5,6,7,8,9,10,12,16,24,40,72,136,264]
EXTRA = [0,0,0,0,0,0,0,0,1,2,3,4,5,6,7,8]

class Huff:
    def __init__(self, rep):
        length = []
        for r in rep:
            n = (r >> 4) + 1; l = r & 15; length += [l]*n
        self.count = [0]*(MAXBITS+1)
        for l in length: self.count[l] += 1
        offs = [0]*(MAXBITS+2)
        for l in range(1, MAXBITS+1): offs[l+1] = offs[l] + self.count[l]
        self.symbol = [0]*len(length)
        for sym, l in enumerate(length):
            if l: self.symbol[offs[l]] = sym; offs[l] += 1

class St:
    __slots__ = ('d','p','buf','cnt')
    def __init__(s, d): s.d = d; s.p = 0; s.buf = 0; s.cnt = 0
    def bits(s, need):
        val = s.buf
        while s.cnt < need:
            val |= s.d[s.p] << s.cnt; s.p += 1; s.cnt += 8
        s.buf = val >> need; s.cnt -= need
        return val & ((1 << need) - 1)
    def decode(s, h):
        code = first = index = 0
        for L in range(1, MAXBITS+1):
            code |= s.bits(1) ^ 1; c = h.count[L]
            if code < first + c: return h.symbol[index + (code - first)]
            index += c; first = (first + c) << 1; code <<= 1
        return -9

_LEN = Huff(LENLEN); _DIST = Huff(DISTLEN)

def explode(data, expected):
    """PKWARE DCL 'implode' decompressor (a la Mark Adler's blast.c)."""
    s = St(data); lit = s.bits(8); dic = s.bits(8)
    assert lit in (0, 1) and 4 <= dic <= 6, "bad DCL header"
    out = bytearray(); ap = out.append; ext = out.extend
    while len(out) < expected:
        if s.bits(1):
            sym = s.decode(_LEN); length = BASE[sym] + s.bits(EXTRA[sym])
            if length == 519: break
            db = 2 if length == 2 else dic
            dist = (s.decode(_DIST) << db) + s.bits(db) + 1
            start = len(out) - dist
            if dist >= length: ext(out[start:start+length])
            else:
                for i in range(length): ap(out[start+i])
        else:
            ap(s.bits(8) if lit == 0 else s.decode(Huff.__dict__))  # lit==1 unused here
    return bytes(out)

def entry(b, name):
    off = b.find(name.encode()); 
    if off < 0: return None
    P = off - 1
    return (struct.unpack_from("<I", b, P-26)[0],   # uncompressed
            struct.unpack_from("<I", b, P-22)[0],   # compressed
            struct.unpack_from("<I", b, P-18)[0])   # data offset

def extract(lib_path, out_dir, names):
    b = open(lib_path, "rb").read()
    os.makedirs(out_dir, exist_ok=True)
    for name in names:
        e = entry(b, name)
        if not e: print("  %s: NOT FOUND" % name); continue
        unc, comp, doff = e
        t = time.time(); res = explode(b[doff:doff+comp], unc)
        open(os.path.join(out_dir, name), "wb").write(res)
        print("  %-16s %10d bytes  ok=%s  (%.1fs)" % (name, len(res), len(res) == unc, time.time()-t))

if __name__ == "__main__":
    lib = sys.argv[1]; out = sys.argv[2]
    names = sys.argv[3:] or ["DarkAges.exe", "DarkAges.dat"]
    extract(lib, out, names)
