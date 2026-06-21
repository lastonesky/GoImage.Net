// Port of Go's image/image.go - RGBA, RGBA64, NRGBA image types.

using GoImage.Color;
using System.Runtime.InteropServices;

namespace GoImage.Image;

/// <summary>
/// RGBA is an in-memory image whose At method returns Color.RGBA values.
/// </summary>
public class RGBA : IImage<Color.RGBA>, IImage64, IDrawImage
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

    public Color.RGBA this[int x, int y]
    {
        get => RGBAAt(x, y);
        set => SetRGBA(x, y, value);
    }

    public Span<Color.RGBA> GetRowSpan(int y)
    {
        if (y < Rect.Min.Y || y >= Rect.Max.Y) return Span<Color.RGBA>.Empty;
        int i = PixOffset(Rect.Min.X, y);
        return MemoryMarshal.Cast<byte, Color.RGBA>(Pix.AsSpan(i, Rect.Dx() * 4));
    }

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
        // Note: In a high-performance version, we should avoid ToArray()
        // and instead use a view mechanism. For now, we keep the original logic
        // but it's a candidate for further optimization if zero-copy is required.
        int i = PixOffset(r.Min.X, r.Min.Y);
        return new RGBA(Pix, Stride, r); // Just pass the same array but with new Rect
    }

    public bool Opaque()
    {
        if (Rect.Empty()) return true;
        for (int y = Rect.Min.Y; y < Rect.Max.Y; y++)
        {
            var row = GetRowSpan(y);
            foreach (var p in row)
            {
                if (p.A != 0xff) return false;
            }
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
public class RGBA64Image : IImage<Color.RGBA64>, IImage64, IDrawImage
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

    public void Set(int x, int y, IColor c)
    {
        if (!new Point(x, y).In(Rect)) return;
        SetRGBA64(x, y, (RGBA64)ColorModels.RGBA64Model.Convert(c));
    }

    public Color.RGBA64 this[int x, int y]
    {
        get => RGBA64At(x, y);
        set => SetRGBA64(x, y, value);
    }

    public Span<Color.RGBA64> GetRowSpan(int y)
    {
        if (y < Rect.Min.Y || y >= Rect.Max.Y) return Span<Color.RGBA64>.Empty;
        int i = PixOffset(Rect.Min.X, y);
        return MemoryMarshal.Cast<byte, Color.RGBA64>(Pix.AsSpan(i, Rect.Dx() * 8));
    }

    public RGBA64 RGBA64At(int x, int y)
    {
        if (!new Point(x, y).In(Rect)) return default;
        int i = PixOffset(x, y);
        // Note: BMP and Go use big-endian for 16-bit components usually, 
        // but let's check how it's stored.
        return new RGBA64(
            (ushort)((Pix[i] << 8) | Pix[i + 1]),
            (ushort)((Pix[i + 2] << 8) | Pix[i + 3]),
            (ushort)((Pix[i + 4] << 8) | Pix[i + 5]),
            (ushort)((Pix[i + 6] << 8) | Pix[i + 7]));
    }

    public void SetRGBA64(int x, int y, RGBA64 c)
    {
        if (!new Point(x, y).In(Rect)) return;
        int i = PixOffset(x, y);
        Pix[i] = (byte)(c.R >> 8); Pix[i + 1] = (byte)c.R;
        Pix[i + 2] = (byte)(c.G >> 8); Pix[i + 3] = (byte)c.G;
        Pix[i + 4] = (byte)(c.B >> 8); Pix[i + 5] = (byte)c.B;
        Pix[i + 6] = (byte)(c.A >> 8); Pix[i + 7] = (byte)c.A;
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
public class NRGBA : IImage<Color.NRGBA>, IImage64, IDrawImage
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

    public void Set(int x, int y, IColor c)
    {
        if (!new Point(x, y).In(Rect)) return;
        SetNRGBA(x, y, (Color.NRGBA)ColorModels.NRGBAModel.Convert(c));
    }

    public Color.NRGBA this[int x, int y]
    {
        get => NRGBAAt(x, y);
        set => SetNRGBA(x, y, value);
    }

    public Span<Color.NRGBA> GetRowSpan(int y)
    {
        if (y < Rect.Min.Y || y >= Rect.Max.Y) return Span<Color.NRGBA>.Empty;
        int i = PixOffset(Rect.Min.X, y);
        return MemoryMarshal.Cast<byte, Color.NRGBA>(Pix.AsSpan(i, Rect.Dx() * 4));
    }

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

    public void SetNRGBA(int x, int y, Color.NRGBA c)
    {
        if (!new Point(x, y).In(Rect)) return;
        int i = PixOffset(x, y);
        Pix[i] = c.R; Pix[i + 1] = c.G; Pix[i + 2] = c.B; Pix[i + 3] = c.A;
    }

    public bool Opaque()
    {
        if (Rect.Empty()) return true;
        for (int y = Rect.Min.Y; y < Rect.Max.Y; y++)
        {
            var row = GetRowSpan(y);
            foreach (var p in row)
            {
                if (p.A != 0xff) return false;
            }
        }
        return true;
    }

    public int PixOffset(int x, int y) =>
        (y - Rect.Min.Y) * Stride + (x - Rect.Min.X) * 4;

    public static NRGBA NewNRGBA(Rectangle r)
    {
        int bufLen = ImageMath.PixelBufferLength(4, r, "NRGBA");
        return new NRGBA(new byte[bufLen], 4 * r.Dx(), r);
    }
}
