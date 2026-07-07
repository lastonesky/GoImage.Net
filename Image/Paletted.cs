// Port of Go's image/image.go - Paletted image type.

using GoImage.Color;

namespace GoImage.Image;

/// <summary>
/// Paletted is an in-memory image of uint8 indices into a given palette.
/// </summary>
public class Paletted : IPalettedImage, IImage64, IDrawImage
{
    public byte[] Pix;
    public int Stride;
    public Rectangle Rect;
    public Palette Palette;

    public Paletted(byte[] pix, int stride, Rectangle rect, Palette palette)
    {
        Pix = pix; Stride = stride; Rect = rect; Palette = palette;
    }

    public IModel ColorModel() => Palette;
    public Rectangle Bounds() => Rect;

    public void Set(int x, int y, IColor c)
    {
        if (!new Point(x, y).In(Rect)) return;
        Pix[PixOffset(x, y)] = (byte)Palette.Index(c);
    }

    public IColor At(int x, int y)
    {
        if (Palette.Count == 0) return null!;
        if (!new Point(x, y).In(Rect)) return Palette[0];
        int i = PixOffset(x, y);
        return Palette[Pix[i]];
    }

    public RGBA64 RGBA64At(int x, int y)
    {
        if (Palette.Count == 0) return default;
        IColor c;
        if (!new Point(x, y).In(Rect))
            c = Palette[0];
        else
        {
            int i = PixOffset(x, y);
            c = Palette[Pix[i]];
        }
        var (r, g, b, a) = c.GetRGBA();
        return new RGBA64((ushort)r, (ushort)g, (ushort)b, (ushort)a);
    }

    public int PixOffset(int x, int y) =>
        (y - Rect.Min.Y) * Stride + (x - Rect.Min.X) * 1;

    public byte ColorIndexAt(int x, int y)
    {
        if (!new Point(x, y).In(Rect)) return 0;
        int i = PixOffset(x, y);
        return Pix[i];
    }

    public void SetColorIndex(int x, int y, byte index)
    {
        if (!new Point(x, y).In(Rect)) return;
        int i = PixOffset(x, y);
        Pix[i] = index;
    }

    public IImage SubImage(Rectangle r)
    {
        r = r.Intersect(Rect);
        if (r.Empty()) return new Paletted(Array.Empty<byte>(), 0, default, Palette);
        int width = r.Dx();
        int height = r.Dy();
        int newStride = width;
        byte[] newPix = new byte[height * newStride];
        for (int y = 0; y < height; y++)
        {
            int srcOff = PixOffset(r.Min.X, r.Min.Y + y);
            Array.Copy(Pix, srcOff, newPix, y * newStride, width);
        }
        return new Paletted(newPix, newStride, r, Palette);
    }

    public bool Opaque()
    {
        if (Rect.Empty()) return true;
        if (Palette.Count == 0) return false;
        for (int i = 0; i < Palette.Count; i++)
        {
            var (_, _, _, a) = Palette[i].GetRGBA();
            if (a != 0xffff) return false;
        }
        return true;
    }

    public static Paletted NewPaletted(Rectangle r, Palette p)
    {
        int bufLen = ImageMath.PixelBufferLength(1, r, "Paletted");
        return new Paletted(new byte[bufLen], 1 * r.Dx(), r, p);
    }
}
