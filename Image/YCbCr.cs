// Port of Go's image/ycbcr.go (YCbCr and NYCbCrA image types).

using GoImage.Color;

namespace GoImage.Image;

/// <summary>
/// YCbCrSubsampleRatio is the chroma subsample ratio used in a YCbCr image.
/// </summary>
public enum YCbCrSubsampleRatio
{
    YCbCrSubsampleRatio444 = 0,
    YCbCrSubsampleRatio422,
    YCbCrSubsampleRatio420,
    YCbCrSubsampleRatio440,
    YCbCrSubsampleRatio411,
    YCbCrSubsampleRatio410,
}

/// <summary>
/// YCbCr is an in-memory image of Y'CbCr colors.
/// Matches Go's memory layout: Y, Cb, Cr are slices of a single backing buffer.
/// </summary>
public class YCbCr : IImage, IImage64
{
    // Backing buffer - Y, Cb, and Cr share this single allocation (like Go's make([]byte)).
    private readonly byte[] _buf;

    // Public views into _buf (matching Go's slice semantics).
    // Y[i] accesses _buf[_yOff + i], etc.
    public byte[] Y => _buf;     // For JPEG reconstructBlock direct access
    public byte[] Cb => _buf;
    public byte[] Cr => _buf;

    // Offsets into the backing buffer
    private readonly int _yOff, _cbOff, _crOff, _cbLen, _crLen;

    public int YStride, CStride;
    public YCbCrSubsampleRatio SubsampleRatio;
    public Rectangle Rect;

    // Constructor for shared-buffer mode (used by NewYCbCr)
    internal YCbCr(byte[] buf, int yOff, int yLen, int cbOff, int cbLen, int crOff, int crLen,
                   int yStride, int cStride, YCbCrSubsampleRatio subsampleRatio, Rectangle rect)
    {
        _buf = buf;
        _yOff = yOff;
        _cbOff = cbOff;
        _crOff = crOff;
        _cbLen = cbLen;
        _crLen = crLen;
        YStride = yStride;
        CStride = cStride;
        SubsampleRatio = subsampleRatio;
        Rect = rect;
    }

    // Convenience constructor for external use (separate arrays)
    public YCbCr(byte[] y, byte[] cb, byte[] cr, int yStride, int cStride,
                 YCbCrSubsampleRatio subsampleRatio, Rectangle rect)
    {
        // Combine into single buffer to match Go's memory layout
        _buf = new byte[y.Length + cb.Length + cr.Length];
        Array.Copy(y, 0, _buf, 0, y.Length);
        _yOff = 0;
        Array.Copy(cb, 0, _buf, y.Length, cb.Length);
        _cbOff = y.Length;
        _cbLen = cb.Length;
        Array.Copy(cr, 0, _buf, y.Length + cb.Length, cr.Length);
        _crOff = y.Length + cb.Length;
        _crLen = cr.Length;
        YStride = yStride;
        CStride = cStride;
        SubsampleRatio = subsampleRatio;
        Rect = rect;
    }

    /// <summary>
    /// GetY returns _buf[offset + i] for the Y channel.
    /// </summary>
    public byte GetY(int i) => _buf[_yOff + i];
    public byte GetCb(int i) => _buf[_cbOff + i];
    public byte GetCr(int i) => _buf[_crOff + i];

    /// <summary>
    /// SetY writes to the Y channel in the backing buffer.
    /// </summary>
    public void SetY(int i, byte v) => _buf[_yOff + i] = v;
    public void SetCb(int i, byte v) => _buf[_cbOff + i] = v;
    public void SetCr(int i, byte v) => _buf[_crOff + i] = v;

    // Expose YOff/CbOff/CrOff for the JPEG decoder's reconstructBlock
    public int YOff => _yOff;
    public int CbOff => _cbOff;
    public int CrOff => _crOff;

    // Direct buffer access (the backing array)
    public byte[] Buffer => _buf;

    public IModel ColorModel() => YCbCrModelStatic.YCbCrModel;
    public Rectangle Bounds() => Rect;
    public IColor At(int x, int y) => YCbCrAt(x, y);

    public RGBA64 RGBA64At(int x, int y)
    {
        var (r, g, b, a) = YCbCrAt(x, y).GetRGBA();
        return new RGBA64((ushort)r, (ushort)g, (ushort)b, (ushort)a);
    }

