// Port of Go's image/color/ycbcr.go (YCbCr color types and conversions).

namespace GoImage.Color;

/// <summary>
/// RGBToYCbCr converts an RGB triple to a Y'CbCr triple.
/// </summary>
public static class YCbCrUtil
{
    public static (byte y, byte cb, byte cr) RGBToYCbCr(byte r, byte g, byte b)
    {
        int r1 = r;
        int g1 = g;
        int b1 = b;

        int yy = (19595 * r1 + 38470 * g1 + 7471 * b1 + (1 << 15)) >> 16;

        int cb = -11056 * r1 - 21712 * g1 + 32768 * b1 + (257 << 15);
        if (((uint)cb & 0xff000000) == 0)
            cb >>= 16;
        else
            cb = ~(cb >> 31);

        int cr = 32768 * r1 - 27440 * g1 - 5328 * b1 + (257 << 15);
        if (((uint)cr & 0xff000000) == 0)
            cr >>= 16;
        else
            cr = ~(cr >> 31);

        return ((byte)yy, (byte)cb, (byte)cr);
    }

    /// <summary>
    /// YCbCrToRGB converts a Y'CbCr triple to an RGB triple.
    /// </summary>
    public static (byte r, byte g, byte b) YCbCrToRGB(byte y, byte cb, byte cr)
    {
        int yy1 = y * 0x10101;
        int cb1 = cb - 128;
        int cr1 = cr - 128;

        int r = yy1 + 91881 * cr1;
        if (((uint)r & 0xff000000) == 0)
            r >>= 16;
        else
            r = ~(r >> 31);

        int g = yy1 - 22554 * cb1 - 46802 * cr1;
        if (((uint)g & 0xff000000) == 0)
            g >>= 16;
        else
            g = ~(g >> 31);

        int b = yy1 + 116130 * cb1;
        if (((uint)b & 0xff000000) == 0)
            b >>= 16;
        else
            b = ~(b >> 31);

        return ((byte)r, (byte)g, (byte)b);
    }

    /// <summary>
    /// RGBToCMYK converts an RGB triple to a CMYK quadruple.
    /// </summary>
    public static (byte c, byte m, byte y, byte k) RGBToCMYK(byte r, byte g, byte b)
    {
        uint rr = r, gg = g, bb = b;
        uint w = rr;
        if (w < gg) w = gg;
        if (w < bb) w = bb;
        if (w == 0) return (0, 0, 0, 0xff);
        uint c = (w - rr) * 0xff / w;
        uint m = (w - gg) * 0xff / w;
        uint y = (w - bb) * 0xff / w;
        return ((byte)c, (byte)m, (byte)y, (byte)(0xff - w));
    }

    /// <summary>
    /// CMYKToRGB converts a CMYK quadruple to an RGB triple.
    /// </summary>
    public static (byte r, byte g, byte b) CMYKToRGB(byte c, byte m, byte y, byte k)
    {
        uint w = 0xffff - (uint)k * 0x101;
        uint r = (0xffff - (uint)c * 0x101) * w / 0xffff;
        uint g = (0xffff - (uint)m * 0x101) * w / 0xffff;
        uint b = (0xffff - (uint)y * 0x101) * w / 0xffff;
        return ((byte)(r >> 8), (byte)(g >> 8), (byte)(b >> 8));
    }
}

/// <summary>
/// YCbCr represents a fully opaque 24-bit Y'CbCr color.
/// </summary>
public struct YCbCrColor : IColor
{
    public byte Y, Cb, Cr;

    public YCbCrColor(byte y, byte cb, byte cr)
    {
        Y = y; Cb = cb; Cr = cr;
    }

    public (uint r, uint g, uint b, uint a) GetRGBA()
    {
        int yy1 = Y * 0x10101;
        int cb1 = Cb - 128;
        int cr1 = Cr - 128;

        int r = yy1 + 91881 * cr1;
        if (((uint)r & 0xff000000) == 0)
            r >>= 8;
        else
            r = ~(r >> 31) & 0xffff;

        int g = yy1 - 22554 * cb1 - 46802 * cr1;
        if (((uint)g & 0xff000000) == 0)
            g >>= 8;
        else
            g = ~(g >> 31) & 0xffff;

        int b = yy1 + 116130 * cb1;
        if (((uint)b & 0xff000000) == 0)
            b >>= 8;
        else
            b = ~(b >> 31) & 0xffff;

        return ((uint)r, (uint)g, (uint)b, 0xffff);
    }
}

