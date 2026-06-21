// Port of Go's image/color Model and Palette.

namespace GoImage.Color;

/// <summary>
/// IModel can convert any IColor to one from its own color model.
/// The conversion may be lossy.
/// </summary>
public interface IModel
{
    IColor Convert(IColor c);
}

/// <summary>
/// ModelFunc returns a IModel that invokes f to implement the conversion.
/// </summary>
public class ModelFunc : IModel
{
    private readonly Func<IColor, IColor> _f;
    public ModelFunc(Func<IColor, IColor> f) { _f = f; }
    public IColor Convert(IColor c) => _f(c);
}

/// <summary>
/// Standard color models.
/// </summary>
public static class ColorModels
{
    public static readonly IModel RGBAModel = new ModelFunc(RGBAModelConvert);
    public static readonly IModel RGBA64Model = new ModelFunc(RGBA64ModelConvert);
    public static readonly IModel NRGBAModel = new ModelFunc(NRGBAModelConvert);
    public static readonly IModel NRGBA64Model = new ModelFunc(NRGBA64ModelConvert);
    public static readonly IModel AlphaModel = new ModelFunc(AlphaModelConvert);
    public static readonly IModel Alpha16Model = new ModelFunc(Alpha16ModelConvert);
    public static readonly IModel GrayModel = new ModelFunc(GrayModelConvert);
    public static readonly IModel Gray16Model = new ModelFunc(Gray16ModelConvert);

    private static IColor RGBAModelConvert(IColor c)
    {
        if (c is RGBA rgba) return rgba;
        var (r, g, b, a) = c.GetRGBA();
        return new RGBA((byte)(r >> 8), (byte)(g >> 8), (byte)(b >> 8), (byte)(a >> 8));
    }

    private static IColor RGBA64ModelConvert(IColor c)
    {
        if (c is RGBA64 rgba64) return rgba64;
        var (r, g, b, a) = c.GetRGBA();
        return new RGBA64((ushort)r, (ushort)g, (ushort)b, (ushort)a);
    }

    private static IColor NRGBAModelConvert(IColor c)
    {
        if (c is NRGBA nrgba) return nrgba;
        var (r, g, b, a) = c.GetRGBA();
        if (a == 0xffff)
            return new NRGBA((byte)(r >> 8), (byte)(g >> 8), (byte)(b >> 8), 0xff);
        if (a == 0)
            return new NRGBA(0, 0, 0, 0);
        r = (r * 0xffff) / a;
        g = (g * 0xffff) / a;
        b = (b * 0xffff) / a;
        return new NRGBA((byte)(r >> 8), (byte)(g >> 8), (byte)(b >> 8), (byte)(a >> 8));
    }

    private static IColor NRGBA64ModelConvert(IColor c)
    {
        if (c is NRGBA64 nrgba64) return nrgba64;
        var (r, g, b, a) = c.GetRGBA();
        if (a == 0xffff)
            return new NRGBA64((ushort)r, (ushort)g, (ushort)b, 0xffff);
        if (a == 0)
            return new NRGBA64(0, 0, 0, 0);
        r = (r * 0xffff) / a;
        g = (g * 0xffff) / a;
        b = (b * 0xffff) / a;
        return new NRGBA64((ushort)r, (ushort)g, (ushort)b, (ushort)a);
    }

    private static IColor AlphaModelConvert(IColor c)
    {
        if (c is Alpha alpha) return alpha;
        var (_, _, _, a) = c.GetRGBA();
        return new Alpha((byte)(a >> 8));
    }

    private static IColor Alpha16ModelConvert(IColor c)
    {
        if (c is Alpha16 alpha16) return alpha16;
        var (_, _, _, a) = c.GetRGBA();
        return new Alpha16((ushort)a);
    }

