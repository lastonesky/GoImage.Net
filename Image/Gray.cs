// Port of Go's image/image.go - Gray, Gray16, Alpha, Alpha16 image types.

using GoImage.Color;
using System.Runtime.InteropServices;

namespace GoImage.Image;

/// <summary>
/// Gray is an in-memory image whose At method returns Color.Gray values.
/// </summary>
public class Gray : IImage<Color.Gray>, IImage64, IDrawImage
{
    public byte[] Pix;
    public int Stride;
    public Rectangle Rect;

    public Gray(byte[] pix, int stride, Rectangle rect)
    {
        Pix = pix; Stride = stride; Rect = rect;
    }

    public IModel ColorModel() => ColorModels.GrayModel;
    public Rectangle Bounds() => Rect;
    public IColor At(int x, int y) => GrayAt(x, y);

    public void Set(int x, int y, IColor c)
    {
        if (!new Point(x, y).In(Rect)) return;
        SetGray(x, y, (Color.Gray)ColorModels.GrayModel.Convert(c));
    }

    public Color.Gray this[int x, int y]
    {
        get => GrayAt(x, y);
        set => SetGray(x, y, value);
    }

    public Span<Color.Gray> GetRowSpan(int y)
    {
        if (y < Rect.Min.Y || y >= Rect.Max.Y) return Span<Color.Gray>.Empty;
        int i = PixOffset(Rect.Min.X, y);
        return MemoryMarshal.Cast<byte, Color.Gray>(Pix.AsSpan(i, Rect.Dx()));
    }

    public RGBA64 RGBA64At(int x, int y)
    {
        ushort gray = GrayAt(x, y).Y;
        gray = (ushort)(gray | (gray << 8));
        return new RGBA64(gray, gray, gray, 0xffff);
    }

    public Color.Gray GrayAt(int x, int y)
    {
        if (!new Point(x, y).In(Rect)) return default;
        int i = PixOffset(x, y);
        return new Color.Gray(Pix[i]);
    }

    public int PixOffset(int x, int y) =>
        (y - Rect.Min.Y) * Stride + (x - Rect.Min.X) * 1;

    public void SetGray(int x, int y, Color.Gray c)
    {
        if (!new Point(x, y).In(Rect)) return;
        int i = PixOffset(x, y);
        Pix[i] = c.Y;
    }

    public bool Opaque() => true;

    public static Gray NewGray(Rectangle r)
    {
        int bufLen = ImageMath.PixelBufferLength(1, r, "Gray");
        return new Gray(new byte[bufLen], 1 * r.Dx(), r);
    }
}

/// <summary>
/// Gray16 is an in-memory image whose At method returns Color.Gray16 values.
/// </summary>
public class Gray16Image : IImage<Color.Gray16>, IImage64, IDrawImage
{
    public byte[] Pix;
    public int Stride;
    public Rectangle Rect;

    public Gray16Image(byte[] pix, int stride, Rectangle rect)
    {
        Pix = pix; Stride = stride; Rect = rect;
    }

    public IModel ColorModel() => ColorModels.Gray16Model;
    public Rectangle Bounds() => Rect;
    public IColor At(int x, int y) => Gray16At(x, y);

    public void Set(int x, int y, IColor c)
    {
        if (!new Point(x, y).In(Rect)) return;
        SetGray16(x, y, (Color.Gray16)ColorModels.Gray16Model.Convert(c));
    }

    public Color.Gray16 this[int x, int y]
    {
        get => Gray16At(x, y);
        set => SetGray16(x, y, value);
    }

    public Span<Color.Gray16> GetRowSpan(int y)
    {
        if (y < Rect.Min.Y || y >= Rect.Max.Y) return Span<Color.Gray16>.Empty;
        int i = PixOffset(Rect.Min.X, y);
        return MemoryMarshal.Cast<byte, Color.Gray16>(Pix.AsSpan(i, Rect.Dx() * 2));
    }

    public RGBA64 RGBA64At(int x, int y)
    {
        ushort gray = Gray16At(x, y).Y;
        return new RGBA64(gray, gray, gray, 0xffff);
    }

    public Color.Gray16 Gray16At(int x, int y)
    {
        if (!new Point(x, y).In(Rect)) return default;
        int i = PixOffset(x, y);
        return new Color.Gray16((ushort)((Pix[i] << 8) | Pix[i + 1]));
    }

    public void SetGray16(int x, int y, Color.Gray16 c)
    {
        if (!new Point(x, y).In(Rect)) return;
        int i = PixOffset(x, y);
        Pix[i] = (byte)(c.Y >> 8); Pix[i + 1] = (byte)c.Y;
    }

    public int PixOffset(int x, int y) =>
        (y - Rect.Min.Y) * Stride + (x - Rect.Min.X) * 2;

    public bool Opaque() => true;

    public static Gray16Image NewGray16(Rectangle r)
    {
        int bufLen = ImageMath.PixelBufferLength(2, r, "Gray16");
        return new Gray16Image(new byte[bufLen], 2 * r.Dx(), r);
    }
}

/// <summary>
/// Alpha is an in-memory image whose At method returns Color.Alpha values.
/// </summary>
public class Alpha : IImage<Color.Alpha>, IImage64
{
    public byte[] Pix;
    public int Stride;
    public Rectangle Rect;

    public Alpha(byte[] pix, int stride, Rectangle rect)
    {
        Pix = pix; Stride = stride; Rect = rect;
    }