/// <summary>
/// YCbCrModel is the IModel for Y'CbCr colors.
/// </summary>
public static class YCbCrModelStatic
{
    public static readonly IModel YCbCrModel = new ModelFunc(YCbCrModelConvert);

    private static IColor YCbCrModelConvert(IColor c)
    {
        if (c is YCbCrColor ycbcr) return ycbcr;
        var (r, g, b, _) = c.GetRGBA();
        var (y, u, v) = YCbCrUtil.RGBToYCbCr((byte)(r >> 8), (byte)(g >> 8), (byte)(b >> 8));
        return new YCbCrColor(y, u, v);
    }
}

/// <summary>
/// NYCbCrA represents a non-alpha-premultiplied Y'CbCr-with-alpha color.
/// </summary>
public struct NYCbCrAColor : IColor
{
    public YCbCrColor YCbCr;
    public byte A;

    public NYCbCrAColor(YCbCrColor ycbcr, byte a)
    {
        YCbCr = ycbcr;
        A = a;
    }

    public NYCbCrAColor(byte y, byte cb, byte cr, byte a)
    {
        YCbCr = new YCbCrColor(y, cb, cr);
        A = a;
    }

    public (uint r, uint g, uint b, uint a) GetRGBA()
    {
        int yy1 = YCbCr.Y * 0x10101;
        int cb1 = YCbCr.Cb - 128;
        int cr1 = YCbCr.Cr - 128;

        int r = yy1 + 91881 * cr1;
        if (((uint)r & 0xff000000) == 0)
            r >>= 8;
        else
            r = ~(r >> 31) & 0xffff;

        int g = yy1 - 22554 * cb1 - 46802 * cr1;
        if (((uint)g & 0xff000000) == 0)
            g >>= 8;
        else
            g = ~(g >> 31) & 0xffff;

        int b = yy1 + 116130 * cb1;
        if (((uint)b & 0xff000000) == 0)
            b >>= 8;
        else
            b = ~(b >> 31) & 0xffff;

        uint a = (uint)A * 0x101;
        return ((uint)r * a / 0xffff, (uint)g * a / 0xffff, (uint)b * a / 0xffff, a);
    }
}

/// <summary>
/// NYCbCrAModel is the IModel for non-alpha-premultiplied Y'CbCr-with-alpha colors.
/// </summary>
public static class NYCbCrAModelStatic
{
    public static readonly IModel NYCbCrAModel = new ModelFunc(NYCbCrAModelConvert);

    private static IColor NYCbCrAModelConvert(IColor c)
    {
        if (c is NYCbCrAColor nycbcra) return nycbcra;
        if (c is YCbCrColor ycbcr) return new NYCbCrAColor(ycbcr, 0xff);
        var (r, g, b, a) = c.GetRGBA();
        if (a != 0)
        {
            r = (r * 0xffff) / a;
            g = (g * 0xffff) / a;
            b = (b * 0xffff) / a;
        }
        var (y, u, v) = YCbCrUtil.RGBToYCbCr((byte)(r >> 8), (byte)(g >> 8), (byte)(b >> 8));
        return new NYCbCrAColor(new YCbCrColor(y, u, v), (byte)(a >> 8));
    }
}

/// <summary>
/// CMYK represents a fully opaque CMYK color.
/// </summary>
public struct CMYKColor : IColor
{
    public byte C, M, Y, K;

    public CMYKColor(byte c, byte m, byte y, byte k)
    {
        C = c; M = m; Y = y; K = k;
    }

    public (uint r, uint g, uint b, uint a) GetRGBA()
    {
        uint w = 0xffff - (uint)K * 0x101;
        uint r = (0xffff - (uint)C * 0x101) * w / 0xffff;
        uint g = (0xffff - (uint)M * 0x101) * w / 0xffff;
        uint b = (0xffff - (uint)Y * 0x101) * w / 0xffff;
        return (r, g, b, 0xffff);
    }
}

/// <summary>
/// CMYKModel is the IModel for CMYK colors.
/// </summary>
public static class CMYKModelStatic
{
    public static readonly IModel CMYKModel = new ModelFunc(CMYKModelConvert);

    private static IColor CMYKModelConvert(IColor c)
    {
        if (c is CMYKColor cmyk) return cmyk;
        var (r, g, b, _) = c.GetRGBA();
        var (cc, mm, yy, kk) = YCbCrUtil.RGBToCMYK((byte)(r >> 8), (byte)(g >> 8), (byte)(b >> 8));
        return new CMYKColor(cc, mm, yy, kk);
    }
}
