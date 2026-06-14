// Port of Go's image/internal/imageutil/impl.go (DrawYCbCr).

using GoImage.Color;
using GoImage.Image;

namespace GoImage.Internal;

/// <summary>
/// DrawYCbCr draws the YCbCr source image on the RGBA destination image with
/// r.Min in dst aligned with sp in src.
/// </summary>
public static class ImageUtil
{
    public static bool DrawYCbCr(Image.RGBA dst, Rectangle r, YCbCr src, Point sp)
    {
        int x0 = (r.Min.X - dst.Rect.Min.X) * 4;
        int x1 = (r.Max.X - dst.Rect.Min.X) * 4;
        int y0 = r.Min.Y - dst.Rect.Min.Y;
        int y1 = r.Max.Y - dst.Rect.Min.Y;

        switch (src.SubsampleRatio)
        {
            case YCbCrSubsampleRatio.YCbCrSubsampleRatio444:
                return DrawYCbCr444(dst, src, sp, x0, x1, y0, y1);
            case YCbCrSubsampleRatio.YCbCrSubsampleRatio422:
                return DrawYCbCr422(dst, src, sp, x0, x1, y0, y1);
            case YCbCrSubsampleRatio.YCbCrSubsampleRatio420:
                return DrawYCbCr420(dst, src, sp, x0, x1, y0, y1);
            case YCbCrSubsampleRatio.YCbCrSubsampleRatio440:
                return DrawYCbCr440(dst, src, sp, x0, x1, y0, y1);
            default:
                return false;
        }
    }

    private static bool DrawYCbCr444(Image.RGBA dst, YCbCr src, Point sp, int x0, int x1, int y0, int y1)
    {
        for (int y = y0, sy = sp.Y; y != y1; y++, sy++)
        {
            int dpixBase = y * dst.Stride;
            int yi = (sy - src.Rect.Min.Y) * src.YStride + (sp.X - src.Rect.Min.X);
            int ci = (sy - src.Rect.Min.Y) * src.CStride + (sp.X - src.Rect.Min.X);
            for (int x = x0; x != x1; x += 4, yi++, ci++)
            {
                ConvertAndStore(dst.Pix, dpixBase + x, src.GetY(yi), src.GetCb(ci), src.GetCr(ci));
            }
        }
        return true;
    }

    private static bool DrawYCbCr422(Image.RGBA dst, YCbCr src, Point sp, int x0, int x1, int y0, int y1)
    {
        for (int y = y0, sy = sp.Y; y != y1; y++, sy++)
        {
            int dpixBase = y * dst.Stride;
            int yi = (sy - src.Rect.Min.Y) * src.YStride + (sp.X - src.Rect.Min.X);
            int ciBase = (sy - src.Rect.Min.Y) * src.CStride - src.Rect.Min.X / 2;
            for (int x = x0, sx = sp.X; x != x1; x += 4, sx++, yi++)
            {
                int ci = ciBase + sx / 2;
                ConvertAndStore(dst.Pix, dpixBase + x, src.GetY(yi), src.GetCb(ci), src.GetCr(ci));
            }
        }
        return true;
    }

    private static bool DrawYCbCr420(Image.RGBA dst, YCbCr src, Point sp, int x0, int x1, int y0, int y1)
    {
        for (int y = y0, sy = sp.Y; y != y1; y++, sy++)
        {
            int dpixBase = y * dst.Stride;
            int yi = (sy - src.Rect.Min.Y) * src.YStride + (sp.X - src.Rect.Min.X);
            int ciBase = (sy / 2 - src.Rect.Min.Y / 2) * src.CStride - src.Rect.Min.X / 2;
            for (int x = x0, sx = sp.X; x != x1; x += 4, sx++, yi++)
            {
                int ci = ciBase + sx / 2;
                ConvertAndStore(dst.Pix, dpixBase + x, src.GetY(yi), src.GetCb(ci), src.GetCr(ci));
            }
        }
        return true;
    }

    private static bool DrawYCbCr440(Image.RGBA dst, YCbCr src, Point sp, int x0, int x1, int y0, int y1)
    {
        for (int y = y0, sy = sp.Y; y != y1; y++, sy++)
        {
            int dpixBase = y * dst.Stride;
            int yi = (sy - src.Rect.Min.Y) * src.YStride + (sp.X - src.Rect.Min.X);
            int ci = (sy / 2 - src.Rect.Min.Y / 2) * src.CStride + (sp.X - src.Rect.Min.X);
            for (int x = x0; x != x1; x += 4, yi++, ci++)
            {
                ConvertAndStore(dst.Pix, dpixBase + x, src.GetY(yi), src.GetCb(ci), src.GetCr(ci));
            }
        }
        return true;
    }

    /// <summary>
    /// Inline YCbCr to RGB conversion matching Go's bit-twiddling implementation.
    /// </summary>
    private static void ConvertAndStore(byte[] pix, int offset, byte yy, byte cb, byte cr)
    {
        int yy1 = yy * 0x10101;
        int cb1 = cb - 128;
        int cr1 = cr - 128;

        int r = yy1 + 91881 * cr1;
        if (((uint)r & 0xff000000) == 0) r >>= 16;
        else r = ~(r >> 31);

        int g = yy1 - 22554 * cb1 - 46802 * cr1;
        if (((uint)g & 0xff000000) == 0) g >>= 16;
        else g = ~(g >> 31);

        int b = yy1 + 116130 * cb1;
        if (((uint)b & 0xff000000) == 0) b >>= 16;
        else b = ~(b >> 31);

        pix[offset] = (byte)r;
        pix[offset + 1] = (byte)g;
        pix[offset + 2] = (byte)b;
        pix[offset + 3] = 255;
    }
}