    public YCbCrColor YCbCrAt(int x, int y)
    {
        if (!new Point(x, y).In(Rect)) return default;
        int yi = YOffset(x, y);
        int ci = COffset(x, y);
        return new YCbCrColor(_buf[_yOff + yi], _buf[_cbOff + ci], _buf[_crOff + ci]);
    }

    public int YOffset(int x, int y) =>
        (y - Rect.Min.Y) * YStride + (x - Rect.Min.X);

    public int COffset(int x, int y) => SubsampleRatio switch
    {
        YCbCrSubsampleRatio.YCbCrSubsampleRatio422 =>
            (y - Rect.Min.Y) * CStride + (x / 2 - Rect.Min.X / 2),
        YCbCrSubsampleRatio.YCbCrSubsampleRatio420 =>
            (y / 2 - Rect.Min.Y / 2) * CStride + (x / 2 - Rect.Min.X / 2),
        YCbCrSubsampleRatio.YCbCrSubsampleRatio440 =>
            (y / 2 - Rect.Min.Y / 2) * CStride + (x - Rect.Min.X),
        YCbCrSubsampleRatio.YCbCrSubsampleRatio411 =>
            (y - Rect.Min.Y) * CStride + (x / 4 - Rect.Min.X / 4),
        YCbCrSubsampleRatio.YCbCrSubsampleRatio410 =>
            (y / 2 - Rect.Min.Y / 2) * CStride + (x / 4 - Rect.Min.X / 4),
        _ => (y - Rect.Min.Y) * CStride + (x - Rect.Min.X),
    };

    public bool Opaque() => true;

    public static (int w, int h, int cw, int ch) YCbCrSize(Rectangle r, YCbCrSubsampleRatio subsampleRatio)
    {
        int w = r.Dx(), h = r.Dy();
        int cw, ch;
        switch (subsampleRatio)
        {
            case YCbCrSubsampleRatio.YCbCrSubsampleRatio422:
                cw = (r.Max.X + 1) / 2 - r.Min.X / 2; ch = h; break;
            case YCbCrSubsampleRatio.YCbCrSubsampleRatio420:
                cw = (r.Max.X + 1) / 2 - r.Min.X / 2;
                ch = (r.Max.Y + 1) / 2 - r.Min.Y / 2; break;
            case YCbCrSubsampleRatio.YCbCrSubsampleRatio440:
                cw = w; ch = (r.Max.Y + 1) / 2 - r.Min.Y / 2; break;
            case YCbCrSubsampleRatio.YCbCrSubsampleRatio411:
                cw = (r.Max.X + 3) / 4 - r.Min.X / 4; ch = h; break;
            case YCbCrSubsampleRatio.YCbCrSubsampleRatio410:
                cw = (r.Max.X + 3) / 4 - r.Min.X / 4;
                ch = (r.Max.Y + 1) / 2 - r.Min.Y / 2; break;
            default:
                cw = w; ch = h; break;
        }
        return (w, h, cw, ch);
    }

    /// <summary>
    /// NewYCbCr allocates a new YCbCr image matching Go's memory layout:
    /// a single backing buffer with Y, Cb, and Cr as views into it.
    /// </summary>
    public static YCbCr NewYCbCr(Rectangle r, YCbCrSubsampleRatio subsampleRatio)
    {
        var (w, h, cw, ch) = YCbCrSize(r, subsampleRatio);
        int totalLength = ImageMath.Add2NonNeg(
            ImageMath.Mul3NonNeg(1, w, h),
            ImageMath.Mul3NonNeg(2, cw, ch));
        if (totalLength < 0)
            throw new InvalidOperationException("image: NewYCbCr Rectangle has huge or negative dimensions");

        // Match Go: single buffer b = make([]byte, w*h + 2*cw*ch)
        // Y = b[0 : w*h], Cb = b[w*h : w*h+cw*ch], Cr = b[w*h+cw*ch : w*h+2*cw*ch]
        int i0 = w * h;
        int i1 = w * h + cw * ch;
        int i2 = w * h + 2 * cw * ch;
        return new YCbCr(new byte[i2], 0, i0, i0, i1 - i0, i1, i2 - i1,
                         w, cw, subsampleRatio, r);
    }
}
