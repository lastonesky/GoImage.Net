// Port of Go's image/bmp/writer.go (BMP image encoder).
// Copyright 2013 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Buffers.Binary;
using GoImage.Color;
using GoImage.Image;

namespace GoImage.Bmp;

/// <summary>
/// BmpWriter implements a BMP image encoder.
/// </summary>
public static class BmpWriter
{
    private struct Header
    {
        // File header (14 bytes)
        public byte SigB, SigM;        // 'B', 'M'
        public uint FileSize;
        public ushort Reserved1, Reserved2;
        public uint PixOffset;
        // DIB header (BITMAPINFOHEADER, 40 bytes)
        public uint DibHeaderSize;
        public uint Width;
        public uint Height;
        public ushort ColorPlane;
        public ushort Bpp;
        public uint Compression;
        public uint ImageSize;
        public uint XPixelsPerMeter;
        public uint YPixelsPerMeter;
        public uint ColorUse;
        public uint ColorImportant;

        /// <summary>
        /// Write the 50-byte header (14 file + 36 DIB, total 50) to the stream.
        /// Note: Go's binary.Write writes the struct as 50 bytes (2+4+4+4+4 + 4+4+4+2+2+4+4+4+4+4+4).
        /// We write it manually for exact binary compatibility.
        /// </summary>
        public void WriteTo(Stream w)
        {
            // File header (14 bytes)
            w.WriteByte(SigB);
            w.WriteByte(SigM);
            WriteUint32(w, FileSize);
            WriteUint16(w, Reserved1);
            WriteUint16(w, Reserved2);
            WriteUint32(w, PixOffset);
            // DIB header (BITMAPINFOHEADER = 40 bytes, but we write 36 here since
            // DibHeaderSize itself is the first 4 bytes of the DIB header)
            WriteUint32(w, DibHeaderSize);
            WriteUint32(w, Width);
            WriteUint32(w, Height);
            WriteUint16(w, ColorPlane);
            WriteUint16(w, Bpp);
            WriteUint32(w, Compression);
            WriteUint32(w, ImageSize);
            WriteUint32(w, XPixelsPerMeter);
            WriteUint32(w, YPixelsPerMeter);
            WriteUint32(w, ColorUse);
            WriteUint32(w, ColorImportant);
        }

        private static void WriteUint16(Stream w, ushort v)
        {
            w.WriteByte((byte)(v & 0xFF));
            w.WriteByte((byte)((v >> 8) & 0xFF));
        }

        private static void WriteUint32(Stream w, uint v)
        {
            w.WriteByte((byte)(v & 0xFF));
            w.WriteByte((byte)((v >> 8) & 0xFF));
            w.WriteByte((byte)((v >> 16) & 0xFF));
            w.WriteByte((byte)((v >> 24) & 0xFF));
        }
    }

    private static void EncodePaletted(Stream w, byte[] pix, int dx, int dy, int stride, int step)
    {
        byte[]? padding = null;
        if (dx < step)
            padding = new byte[step - dx];

        for (int y = dy - 1; y >= 0; y--)
        {
            int min = y * stride;
            int max = y * stride + dx;
            w.Write(pix, min, max - min);
            if (padding != null)
                w.Write(padding, 0, padding.Length);
        }
    }