    private static IColor GrayModelConvert(IColor c)
    {
        if (c is Gray gray) return gray;
        var (r, g, b, _) = c.GetRGBA();
        // These coefficients (0.299, 0.587, 0.114) are the same as JFIF spec.
        uint y = (19595 * r + 38470 * g + 7471 * b + (1 << 15)) >> 24;
        return new Gray((byte)y);
    }

    private static IColor Gray16ModelConvert(IColor c)
    {
        if (c is Gray16 gray16) return gray16;
        var (r, g, b, _) = c.GetRGBA();
        uint y = (19595 * r + 38470 * g + 7471 * b + (1 << 15)) >> 16;
        return new Gray16((ushort)y);
    }
}

/// <summary>
/// Palette is a palette of colors.
/// </summary>
public class Palette : List<IColor>, IModel
{
    private byte[]? _cache;
    private uint _cacheKey;

    public Palette() : base() { }
    public Palette(IEnumerable<IColor> collection) : base(collection) { }

    public IColor Convert(IColor c)
    {
        if (Count == 0) return null!;
        return this[Index(c)];
    }

    public int Index(IColor c)
    {
        var (cr, cg, cb, ca) = c.GetRGBA();
        
        // If it's a small palette (like GIF), use a 15-bit RGB lookup table.
        if (Count > 0 && Count <= 256)
        {
            if (_cache == null || _cacheKey != (uint)Count)
            {
                _cache = new byte[32768];
                for (int i = 0; i < 32768; i++)
                {
                    int r = (i >> 10) & 0x1F;
                    int g = (i >> 5) & 0x1F;
                    int b = i & 0x1F;
                    // Scale 5-bit to 16-bit
                    uint r16 = (uint)(r << 11) | (uint)(r << 6) | (uint)(r << 1);
                    uint g16 = (uint)(g << 11) | (uint)(g << 6) | (uint)(g << 1);
                    uint b16 = (uint)(b << 11) | (uint)(b << 6) | (uint)(b << 1);
                    _cache[i] = (byte)IndexLinear(r16, g16, b16, 0xffff);
                }
                _cacheKey = (uint)Count;
            }
            int ci = ((int)(cr >> 11) << 10) | ((int)(cg >> 11) << 5) | (int)(cb >> 11);
            return _cache[ci];
        }

        return IndexLinear(cr, cg, cb, ca);
    }

    private int IndexLinear(uint cr, uint cg, uint cb, uint ca)
    {
        int ret = 0;
        uint bestSum = uint.MaxValue;
        for (int i = 0; i < Count; i++)
        {
            var (vr, vg, vb, va) = this[i].GetRGBA();
            uint sum = ColorUtil.SqDiff(cr, vr) + ColorUtil.SqDiff(cg, vg) +
                       ColorUtil.SqDiff(cb, vb) + ColorUtil.SqDiff(ca, va);
            if (sum < bestSum)
            {
                if (sum == 0) return i;
                ret = i;
                bestSum = sum;
            }
        }
        return ret;
    }

    /// <summary>
    /// Plan9 is a 256-color palette. 
    /// This is a placeholder for the actual Plan9 palette.
    /// </summary>
    public static readonly Palette Plan9 = GeneratePlan9();

    private static Palette GeneratePlan9()
    {
        var p = new Palette();
        // The Plan 9 palette is a 6x6x6 color cube with 40 additional gray levels.
        // This is a more accurate representation of the Plan 9 palette.
        for (int r = 0; r < 6; r++)
        {
            for (int g = 0; g < 6; g++)
            {
                for (int b = 0; b < 6; b++)
                {
                    p.Add(new RGBA((byte)(255 - r * 255 / 5), (byte)(255 - g * 255 / 5), (byte)(255 - b * 255 / 5), 255));
                }
            }
        }
        // Add remaining grayscale and other colors to fill 256
        for (int i = p.Count; i < 256; i++)
        {
            byte v = (byte)(255 - (i - 216) * 255 / 39);
            p.Add(new RGBA(v, v, v, 255));
        }
        return p;
    }
}
