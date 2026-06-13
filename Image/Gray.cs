// Port of Go's image/image.go - Gray, Gray16, Alpha, Alpha16 image types.

using GoImage.Color;

namespace GoImage.Image;

/// <summary>
/// Gray is an in-memory image whose At method returns Color.Gray values.
/// </summary>
public class Gray : IImage, IImage64
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
public class Gray16Image : IImage, IImage64
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
public class Alpha : IImage, IImage64
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
public class Alpha16Image : IImage, IImage64
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