    private static void EncodeRGBA(Stream w, byte[] pix, int dx, int dy, int stride, int step, bool opaque)
    {
        byte[] buf = new byte[step];
        if (opaque)
        {
            for (int y = dy - 1; y >= 0; y--)
            {
                int min = y * stride;
                int max = y * stride + dx * 4;
                int off = 0;
                for (int i = min; i < max; i += 4)
                {
                    buf[off + 2] = pix[i + 0];
                    buf[off + 1] = pix[i + 1];
                    buf[off + 0] = pix[i + 2];
                    off += 3;
                }
                w.Write(buf, 0, step);
            }
        }
        else
        {
            for (int y = dy - 1; y >= 0; y--)
            {
                int min = y * stride;
                int max = y * stride + dx * 4;
                int off = 0;
                for (int i = min; i < max; i += 4)
                {
                    uint a = pix[i + 3];
                    if (a == 0)
                    {
                        buf[off + 2] = 0;
                        buf[off + 1] = 0;
                        buf[off + 0] = 0;
                        buf[off + 3] = 0;
                        off += 4;
                        continue;
                    }
                    else if (a == 0xff)
                    {
                        buf[off + 2] = pix[i + 0];
                        buf[off + 1] = pix[i + 1];
                        buf[off + 0] = pix[i + 2];
                        buf[off + 3] = 0xff;
                        off += 4;
                        continue;
                    }
                    buf[off + 2] = (byte)(((uint)pix[i + 0] * 0xffff / a) >> 8);
                    buf[off + 1] = (byte)(((uint)pix[i + 1] * 0xffff / a) >> 8);
                    buf[off + 0] = (byte)(((uint)pix[i + 2] * 0xffff / a) >> 8);
                    buf[off + 3] = (byte)a;
                    off += 4;
                }
                w.Write(buf, 0, step);
            }
        }
    }

    private static void EncodeNRGBA(Stream w, byte[] pix, int dx, int dy, int stride, int step, bool opaque)
    {
        byte[] buf = new byte[step];
        if (opaque)
        {
            for (int y = dy - 1; y >= 0; y--)
            {
                int min = y * stride;
                int max = y * stride + dx * 4;
                int off = 0;
                for (int i = min; i < max; i += 4)
                {
                    buf[off + 2] = pix[i + 0];
                    buf[off + 1] = pix[i + 1];
                    buf[off + 0] = pix[i + 2];
                    off += 3;
                }
                w.Write(buf, 0, step);
            }
        }
        else
        {
            for (int y = dy - 1; y >= 0; y--)
            {
                int min = y * stride;
                int max = y * stride + dx * 4;
                int off = 0;
                for (int i = min; i < max; i += 4)
                {
                    buf[off + 2] = pix[i + 0];
                    buf[off + 1] = pix[i + 1];
                    buf[off + 0] = pix[i + 2];
                    buf[off + 3] = pix[i + 3];
                    off += 4;
                }
                w.Write(buf, 0, step);
            }
        }
    }

    private static void EncodeGeneric(Stream w, IImage m, int step)
    {
        var b = m.Bounds();
        byte[] buf = new byte[step];
        for (int y = b.Max.Y - 1; y >= b.Min.Y; y--)
        {
            int off = 0;
            for (int x = b.Min.X; x < b.Max.X; x++)
            {
                var (r, g, bb, _) = m.At(x, y).GetRGBA();
                buf[off + 2] = (byte)(r >> 8);
                buf[off + 1] = (byte)(g >> 8);
                buf[off + 0] = (byte)(bb >> 8);
                off += 3;
            }
            w.Write(buf, 0, step);
        }
    }

