"""
da_re.py - reusable static-analysis toolkit for the Dark Ages client (native 32-bit PE).

Built on pefile + capstone. Provides:
  - PE load + memory-mapped image, VA/RVA/file-offset mapping
  - import + thunk resolution (name<->thunk VA, IAT slot -> name)
  - call xref finder (direct E8/FF15 and via-thunk), immediate (push/mov imm32) refs
  - function-start indexing (from call targets + prologues) and func bounds
  - linear function disassembly, C-string reads, string table extraction

The client is a no-ASLR image, so virtual addresses equal runtime addresses.

Usage:
    from da_re import DA
    da = DA("DarkAges.exe")
    for site in da.xrefs_to_import('recv'): ...
"""
import os, re, struct
import pefile
import capstone

# Point this at the DarkAges.exe you want to analyze (defaults to one in the cwd).
DEFAULT_EXE = os.environ.get("DA_EXE", "DarkAges.exe")


class DA:
    def __init__(self, path=DEFAULT_EXE):
        self.path = os.path.abspath(path)
        self.raw = open(self.path, "rb").read()
        self.pe = pefile.PE(data=self.raw, fast_load=False)
        self.IB = self.pe.OPTIONAL_HEADER.ImageBase
        self.img = self.pe.get_memory_mapped_image()  # indexed by RVA
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        self.md.detail = True
        self.text = self.pe.sections[0]
        self.text_start = self.IB + self.text.VirtualAddress
        self.text_end = self.text_start + self.text.Misc_VirtualSize
        self._build_imports()
        self._func_index = None  # lazy

    # ---- address mapping -------------------------------------------------
    def rva(self, va):
        return va - self.IB

    def in_text(self, va):
        return self.text_start <= va < self.text_end

    def read(self, va, n):
        r = self.rva(va)
        return self.img[r:r + n]

    def section_of(self, va):
        rva = self.rva(va)
        for s in self.pe.sections:
            if s.VirtualAddress <= rva < s.VirtualAddress + max(s.Misc_VirtualSize, s.SizeOfRawData):
                return s
        return None

    # ---- imports / thunks ------------------------------------------------
    def _build_imports(self):
        self.iat = {}            # IAT slot VA -> (dll, name)
        self.name2slot = {}      # name -> slot VA
        for d in self.pe.DIRECTORY_ENTRY_IMPORT:
            dll = d.dll.decode("latin1")
            for imp in d.imports:
                nm = imp.name.decode("latin1") if imp.name else ("ord%d" % imp.ordinal)
                self.iat[imp.address] = (dll, nm)
                self.name2slot.setdefault(nm, imp.address)
        # find FF25 jmp-thunks: thunk VA -> name
        self.thunks = {}         # thunk VA -> name
        self.name2thunks = {}    # name -> [thunk VA,...]
        code = self.img[self.text.VirtualAddress:self.text.VirtualAddress + self.text.Misc_VirtualSize]
        base = self.text_start
        for m in re.finditer(b"\xff\x25", code):
            off = m.start()
            ptr = struct.unpack_from("<I", code, off + 2)[0]
            if ptr in self.iat:
                va = base + off
                nm = self.iat[ptr][1]
                self.thunks[va] = nm
                self.name2thunks.setdefault(nm, []).append(va)

    # ---- xrefs -----------------------------------------------------------
    def _text_code(self):
        return self.img[self.text.VirtualAddress:self.text.VirtualAddress + self.text.Misc_VirtualSize]

    def find_call_xrefs(self, target_va):
        """All E8 rel32 / FF15 [abs] sites whose target == target_va."""
        code = self._text_code()
        base = self.text_start
        out = []
        # E8 rel32
        i = 0
        n = len(code)
        while i < n - 5:
            b = code[i]
            if b == 0xE8:
                rel = struct.unpack_from("<i", code, i + 1)[0]
                if base + i + 5 + rel == target_va:
                    out.append(base + i)
            i += 1
        # FF15 abs (call dword ptr [imm32])
        for m in re.finditer(b"\xff\x15", code):
            off = m.start()
            ptr = struct.unpack_from("<I", code, off + 2)[0]
            if ptr == target_va:
                out.append(base + off)
        return sorted(set(out))

    def xrefs_to_import(self, name):
        """Call sites that invoke an imported function, via its jmp-thunks or direct FF15 to the IAT slot."""
        sites = []
        for tva in self.name2thunks.get(name, []):
            sites += self.find_call_xrefs(tva)
        slot = self.name2slot.get(name)
        if slot is not None:
            sites += self.find_call_xrefs(slot)
        return sorted(set(sites))

    def find_imm_refs(self, value, sections=(".text",)):
        """Sites with a 4-byte immediate == value: push imm32 (68), mov reg,imm32 (B8..BF),
        mov [..],imm32 (C7), cmp/and/or with imm32, or a raw dword.

        Defaults to .text only. This client is heavily vtable-dispatched, so a function
        reached only through a vtable has NO .text reference at all -- pass e.g.
        sections=(".text", ".rdata", ".data") to also find the vtable slot that holds it.
        (The binary is built WITHOUT RTTI, so a vtable head has no Complete Object Locator
        and the owning class cannot be recovered by name.)
        """
        out = []
        for sec in self.pe.sections:
            name = sec.Name.decode(errors="ignore").strip("\x00")
            if name not in sections:
                continue
            base = self.IB + sec.VirtualAddress
            blob = sec.get_data()
            needle = struct.pack("<I", value)
            start = 0
            while True:
                idx = blob.find(needle, start)
                if idx < 0:
                    break
                out.append(base + idx)
                start = idx + 1
        return sorted(out)

    # ---- function indexing ----------------------------------------------
    # Bytes that can legitimately precede a function: ret/ret imm16, int3 & nop
    # padding, and the tail of a jmp.
    _TERMINATORS = frozenset((0xC3, 0xC2, 0xCC, 0x90, 0xE9, 0xEB))

    def _is_plausible_start(self, code, off):
        """Reject phantom starts synthesized from misaligned bytes.

        Both scans below are byte-wise, not instruction-aligned, so they splice
        'calls' out of the middle of real instructions. The dominant source is the
        disp8 of [ebp-0x18], which *is* 0xE8:

            mov dword [ebp-0x18],0  ->  c7 45 e8 | 00 00 00 00   -> "call rel32=0"
            mov eax,[ebp-0x18]      ->  8b 45 e8 | <next insn>    -> "call <garbage>"

        A rel32=0 'call' targets the very next instruction, so such a phantom always
        lands mid-function, where it silently outranks the real start in func_start()
        (which takes the largest start <= va).

        Accept a candidate if it opens with an MSVC frame prologue OR is preceded by
        a terminator/padding byte. Frameless functions keep the second test, so recall
        stays high.
        """
        if off < 1 or off + 5 > len(code):
            return False
        if code[off:off + 3] == b"\x55\x8b\xec" or code[off:off + 5] == b"\x8b\xff\x55\x8b\xec":
            return True
        return code[off - 1] in self._TERMINATORS

    def _build_func_index(self):
        code = self._text_code()
        base = self.text_start
        starts = set()
        n = len(code)
        # 1) every E8 call target inside .text is a function start
        i = 0
        while i < n - 5:
            if code[i] == 0xE8:
                rel = struct.unpack_from("<i", code, i + 1)[0]
                t = base + i + 5 + rel
                if self.text_start <= t < self.text_end:
                    starts.add(t)
            i += 1
        # 2) classic prologues: push ebp; mov ebp,esp (55 8B EC)
        for m in re.finditer(b"\x55\x8b\xec", code):
            starts.add(base + m.start())
        # 3) drop candidates that cannot be entries (see _is_plausible_start)
        self._func_index = sorted(s for s in starts if self._is_plausible_start(code, s - base))

    @property
    def func_starts(self):
        if self._func_index is None:
            self._build_func_index()
        return self._func_index

    def func_start(self, va):
        """Largest known function start <= va."""
        import bisect
        fs = self.func_starts
        j = bisect.bisect_right(fs, va) - 1
        return fs[j] if j >= 0 else None

    def func_bounds(self, va):
        """(start, end) — start from index, end = next start (upper bound)."""
        import bisect
        fs = self.func_starts
        s = self.func_start(va)
        if s is None:
            return None
        j = bisect.bisect_right(fs, s)
        e = fs[j] if j < len(fs) else self.text_end
        return (s, e)

    # ---- disassembly -----------------------------------------------------
    def disasm(self, va, nbytes):
        data = self.read(va, nbytes)
        for ins in self.md.disasm(data, va):
            yield ins

    def disasm_func(self, va, cap=0x4000):
        """Disassemble the function containing va, start..end."""
        b = self.func_bounds(va)
        if not b:
            return []
        s, e = b
        e = min(e, s + cap)
        return list(self.md.disasm(self.read(s, e - s), s))

    # ---- strings ---------------------------------------------------------
    def cstring(self, va, maxlen=512):
        r = self.rva(va)
        end = self.img.find(b"\x00", r, r + maxlen)
        if end < 0:
            end = r + maxlen
        return self.img[r:end].decode("latin1", "replace")

    def iter_strings(self, minlen=4):
        """Yield (va, text) for printable ASCII strings in .rdata/.data sections."""
        for s in self.pe.sections:
            nm = s.Name.rstrip(b"\x00").decode("latin1")
            if nm not in (".rdata", ".data", ".text"):
                continue
            blob = self.img[s.VirtualAddress:s.VirtualAddress + s.Misc_VirtualSize]
            base = self.IB + s.VirtualAddress
            for m in re.finditer(rb"[\x20-\x7e]{%d,}" % minlen, blob):
                yield (base + m.start(), m.group().decode("latin1"))

    def find_string_va(self, substr):
        """VAs of strings containing substr (as bytes search in mapped image)."""
        if isinstance(substr, str):
            substr = substr.encode("latin1")
        out = []
        start = 0
        while True:
            idx = self.img.find(substr, start)
            if idx < 0:
                break
            # walk back to string start (after a NUL)
            s = idx
            while s > 0 and 0x20 <= self.img[s - 1] <= 0x7e:
                s -= 1
            out.append(self.IB + s)
            start = idx + len(substr)
        return out


if __name__ == "__main__":
    import sys
    da = DA(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_EXE)
    print("loaded", da.path)
    print("ImageBase %08x text %08x..%08x" % (da.IB, da.text_start, da.text_end))
    print("imports:", len(da.iat), "thunks:", len(da.thunks), "func starts:", len(da.func_starts))
