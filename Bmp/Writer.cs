// Port of Go's image/bmp/writer.go (BMP image encoder).
// Copyright 2013 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
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

        public void WriteTo(Stream w)
        {
            Span<byte> b = stackalloc byte[54];
            b[0] = SigB;
            b[1] = SigM;
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(2), FileSize);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(6), Reserved1);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(8), Reserved2);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(10), PixOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(14), DibHeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(18), Width);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(22), Height);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(26), ColorPlane);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(28), Bpp);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(30), Compression);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(34), ImageSize);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(38), XPixelsPerMeter);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(42), YPixelsPerMeter);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(46), ColorUse);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(50), ColorImportant);
            w.Write(b);
        }
    }

    private static void EncodePaletted(Stream w, byte[] pix, int dx, int dy, int stride, int step)
    {
        byte[] b = ArrayPool<byte>.Shared.Rent(step);
        try
        {
            b.AsSpan(0, step).Clear();
            for (int y = dy - 1; y >= 0; y--)
            {
                int min = y * stride;
                pix.AsSpan(min, dx).CopyTo(b);
                w.Write(b, 0, step);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(b);
        }
    }

    private static void EncodeRGBA(Stream w, Image.RGBA rgba, int dx, int dy, int step, bool opaque)
    {
        byte[] buf = ArrayPool<byte>.Shared.Rent(step);
        try
        {
            if (opaque)
            {
                for (int y = dy - 1; y >= 0; y--)
                {
                    var row = rgba.GetRowSpan(y);
                    int off = 0;
                    for (int i = 0; i < dx; i++)
                    {
                        var p = row[i];
                        buf[off + 2] = p.R;
                        buf[off + 1] = p.G;
                        buf[off + 0] = p.B;
                        off += 3;
                    }
                    w.Write(buf, 0, step);
                }
            }
            else
            {
                for (int y = dy - 1; y >= 0; y--)
                {
                    var row = rgba.GetRowSpan(y);
                    int off = 0;
                    for (int i = 0; i < dx; i++)
                    {
                        var p = row[i];
                        uint a = p.A;
                        if (a == 0)
                        {
                            buf[off + 2] = 0;
                            buf[off + 1] = 0;
                            buf[off + 0] = 0;
                            buf[off + 3] = 0;
                        }
                        else if (a == 0xff)
                        {
                            buf[off + 2] = p.R;
                            buf[off + 1] = p.G;
                            buf[off + 0] = p.B;
                            buf[off + 3] = 0xff;
                        }
                        else
                        {
                            buf[off + 2] = (byte)(((uint)p.R * 0xffff / a) >> 8);
                            buf[off + 1] = (byte)(((uint)p.G * 0xffff / a) >> 8);
                            buf[off + 0] = (byte)(((uint)p.B * 0xffff / a) >> 8);
                            buf[off + 3] = (byte)a;
                        }
                        off += 4;
                    }
                    w.Write(buf, 0, step);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private static void EncodeNRGBA(Stream w, Image.NRGBA nrgba, int dx, int dy, int step, bool opaque)
    {
        byte[] buf = ArrayPool<byte>.Shared.Rent(step);
        try
        {
            if (opaque)
            {
                for (int y = dy - 1; y >= 0; y--)
                {
                    var row = nrgba.GetRowSpan(y);
                    int off = 0;
                    for (int i = 0; i < dx; i++)
                    {
                        var p = row[i];
                        buf[off + 2] = p.R;
                        buf[off + 1] = p.G;
                        buf[off + 0] = p.B;
                        off += 3;
                    }
                    w.Write(buf, 0, step);
                }
            }
            else
            {
                for (int y = dy - 1; y >= 0; y--)
                {
                    var row = nrgba.GetRowSpan(y);
                    int off = 0;
                    for (int i = 0; i < dx; i++)
                    {
                        var p = row[i];
                        buf[off + 2] = p.R;
                        buf[off + 1] = p.G;
                        buf[off + 0] = p.B;
                        buf[off + 3] = p.A;
                        off += 4;
                    }
                    w.Write(buf, 0, step);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private static void EncodeGeneric(Stream w, IImage m, int step)
    {
        var b = m.Bounds();
        byte[] buf = ArrayPool<byte>.Shared.Rent(step);
        try
        {
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
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
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
            Reserved1 = 0,
            Reserved2 = 0,
            DibHeaderSize = 40,
            Width = (uint)d.X,
            Height = (uint)d.Y,
            ColorPlane = 1,
            Compression = 0,
            XPixelsPerMeter = 0,
            YPixelsPerMeter = 0,
            ColorUse = 0,
            ColorImportant = 0,
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
                    opaque = nrgba.Opaque();
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
                EncodeRGBA(w, rgba, d.X, d.Y, step, opaque);
                break;
            case Image.NRGBA nrgba:
                EncodeNRGBA(w, nrgba, d.X, d.Y, step, opaque);
                break;
            default:
                EncodeGeneric(w, m, step);
                break;
        }
    }
}