    /// <summary>
    /// Encode writes the image m to w in BMP format.
    /// </summary>
    public static void Encode(Stream w, IImage m)
    {
        var d = m.Bounds().Size();
        if (d.X < 0 || d.Y < 0)
            throw new ArgumentException("bmp: negative bounds");

        var h = new Header
        {
            SigB = (byte)'B',
            SigM = (byte)'M',
            FileSize = 14 + 40,
            PixOffset = 14 + 40,
            DibHeaderSize = 40,
            Width = (uint)d.X,
            Height = (uint)d.Y,
            ColorPlane = 1,
        };

        int step = 0;
        byte[]? palette = null;
        bool opaque = false;

        switch (m)
        {
            case Image.Gray gray:
            {
                step = (d.X + 3) & ~3;
                palette = new byte[1024];
                for (int i = 0; i < 256; i++)
                {
                    palette[i * 4 + 0] = (byte)i;
                    palette[i * 4 + 1] = (byte)i;
                    palette[i * 4 + 2] = (byte)i;
                    palette[i * 4 + 3] = 0xFF;
                }
                h.ImageSize = (uint)(d.Y * step);
                h.FileSize += (uint)(palette.Length) + h.ImageSize;
                h.PixOffset += (uint)(palette.Length);
                h.Bpp = 8;
                break;
            }
            case Paletted paletted:
            {
                step = (d.X + 3) & ~3;
                palette = new byte[1024];
                for (int i = 0; i < paletted.Palette.Count && i < 256; i++)
                {
                    var (r, g, b, _) = paletted.Palette[i].GetRGBA();
                    palette[i * 4 + 0] = (byte)(b >> 8);
                    palette[i * 4 + 1] = (byte)(g >> 8);
                    palette[i * 4 + 2] = (byte)(r >> 8);
                    palette[i * 4 + 3] = 0xFF;
                }
                h.ImageSize = (uint)(d.Y * step);
                h.FileSize += (uint)(palette.Length) + h.ImageSize;
                h.PixOffset += (uint)(palette.Length);
                h.Bpp = 8;
                break;
            }
            case Image.RGBA rgba:
            {
                opaque = rgba.Opaque();
                if (opaque)
                {
                    step = (3 * d.X + 3) & ~3;
                    h.Bpp = 24;
                }
                else
                {
                    step = 4 * d.X;
                    h.Bpp = 32;
                }
                h.ImageSize = (uint)(d.Y * step);
                h.FileSize += h.ImageSize;
                break;
            }
            case Image.NRGBA nrgba:
            {
                // NRGBA doesn't have Opaque() in this codebase, so check pixels
                opaque = IsOpaqueNRGBA(nrgba);
                if (opaque)
                {
                    step = (3 * d.X + 3) & ~3;
                    h.Bpp = 24;
                }
                else
                {
                    step = 4 * d.X;
                    h.Bpp = 32;
                }
                h.ImageSize = (uint)(d.Y * step);
                h.FileSize += h.ImageSize;
                break;
            }
            default:
            {
                step = (3 * d.X + 3) & ~3;
                h.ImageSize = (uint)(d.Y * step);
                h.FileSize += h.ImageSize;
                h.Bpp = 24;
                break;
            }
        }

        h.WriteTo(w);

        if (palette != null)
            w.Write(palette, 0, palette.Length);

        if (d.X == 0 || d.Y == 0)
            return;

        switch (m)
        {
            case Image.Gray gray:
                EncodePaletted(w, gray.Pix, d.X, d.Y, gray.Stride, step);
                break;
            case Paletted paletted:
                EncodePaletted(w, paletted.Pix, d.X, d.Y, paletted.Stride, step);
                break;
            case Image.RGBA rgba:
                EncodeRGBA(w, rgba.Pix, d.X, d.Y, rgba.Stride, step, opaque);
                break;
            case Image.NRGBA nrgba:
                EncodeNRGBA(w, nrgba.Pix, d.X, d.Y, nrgba.Stride, step, opaque);
                break;
            default:
                EncodeGeneric(w, m, step);
                break;
        }
    }

    /// <summary>
    /// Check if an NRGBA image is fully opaque (since NRGBA type in this codebase
    /// doesn't have an Opaque() method like Go's image.NRGBA).
    /// </summary>
    private static bool IsOpaqueNRGBA(Image.NRGBA nrgba)
    {
        var r = nrgba.Rect;
        if (r.Empty()) return true;
        int i0 = 3, i1 = r.Dx() * 4;
        for (int y = r.Min.Y; y < r.Max.Y; y++)
        {
            for (int i = i0; i < i1; i += 4)
            {
                if (nrgba.Pix[i] != 0xff) return false;
            }
            i0 += nrgba.Stride;
            i1 += nrgba.Stride;
        }
        return true;
    }
}
