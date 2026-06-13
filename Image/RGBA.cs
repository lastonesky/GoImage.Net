// Port of Go's image/image.go - RGBA, RGBA64, NRGBA image types.

using GoImage.Color;

namespace GoImage.Image;

/// <summary>
/// RGBA is an in-memory image whose At method returns Color.RGBA values.
/// </summary>
public class RGBA : IImage, IImage64
{
    /// <summary>
    /// Pix holds the image's pixels, in R, G, B, A order.
    /// </summary>
    public byte[] Pix;
    public int Stride;
    public Rectangle Rect;

    public RGBA(byte[] pix, int stride, Rectangle rect)
    {
        Pix = pix;
        Stride = stride;
        Rect = rect;
    }

    public IModel ColorModel() => ColorModels.RGBAModel;
    public Rectangle Bounds() => Rect;

    public IColor At(int x, int y) => RGBAAt(x, y);

    public RGBA64 RGBA64At(int x, int y)
    {
        if (!new Point(x, y).In(Rect)) return default;
        int i = PixOffset(x, y);
        ushort r = Pix[i], g = Pix[i + 1], b = Pix[i + 2], a = Pix[i + 3];
        return new RGBA64((ushort)((r << 8) | r), (ushort)((g << 8) | g),
                          (ushort)((b << 8) | b), (ushort)((a << 8) | a));
    }

    public Color.RGBA RGBAAt(int x, int y)
    {
        if (!new Point(x, y).In(Rect)) return default;
        int i = PixOffset(x, y);
        return new Color.RGBA(Pix[i], Pix[i + 1], Pix[i + 2], Pix[i + 3]);
    }

    public int PixOffset(int x, int y) =>
        (y - Rect.Min.Y) * Stride + (x - Rect.Min.X) * 4;

    public void Set(int x, int y, IColor c)
    {
        if (!new Point(x, y).In(Rect)) return;
        int i = PixOffset(x, y);
        var c1 = (Color.RGBA)ColorModels.RGBAModel.Convert(c);
        Pix[i] = c1.R; Pix[i + 1] = c1.G; Pix[i + 2] = c1.B; Pix[i + 3] = c1.A;
    }

    public void SetRGBA(int x, int y, Color.RGBA c)
    {
        if (!new Point(x, y).In(Rect)) return;
        int i = PixOffset(x, y);
        Pix[i] = c.R; Pix[i + 1] = c.G; Pix[i + 2] = c.B; Pix[i + 3] = c.A;
    }

    public void SetRGBA64(int x, int y, RGBA64 c)
    {
        if (!new Point(x, y).In(Rect)) return;
        int i = PixOffset(x, y);
        Pix[i] = (byte)(c.R >> 8); Pix[i + 1] = (byte)(c.G >> 8);
        Pix[i + 2] = (byte)(c.B >> 8); Pix[i + 3] = (byte)(c.A >> 8);
    }

    public IImage SubImage(Rectangle r)
    {
        r = r.Intersect(Rect);
        if (r.Empty()) return new RGBA(Array.Empty<byte>(), 0, default);
        int i = PixOffset(r.Min.X, r.Min.Y);
        return new RGBA(Pix.AsSpan(i).ToArray(), Stride, r);
    }

    public bool Opaque()
    {
        if (Rect.Empty()) return true;
        int i0 = 3, i1 = Rect.Dx() * 4;
        for (int y = Rect.Min.Y; y < Rect.Max.Y; y++)
        {
            for (int i = i0; i < i1; i += 4)
            {
                if (Pix[i] != 0xff) return false;
            }
            i0 += Stride;
            i1 += Stride;
        }
        return true;
    }

    public static RGBA NewRGBA(Rectangle r)
    {
        int bufLen = ImageMath.PixelBufferLength(4, r, "RGBA");
        return new RGBA(new byte[bufLen], 4 * r.Dx(), r);
    }
}

/// <summary>
/// RGBA64 is an in-memory image whose At method returns Color.RGBA64 values.
/// </summary>
public class RGBA64Image : IImage, IImage64
{
    public byte[] Pix;
    public int Stride;
    public Rectangle Rect;

    public RGBA64Image(byte[] pix, int stride, Rectangle rect)
    {
        Pix = pix; Stride = stride; Rect = rect;
    }

    public IModel ColorModel() => ColorModels.RGBA64Model;
    public Rectangle Bounds() => Rect;
    public IColor At(int x, int y) => RGBA64At(x, y);

    public RGBA64 RGBA64At(int x, int y)
    {
        if (!new Point(x, y).In(Rect)) return default;
        int i = PixOffset(x, y);
        return new RGBA64(
            (ushort)((Pix[i] << 8) | Pix[i + 1]),
            (ushort)((Pix[i + 2] << 8) | Pix[i + 3]),
            (ushort)((Pix[i + 4] << 8) | Pix[i + 5]),
            (ushort)((Pix[i + 6] << 8) | Pix[i + 7]));
    }

    public int PixOffset(int x, int y) =>
        (y - Rect.Min.Y) * Stride + (x - Rect.Min.X) * 8;

    public static RGBA64Image NewRGBA64(Rectangle r)
    {
        int bufLen = ImageMath.PixelBufferLength(8, r, "RGBA64");
        return new RGBA64Image(new byte[bufLen], 8 * r.Dx(), r);
    }
}

/// <summary>
/// NRGBA is an in-memory image whose At method returns Color.NRGBA values.
/// </summary>
public class NRGBA : IImage, IImage64
{
    public byte[] Pix;
    public int Stride;
    public Rectangle Rect;

    public NRGBA(byte[] pix, int stride, Rectangle rect)
    {
        Pix = pix; Stride = stride; Rect = rect;
    }

    public IModel ColorModel() => ColorModels.NRGBAModel;
    public Rectangle Bounds() => Rect;
    public IColor At(int x, int y) => NRGBAAt(x, y);

    public RGBA64 RGBA64At(int x, int y)
    {
        var c = NRGBAAt(x, y);
        var (r, g, b, a) = c.GetRGBA();
        return new RGBA64((ushort)r, (ushort)g, (ushort)b, (ushort)a);
    }

    public Color.NRGBA NRGBAAt(int x, int y)
    {
        if (!new Point(x, y).In(Rect)) return default;
        int i = PixOffset(x, y);
        return new Color.NRGBA(Pix[i], Pix[i + 1], Pix[i + 2], Pix[i + 3]);
    }

    public int PixOffset(int x, int y) =>
        (y - Rect.Min.Y) * Stride + (x - Rect.Min.X) * 4;

    public static NRGBA NewNRGBA(Rectangle r)
    {
        int bufLen = ImageMath.PixelBufferLength(4, r, "NRGBA");
        return new NRGBA(new byte[bufLen], 4 * r.Dx(), r);
    }
}
