namespace DarkAges350;

/// <summary>A frame of an MPF creature sprite: a bounded region of 8-bit indexed pixels
/// plus its placement anchor (centerX/centerY).</summary>
public sealed class MpfFrame
{
    public int Left, Top, Width, Height, CenterX, CenterY;
    public byte[] Pixels = Array.Empty<byte>();
}

/// <summary>Reader for an .mpf creature sprite (monsters: mns*.mpf), ported faithfully from DALib's
/// MpfFile: header {u8 frameCount, i16 w, i16 h, i32 dataLength, u8 walkIdx, u8 walkCnt}, then a
/// SingleAttack (6-byte) or MultipleAttacks (10-byte) block, then frameCount 16-byte entries
/// {i16 left,top,right,bottom,centerX,centerY; i32 startAddress}. A (-1,-1) entry marks the palette
/// frame. Pixel data is the final dataLength bytes; each frame reads w*h bytes at its startAddress.</summary>
public sealed class MpfSprite
{
    public int Width, Height, PaletteNumber;
    public readonly List<MpfFrame> Frames = new();

    public static MpfSprite FromArchive(DatArchive dat, string name) =>
        new(dat.Read(name.EndsWith(".mpf", StringComparison.OrdinalIgnoreCase) ? name : name + ".mpf"));

    public MpfSprite(byte[] d)
    {
        // mns files carry no 4-byte header-type prefix (first int32 is large); parse from offset 0
        int fc = d[0];
        Width = BitConverter.ToInt16(d, 1);
        Height = BitConverter.ToInt16(d, 3);
        int dataLen = BitConverter.ToInt32(d, 5);
        // offset 9,10 = walk idx/count; 11.. = format block. Frame table is at 17 (SingleAttack) or 23
        // (MultipleAttacks); pick whichever yields a sane first entry.
        var frames = TryTable(d, 17, fc, dataLen) ?? TryTable(d, 23, fc, dataLen)
                     ?? throw new InvalidDataException("MPF: no valid frame table");
        int dataStart = d.Length - dataLen;
        foreach (var f in frames)
        {
            int need = f.Width * f.Height, from = dataStart + f.startAddr;
            f.frame.Pixels = (from >= 0 && from + need <= d.Length) ? d[from..(from + need)] : new byte[need];
            Frames.Add(f.frame);
        }
    }

    private List<(MpfFrame frame, int startAddr, int Width, int Height)>? TryTable(byte[] d, int off, int fc, int dataLen)
    {
        var list = new List<(MpfFrame, int, int, int)>();
        int q = off, want = fc;
        for (int i = 0; i < want;)
        {
            if (q + 16 > d.Length) return null;
            short left = BitConverter.ToInt16(d, q), top = BitConverter.ToInt16(d, q + 2);
            short right = BitConverter.ToInt16(d, q + 4), bottom = BitConverter.ToInt16(d, q + 6);
            short cx = BitConverter.ToInt16(d, q + 8), cy = BitConverter.ToInt16(d, q + 10);
            int start = BitConverter.ToInt32(d, q + 12); q += 16;
            if (left == -1 && top == -1) { PaletteNumber = start; want--; continue; }
            int fw = right - left, fh = bottom - top;
            if (!(left >= 0 && right > left && right <= Width + 4 && top >= 0 && bottom > top
                  && bottom <= Height + 4 && start >= 0 && start <= dataLen && fw * fh > 0))
                return null;
            list.Add((new MpfFrame { Left = left, Top = top, Width = fw, Height = fh, CenterX = cx, CenterY = cy }, start, fw, fh));
            i++;
        }
        return list;
    }
}
