// Port of Go's image/draw/draw.go (Image drawing and composition).
// Copyright 2009 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using GoImage.Color;
using GoImage.Image;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace GoImage.Draw;

/// <summary>
/// Op is a Porter-Duff compositing operator.
/// </summary>
public enum Op
{
    /// <summary>
    /// Over specifies "(src in mask) over dst".
    /// </summary>
    Over = 0,
    /// <summary>
    /// Src specifies "(src in mask)".
    /// </summary>
    Src = 1,
}

/// <summary>
/// Draw calls DrawMask with a nil mask.
/// </summary>
public static class Drawer
{
    public static void Draw(IDrawImage dst, Rectangle r, IImage src, Point sp, Op op)
    {
        DrawMask(dst, r, src, sp, null, default, op);
    }

    public static void DrawMask(IDrawImage dst, Rectangle r, IImage src, Point sp, IImage? mask, Point mp, Op op)
    {
        r = r.Intersect(dst.Bounds());
        r = r.Intersect(src.Bounds().Add(r.Min.Sub(sp)));
        if (mask != null)
        {
            r = r.Intersect(mask.Bounds().Add(r.Min.Sub(mp)));
        }

        if (r.Empty()) return;

        // Fast paths
        if (mask == null && op == Op.Src)
        {
            if (dst is GoImage.Image.RGBA rgbaDst)
            {
                if (src is Uniform uniformSrc)
                {
                    DrawFillRGBA(rgbaDst, r, (GoImage.Color.RGBA)ColorModels.RGBAModel.Convert(uniformSrc.C));
                    return;
                }
                if (src is GoImage.Image.RGBA rgbaSrc)
                {
                    DrawCopyRGBA(rgbaDst, r, rgbaSrc, sp);
                    return;
                }
            }
            else if (dst is Paletted palettedDst)
            {
                DrawToPaletted(palettedDst, r, src, sp);
                return;
            }
        }

        if (mask == null && op == Op.Over && dst is GoImage.Image.RGBA rgbaDstOver && src is GoImage.Image.RGBA rgbaSrcOver)
        {
            DrawOverRGBA(rgbaDstOver, r, rgbaSrcOver, sp);
            return;
        }

        // Generic path
        int x0 = r.Min.X, x1 = r.Max.X;
        int y0 = r.Min.Y, y1 = r.Max.Y;
        int dx = sp.X - x0;
        int dy = sp.Y - y0;
        int mx = mp.X - x0;
        int my = mp.Y - y0;

        if (mask == null)
        {
            if (op == Op.Over)
            {
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        var s = src.At(x + dx, y + dy);
                        var d = dst.At(x, y);
                        dst.Set(x, y, Over(d, s));
                    }
                }
            }
            else
            {
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        dst.Set(x, y, src.At(x + dx, y + dy));
                    }
                }
            }
        }
        else
        {
            if (op == Op.Over)
            {
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        var m = mask.At(x + mx, y + my);
                        var (_, _, _, ma) = m.GetRGBA();
                        if (ma == 0) continue;
                        if (ma == 0xffff)
                        {
                            var s = src.At(x + dx, y + dy);
                            var d = dst.At(x, y);
                            dst.Set(x, y, Over(d, s));
                        }
                        else
                        {
                            var s = src.At(x + dx, y + dy);
                            var d = dst.At(x, y);
                            dst.Set(x, y, Over(d, Mask(s, ma)));
                        }
                    }
                }
            }
            else
            {
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        var m = mask.At(x + mx, y + my);
                        var (_, _, _, ma) = m.GetRGBA();
                        if (ma == 0)
                        {
                            dst.Set(x, y, StandardColors.Transparent);
                        }
                        else if (ma == 0xffff)
                        {
                            dst.Set(x, y, src.At(x + dx, y + dy));
                        }
                        else
                        {
                            var s = src.At(x + dx, y + dy);
                            dst.Set(x, y, Mask(s, ma));
                        }
                    }
                }
            }
        }
    }

    private static IColor Over(IColor dst, IColor src)
    {
        var (dr, dg, db, da) = dst.GetRGBA();
        if (da == 0) return src;
        var (sr, sg, sb, sa) = src.GetRGBA();
        if (sa == 0) return dst;
        if (sa == 0xffff) return src;

        uint a = 0xffff - sa;
        return new RGBA64(
            (ushort)((sr + (dr * a) / 0xffff)),
            (ushort)((sg + (dg * a) / 0xffff)),
            (ushort)((sb + (db * a) / 0xffff)),
            (ushort)((sa + (da * a) / 0xffff))
        );
    }

    private static IColor Mask(IColor src, uint ma)
    {
        var (sr, sg, sb, sa) = src.GetRGBA();
        return new RGBA64(
            (ushort)((sr * ma) / 0xffff),
            (ushort)((sg * ma) / 0xffff),
            (ushort)((sb * ma) / 0xffff),
            (ushort)((sa * ma) / 0xffff)
        );
    }

    private static void DrawFillRGBA(GoImage.Image.RGBA dst, Rectangle r, GoImage.Color.RGBA c)
    {
        for (int y = r.Min.Y; y < r.Max.Y; y++)
        {
            var row = dst.GetRowSpan(y).Slice(r.Min.X - dst.Rect.Min.X, r.Dx());
            row.Fill(c);
        }
    }

    private static void DrawCopyRGBA(GoImage.Image.RGBA dst, Rectangle r, GoImage.Image.RGBA src, Point sp)
    {
        int dx = sp.X - r.Min.X;
        int dy = sp.Y - r.Min.Y;
        for (int y = r.Min.Y; y < r.Max.Y; y++)
        {
            var dstRow = dst.GetRowSpan(y).Slice(r.Min.X - dst.Rect.Min.X, r.Dx());
            var srcRow = src.GetRowSpan(y + dy).Slice(r.Min.X + dx - src.Rect.Min.X, r.Dx());
            srcRow.CopyTo(dstRow);
        }
    }

    private static void DrawOverRGBA(GoImage.Image.RGBA dst, Rectangle r, GoImage.Image.RGBA src, Point sp)
    {
        int dx = sp.X - r.Min.X;
        int dy = sp.Y - r.Min.Y;
        for (int y = r.Min.Y; y < r.Max.Y; y++)
        {
            var dstRow = dst.GetRowSpan(y).Slice(r.Min.X - dst.Rect.Min.X, r.Dx());
            var srcRow = src.GetRowSpan(y + dy).Slice(r.Min.X + dx - src.Rect.Min.X, r.Dx());
            
            for (int x = 0; x < r.Dx(); x++)
            {
                var s = srcRow[x];
                if (s.A == 0) continue;
                if (s.A == 255)
                {
                    dstRow[x] = s;
                    continue;
                }
                var d = dstRow[x];
                int a = 255 - s.A;
                dstRow[x] = new GoImage.Color.RGBA(
                    (byte)(s.R + (d.R * a) / 255),
                    (byte)(s.G + (d.G * a) / 255),
                    (byte)(s.B + (d.B * a) / 255),
                    (byte)(s.A + (d.A * a) / 255)
                );
            }
        }
    }

    private static void DrawToPaletted(Paletted dst, Rectangle r, IImage src, Point sp)
    {
        int dx = sp.X - r.Min.X;
        int dy = sp.Y - r.Min.Y;
        var pal = dst.Palette;

        for (int y = r.Min.Y; y < r.Max.Y; y++)
        {
            var dstRow = dst.Pix.AsSpan(dst.PixOffset(r.Min.X, y), r.Dx());
            if (src is GoImage.Image.RGBA rgbaSrc)
            {
                var srcRow = rgbaSrc.GetRowSpan(y + dy).Slice(r.Min.X + dx - rgbaSrc.Rect.Min.X, r.Dx());
                for (int x = 0; x < r.Dx(); x++)
                {
                    dstRow[x] = (byte)pal.Index(srcRow[x]);
                }
            }
            else
            {
                for (int x = 0; x < r.Dx(); x++)
                {
                    dstRow[x] = (byte)pal.Index(src.At(x + r.Min.X + dx, y + dy));
                }
            }
        }
    }
}

/// <summary>
/// Uniform is an infinite-sized IImage of uniform color.
/// </summary>
public class Uniform : IImage
{
    public IColor C;
    public Uniform(IColor c) { C = c; }
    public IModel ColorModel() => ColorModels.RGBAModel; // Approximation
    public Rectangle Bounds() => Rect.New(-1000000, -1000000, 1000000, 1000000);
    public IColor At(int x, int y) => C;
}