    public IModel ColorModel() => ColorModels.AlphaModel;
    public Rectangle Bounds() => Rect;
    public IColor At(int x, int y) => AlphaAt(x, y);

    public Color.Alpha this[int x, int y]
    {
        get => AlphaAt(x, y);
        set => SetAlpha(x, y, value);
    }

    public Span<Color.Alpha> GetRowSpan(int y)
    {
        if (y < Rect.Min.Y || y >= Rect.Max.Y) return Span<Color.Alpha>.Empty;
        int i = PixOffset(Rect.Min.X, y);
        return MemoryMarshal.Cast<byte, Color.Alpha>(Pix.AsSpan(i, Rect.Dx()));
    }

    public RGBA64 RGBA64At(int x, int y)
    {
        ushort a = AlphaAt(x, y).A;
        a = (ushort)(a | (a << 8));
        return new RGBA64(a, a, a, a);
    }

    public Color.Alpha AlphaAt(int x, int y)
    {
        if (!new Point(x, y).In(Rect)) return default;
        int i = PixOffset(x, y);
        return new Color.Alpha(Pix[i]);
    }

    public void SetAlpha(int x, int y, Color.Alpha c)
    {
        if (!new Point(x, y).In(Rect)) return;
        int i = PixOffset(x, y);
        Pix[i] = c.A;
    }

    public int PixOffset(int x, int y) =>
        (y - Rect.Min.Y) * Stride + (x - Rect.Min.X) * 1;

    public static Alpha NewAlpha(Rectangle r)
    {
        int bufLen = ImageMath.PixelBufferLength(1, r, "Alpha");
        return new Alpha(new byte[bufLen], 1 * r.Dx(), r);
    }
}

/// <summary>
/// Alpha16 is an in-memory image whose At method returns Color.Alpha16 values.
/// </summary>
public class Alpha16Image : IImage<Color.Alpha16>, IImage64
{
    public byte[] Pix;
    public int Stride;
    public Rectangle Rect;

    public Alpha16Image(byte[] pix, int stride, Rectangle rect)
    {
        Pix = pix; Stride = stride; Rect = rect;
    }

    public IModel ColorModel() => ColorModels.Alpha16Model;
    public Rectangle Bounds() => Rect;
    public IColor At(int x, int y) => Alpha16At(x, y);

    public Color.Alpha16 this[int x, int y]
    {
        get => Alpha16At(x, y);
        set => SetAlpha16(x, y, value);
    }

    public Span<Color.Alpha16> GetRowSpan(int y)
    {
        if (y < Rect.Min.Y || y >= Rect.Max.Y) return Span<Color.Alpha16>.Empty;
        int i = PixOffset(Rect.Min.X, y);
        return MemoryMarshal.Cast<byte, Color.Alpha16>(Pix.AsSpan(i, Rect.Dx() * 2));
    }

    public RGBA64 RGBA64At(int x, int y)
    {
        ushort a = Alpha16At(x, y).A;
        return new RGBA64(a, a, a, a);
    }

    public Color.Alpha16 Alpha16At(int x, int y)
    {
        if (!new Point(x, y).In(Rect)) return default;
        int i = PixOffset(x, y);
        return new Color.Alpha16((ushort)((Pix[i] << 8) | Pix[i + 1]));
    }

    public void SetAlpha16(int x, int y, Color.Alpha16 c)
    {
        if (!new Point(x, y).In(Rect)) return;
        int i = PixOffset(x, y);
        Pix[i] = (byte)(c.A >> 8); Pix[i + 1] = (byte)c.A;
    }

    public int PixOffset(int x, int y) =>
        (y - Rect.Min.Y) * Stride + (x - Rect.Min.X) * 2;

    public static Alpha16Image NewAlpha16(Rectangle r)
    {
        int bufLen = ImageMath.PixelBufferLength(2, r, "Alpha16");
        return new Alpha16Image(new byte[bufLen], 2 * r.Dx(), r);
    }
}

/// <summary>
/// CMYK is an in-memory image whose At method returns Color.CMYK values.
/// </summary>
public class CMYK : IImage, IImage64
{
    public byte[] Pix;
    public int Stride;
    public Rectangle Rect;

    public CMYK(byte[] pix, int stride, Rectangle rect)
    {
        Pix = pix; Stride = stride; Rect = rect;
    }

    public IModel ColorModel() => CMYKModelStatic.CMYKModel;
    public Rectangle Bounds() => Rect;
    public IColor At(int x, int y) => CMYKAt(x, y);

    public RGBA64 RGBA64At(int x, int y)
    {
        var (r, g, b, a) = CMYKAt(x, y).GetRGBA();
        return new RGBA64((ushort)r, (ushort)g, (ushort)b, (ushort)a);
    }

    public Color.CMYKColor CMYKAt(int x, int y)
    {
        if (!new Point(x, y).In(Rect)) return default;
        int i = PixOffset(x, y);
        return new Color.CMYKColor(Pix[i], Pix[i + 1], Pix[i + 2], Pix[i + 3]);
    }

    public int PixOffset(int x, int y) =>
        (y - Rect.Min.Y) * Stride + (x - Rect.Min.X) * 4;

    public bool Opaque() => true;

    public static CMYK NewCMYK(Rectangle r)
    {
        int bufLen = ImageMath.PixelBufferLength(4, r, "CMYK");
        return new CMYK(new byte[bufLen], 4 * r.Dx(), r);
    }
}
