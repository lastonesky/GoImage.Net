// Port of Go's image/png/reader.go (PNG image decoder).
// Copyright 2009 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using GoImage.Color;
using GoImage.Image;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

// Type aliases to disambiguate between Color and Image namespaces
using GrayImage = GoImage.Image.Gray;
using RGBAImage = GoImage.Image.RGBA;
using NRGBAImage = GoImage.Image.NRGBA;

namespace GoImage.Png;

// Color type, as per the PNG spec.
internal static class PngColorType
{
    public const int Grayscale = 0;
    public const int TrueColor = 2;
    public const int Paletted = 3;
    public const int GrayscaleAlpha = 4;
    public const int TrueColorAlpha = 6;
}

// A cb is a combination of color type and bit depth.
internal static class Cb
{
    public const int Invalid = 0;
    public const int G1 = 1;
    public const int G2 = 2;
    public const int G4 = 3;
    public const int G8 = 4;
    public const int GA8 = 5;
    public const int TC8 = 6;
    public const int P1 = 7;
    public const int P2 = 8;
    public const int P4 = 9;
    public const int P8 = 10;
    public const int TCA8 = 11;
    public const int G16 = 12;
    public const int GA16 = 13;
    public const int TC16 = 14;
    public const int TCA16 = 15;

    public static bool IsPaletted(int cb) => cb >= P1 && cb <= P8;
    public static bool IsTrueColor(int cb) => cb == TC8 || cb == TC16;
}

// Filter type, as per the PNG spec.
internal static class FilterType
{
    public const int None = 0;
    public const int Sub = 1;
    public const int Up = 2;
    public const int Average = 3;
    public const int Paeth = 4;
    public const int NFilter = 5;
}

// Interlace type.
internal static class InterlaceType
{
    public const int None = 0;
    public const int Adam7 = 1;
}

// Decoding stage for chunk ordering.
internal static class DecodeStage
{
    public const int Start = 0;
    public const int SeenIHDR = 1;
    public const int SeenPLTE = 2;
    public const int SeentRNS = 3;
    public const int SeenIDAT = 4;
    public const int SeenIEND = 5;
}

internal struct InterlaceScan
{
    public int XFactor, YFactor, XOffset, YOffset;
}

internal static class Interlacing
{
    public static readonly InterlaceScan[] Scans =
    [
        new() { XFactor = 8, YFactor = 8, XOffset = 0, YOffset = 0 },
        new() { XFactor = 8, YFactor = 8, XOffset = 4, YOffset = 0 },
        new() { XFactor = 4, YFactor = 8, XOffset = 0, YOffset = 4 },
        new() { XFactor = 4, YFactor = 4, XOffset = 2, YOffset = 0 },
        new() { XFactor = 2, YFactor = 4, XOffset = 0, YOffset = 2 },
        new() { XFactor = 2, YFactor = 2, XOffset = 1, YOffset = 0 },
        new() { XFactor = 1, YFactor = 2, XOffset = 0, YOffset = 1 },
    ];
}

public static class PngReader
{
    private static readonly byte[] pngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static void Register()
    {
        ImageRegistry.RegisterFormat("png", "\x89PNG\r\n\x1a\n", Decode, DecodeConfig);
    }

    public static IImage Decode(Stream r)
    {
        byte[] sig = new byte[8];
        r.ReadExactly(sig);
        if (!sig.AsSpan().SequenceEqual(pngSignature))
            throw new FormatException("png: invalid format");

        var d = new PngDecoder(r);
        return d.Decode();
    }

    public static Config DecodeConfig(Stream r)
    {
        byte[] sig = new byte[8];
        r.ReadExactly(sig);
        if (!sig.AsSpan().SequenceEqual(pngSignature))
            throw new FormatException("png: invalid format");

        var d = new PngDecoder(r);
        return d.DecodeConfig();
    }
}

internal class PngDecoder
{
    // CRC32 with IEEE polynomial (same as Go's crc32.NewIEEE())
    private static readonly uint[] CrcTable = GenerateCrcTable();

    private static uint[] GenerateCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                if ((c & 1) != 0)
                    c = 0xedb88320 ^ (c >> 1);
                else
                    c >>= 1;
            }
            table[n] = c;
        }
        return table;
    }

    private readonly Stream _r;
    private IImage? _img;
    private uint _crcValue;
    private int _width, _height;
    private int _depth;
    private Palette? _palette;
    private int _cb;
    private int _stage;
    private uint _idatLength;
    private readonly byte[] _tmp = new byte[3 * 256];
    private int _interlace;
    private bool _useTransparent;
    private readonly byte[] _transparent = new byte[6];

    public PngDecoder(Stream r)
    {
        _r = r;
    }

    private void CrcReset() => _crcValue = 0xffffffff;

    private void CrcWrite(byte[] data, int offset, int count)
    {
        for (int i = 0; i < count; i++)
            _crcValue = CrcTable[(_crcValue ^ data[offset + i]) & 0xff] ^ (_crcValue >> 8);
    }

    private uint CrcSum32() => _crcValue ^ 0xffffffff;

    private void VerifyChecksum()
    {
        byte[] crcBytes = new byte[4];
        _r.ReadExactly(crcBytes);
        uint expected = BinaryPrimitives.ReadUInt32BigEndian(crcBytes);
        uint actual = CrcSum32();
        if (expected != actual)
            throw new FormatException("png: invalid checksum");
    }

    private void ParseIHDR(uint length)
    {
        if (length != 13)
            throw new FormatException("png: bad IHDR length");
        _r.ReadExactly(_tmp, 0, 13);
        CrcWrite(_tmp, 0, 13);
        if (_tmp[10] != 0)
            throw new NotSupportedException("png: unsupported compression method");
        if (_tmp[11] != 0)
            throw new NotSupportedException("png: unsupported filter method");
        if (_tmp[12] != InterlaceType.None && _tmp[12] != InterlaceType.Adam7)
            throw new FormatException("png: invalid interlace method");
        _interlace = _tmp[12];

        int w = BinaryPrimitives.ReadInt32BigEndian(_tmp.AsSpan(0, 4));
        int h = BinaryPrimitives.ReadInt32BigEndian(_tmp.AsSpan(4, 4));
        if (w <= 0 || h <= 0)
            throw new FormatException("png: non-positive dimension");
        long nPixels64 = (long)w * h;
        int nPixels = (int)nPixels64;
        if (nPixels64 != nPixels)
            throw new NotSupportedException("png: dimension overflow");
        // There can be up to 8 bytes per pixel, for 16 bits per channel RGBA.
        if (nPixels != (nPixels * 8) / 8)
            throw new NotSupportedException("png: dimension overflow");

        _cb = Cb.Invalid;
        _depth = _tmp[8];
        switch (_depth)
        {
            case 1:
                switch (_tmp[9])
                {
                    case PngColorType.Grayscale: _cb = Cb.G1; break;
                    case PngColorType.Paletted: _cb = Cb.P1; break;
                }
                break;
            case 2:
                switch (_tmp[9])
                {
                    case PngColorType.Grayscale: _cb = Cb.G2; break;
                    case PngColorType.Paletted: _cb = Cb.P2; break;
                }
                break;
            case 4:
                switch (_tmp[9])
                {
                    case PngColorType.Grayscale: _cb = Cb.G4; break;
                    case PngColorType.Paletted: _cb = Cb.P4; break;
                }
                break;
            case 8:
                switch (_tmp[9])
                {
                    case PngColorType.Grayscale: _cb = Cb.G8; break;
                    case PngColorType.TrueColor: _cb = Cb.TC8; break;
                    case PngColorType.Paletted: _cb = Cb.P8; break;
                    case PngColorType.GrayscaleAlpha: _cb = Cb.GA8; break;
                    case PngColorType.TrueColorAlpha: _cb = Cb.TCA8; break;
                }
                break;
            case 16:
                switch (_tmp[9])
                {
                    case PngColorType.Grayscale: _cb = Cb.G16; break;
                    case PngColorType.TrueColor: _cb = Cb.TC16; break;
                    case PngColorType.GrayscaleAlpha: _cb = Cb.GA16; break;
                    case PngColorType.TrueColorAlpha: _cb = Cb.TCA16; break;
                }
                break;
        }
        if (_cb == Cb.Invalid)
            throw new NotSupportedException($"png: bit depth {_depth}, color type {_tmp[9]}");
        _width = w;
        _height = h;
        VerifyChecksum();
    }

    private void ParsePLTE(uint length)
    {
        int np = (int)(length / 3); // The number of palette entries.
        if (length % 3 != 0 || np <= 0 || np > 256 || np > 1 << _depth)
            throw new FormatException("png: bad PLTE length");
        int n = (int)length;
        _r.ReadExactly(_tmp, 0, n);
        CrcWrite(_tmp, 0, n);

        switch (_cb)
        {
            case Cb.P1:
            case Cb.P2:
            case Cb.P4:
            case Cb.P8:
                _palette = new Palette();
                for (int i = 0; i < 256; i++)
                    _palette.Add(new Color.RGBA(0, 0, 0, 0xff));
                for (int i = 0; i < np; i++)
                    _palette[i] = new Color.RGBA(_tmp[3 * i], _tmp[3 * i + 1], _tmp[3 * i + 2], 0xff);
                // Trim to actual palette size — remaining entries are opaque black fallback
                _palette.RemoveRange(np, 256 - np);
                break;
            case Cb.TC8:
            case Cb.TCA8:
            case Cb.TC16:
            case Cb.TCA16:
                // PLTE is optional for truecolor images; ignore.
                break;
            default:
                throw new FormatException("png: PLTE, color type mismatch");
        }
        VerifyChecksum();
    }

    private void ParsetRNS(uint length)
    {
        switch (_cb)
        {
            case Cb.G1:
            case Cb.G2:
            case Cb.G4:
            case Cb.G8:
            case Cb.G16:
                if (length != 2)
                    throw new FormatException("png: bad tRNS length");
                _r.ReadExactly(_tmp, 0, (int)length);
                CrcWrite(_tmp, 0, (int)length);
                Array.Copy(_tmp, 0, _transparent, 0, length);
                switch (_cb)
                {
                    case Cb.G1: _transparent[1] *= 0xff; break;
                    case Cb.G2: _transparent[1] *= 0x55; break;
                    case Cb.G4: _transparent[1] *= 0x11; break;
                }
                _useTransparent = true;
                break;

            case Cb.TC8:
            case Cb.TC16:
                if (length != 6)
                    throw new FormatException("png: bad tRNS length");
                _r.ReadExactly(_tmp, 0, (int)length);
                CrcWrite(_tmp, 0, (int)length);
                Array.Copy(_tmp, 0, _transparent, 0, length);
                _useTransparent = true;
                break;

            case Cb.P1:
            case Cb.P2:
            case Cb.P4:
            case Cb.P8:
                if (length > 256)
                    throw new FormatException("png: bad tRNS length");
                _r.ReadExactly(_tmp, 0, (int)length);
                CrcWrite(_tmp, 0, (int)length);
                if (_palette != null && _palette.Count < (int)length)
                {
                    // Extend palette if needed (shouldn't happen with well-formed PNGs)
                    while (_palette.Count < (int)length)
                        _palette.Add(new Color.RGBA(0, 0, 0, 0xff));
                }
                for (int i = 0; i < (int)length && _palette != null && i < _palette.Count; i++)
                {
                    var rgba = (Color.RGBA)_palette[i];
                    _palette[i] = new Color.NRGBA(rgba.R, rgba.G, rgba.B, _tmp[i]);
                }
                break;

            default:
                throw new FormatException("png: tRNS, color type mismatch");
        }
        VerifyChecksum();
    }

    // ReadIdat presents one or more IDAT chunks as one continuous stream.
    // See Go's decoder.Read method.
    private int ReadIdat(byte[] p, int offset, int count)
    {
        if (count == 0) return 0;
        while (_idatLength == 0)
        {
            VerifyChecksum();
            // Read next chunk header (length + type)
            _r.ReadExactly(_tmp, 0, 8);
            _idatLength = BinaryPrimitives.ReadUInt32BigEndian(_tmp.AsSpan(0, 4));
            string chunkType = Encoding.ASCII.GetString(_tmp, 4, 4);
            if (chunkType != "IDAT")
                throw new FormatException("png: not enough pixel data");
            CrcReset();
            CrcWrite(_tmp, 4, 4); // CRC covers chunk type
        }
        if (_idatLength > int.MaxValue)
            throw new NotSupportedException("png: IDAT chunk length overflow");
        int toRead = (int)Math.Min((uint)count, _idatLength);
        int n = _r.Read(p, offset, toRead);
        if (n <= 0)
            throw new EndOfStreamException();
        CrcWrite(p, offset, n);
        _idatLength -= (uint)n;
        return n;
    }

    // IdatStream wraps the decoder to present IDAT chunks as a Stream for ZLibStream.
    private sealed class IdatStream : Stream
    {
        private readonly PngDecoder _d;

        public IdatStream(PngDecoder d) => _d = d;

        public override int Read(byte[] buffer, int offset, int count)
            => _d.ReadIdat(buffer, offset, count);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // decode decodes the IDAT data into an image.
    private IImage DecodeIdat()
    {
        var idatStream = new IdatStream(this);
        var zlib = new ZLibStream(idatStream, CompressionMode.Decompress);
        IImage img;
        if (_interlace == InterlaceType.None)
        {
            img = ReadImagePass(zlib, 0, false);
        }
        else if (_interlace == InterlaceType.Adam7)
        {
            // Allocate a blank image of the full size.
            img = ReadImagePass(null!, 0, true);
            for (int pass = 0; pass < 7; pass++)
            {
                var imagePass = ReadImagePass(zlib, pass, false);
                if (imagePass != null)
                {
                    MergePassInto(img, imagePass, pass);
                }
            }
        }
        else
        {
            throw new FormatException("png: invalid interlace");
        }

        // Check for EOF, to verify the zlib checksum.
        byte[] tmp = new byte[1];
        int n = zlib.Read(tmp, 0, 1);
        if (n != 0 || _idatLength != 0)
            throw new FormatException("png: too much pixel data");

        return img;
    }

    // readImagePass reads a single image pass, sized according to the pass number.
    private IImage ReadImagePass(Stream zlib, int pass, bool allocateOnly)
    {
        int bitsPerPixel = 0;
        int pixOffset = 0;
        GrayImage? gray = null;
        RGBAImage? rgba = null;
        Paletted? paletted = null;
        NRGBAImage? nrgba = null;
        Gray16Image? gray16 = null;
        RGBA64Image? rgba64 = null;
        NRGBA64Image? nrgba64 = null;
        IImage img = null!;

        int width = _width, height = _height;
        if (_interlace == InterlaceType.Adam7 && !allocateOnly)
        {
            var p = Interlacing.Scans[pass];
            width = (width - p.XOffset + p.XFactor - 1) / p.XFactor;
            height = (height - p.YOffset + p.YFactor - 1) / p.YFactor;
            if (width == 0 || height == 0)
                return null!;
        }

        switch (_cb)
        {
            case Cb.G1:
            case Cb.G2:
            case Cb.G4:
            case Cb.G8:
                bitsPerPixel = _depth;
                if (_useTransparent)
                {
                    nrgba = NRGBAImage.NewNRGBA(Rect.New(0, 0, width, height));
                    img = nrgba;
                }
                else
                {
                    gray = GrayImage.NewGray(Rect.New(0, 0, width, height));
                    img = gray;
                }
                break;
            case Cb.GA8:
                bitsPerPixel = 16;
                nrgba = NRGBAImage.NewNRGBA(Rect.New(0, 0, width, height));
                img = nrgba;
                break;
            case Cb.TC8:
                bitsPerPixel = 24;
                if (_useTransparent)
                {
                    nrgba = NRGBAImage.NewNRGBA(Rect.New(0, 0, width, height));
                    img = nrgba;
                }
                else
                {
                    rgba = RGBAImage.NewRGBA(Rect.New(0, 0, width, height));
                    img = rgba;
                }
                break;
            case Cb.P1:
            case Cb.P2:
            case Cb.P4:
            case Cb.P8:
                bitsPerPixel = _depth;
                paletted = Paletted.NewPaletted(Rect.New(0, 0, width, height), _palette!);
                img = paletted;
                break;
            case Cb.TCA8:
                bitsPerPixel = 32;
                nrgba = NRGBAImage.NewNRGBA(Rect.New(0, 0, width, height));
                img = nrgba;
                break;
            case Cb.G16:
                bitsPerPixel = 16;
                if (_useTransparent)
                {
                    nrgba64 = NRGBA64Image.NewNRGBA64(Rect.New(0, 0, width, height));
                    img = nrgba64;
                }
                else
                {
                    gray16 = Gray16Image.NewGray16(Rect.New(0, 0, width, height));
                    img = gray16;
                }
                break;
            case Cb.GA16:
                bitsPerPixel = 32;
                nrgba64 = NRGBA64Image.NewNRGBA64(Rect.New(0, 0, width, height));
                img = nrgba64;
                break;
            case Cb.TC16:
                bitsPerPixel = 48;
                if (_useTransparent)
                {
                    nrgba64 = NRGBA64Image.NewNRGBA64(Rect.New(0, 0, width, height));
                    img = nrgba64;
                }
                else
                {
                    rgba64 = RGBA64Image.NewRGBA64(Rect.New(0, 0, width, height));
                    img = rgba64;
                }
                break;
            case Cb.TCA16:
                bitsPerPixel = 64;
                nrgba64 = NRGBA64Image.NewNRGBA64(Rect.New(0, 0, width, height));
                img = nrgba64;
                break;
        }

        if (allocateOnly)
            return img!;

        int bytesPerPixel = (bitsPerPixel + 7) / 8;
        // The +1 is for the per-row filter type, which is at cr[0].
        long rowSizeLong = 1L + ((long)bitsPerPixel * width + 7) / 8;
        if (rowSizeLong > int.MaxValue)
            throw new NotSupportedException("png: dimension overflow");
        int rowSize = (int)rowSizeLong;

        // cr and pr are the bytes for the current and previous row.
        byte[] cr = new byte[rowSize];
        byte[] pr = new byte[rowSize];

        for (int y = 0; y < height; y++)
        {
            // Read the decompressed bytes.
            zlib.ReadExactly(cr);

            // Apply the filter.
            Span<byte> cdat = cr.AsSpan(1);
            Span<byte> pdat = pr.AsSpan(1);
            switch (cr[0])
            {
                case FilterType.None:
                    break;
                case FilterType.Sub:
                    for (int i = bytesPerPixel; i < cdat.Length; i++)
                        cdat[i] += cdat[i - bytesPerPixel];
                    break;
                case FilterType.Up:
                    for (int i = 0; i < cdat.Length; i++)
                        cdat[i] += pdat[i];
                    break;
                case FilterType.Average:
                    for (int i = 0; i < bytesPerPixel; i++)
                        cdat[i] += (byte)(pdat[i] / 2);
                    for (int i = bytesPerPixel; i < cdat.Length; i++)
                        cdat[i] += (byte)((cdat[i - bytesPerPixel] + pdat[i]) / 2);
                    break;
                case FilterType.Paeth:
                    FilterPaeth(cdat, pdat, bytesPerPixel);
                    break;
                default:
                    throw new FormatException("png: bad filter type");
            }

            // Convert from bytes to colors.
            switch (_cb)
            {
                case Cb.G1:
                    if (_useTransparent)
                    {
                        byte ty = _transparent[1];
                        for (int x = 0; x < width; x += 8)
                        {
                            byte b = cdat[x / 8];
                            for (int x2 = 0; x2 < 8 && x + x2 < width; x2++)
                            {
                                byte ycol = (byte)((b >> 7) * 0xff);
                                byte acol = (byte)0xff;
                                if (ycol == ty) acol = 0;
                                nrgba!.SetNRGBA(x + x2, y, new Color.NRGBA(ycol, ycol, ycol, acol));
                                b <<= 1;
                            }
                        }
                    }
                    else
                    {
                        for (int x = 0; x < width; x += 8)
                        {
                            byte b = cdat[x / 8];
                            for (int x2 = 0; x2 < 8 && x + x2 < width; x2++)
                            {
                                gray!.SetGray(x + x2, y, new Color.Gray((byte)((b >> 7) * 0xff)));
                                b <<= 1;
                            }
                        }
                    }
                    break;

                case Cb.G2:
                    if (_useTransparent)
                    {
                        byte ty = _transparent[1];
                        for (int x = 0; x < width; x += 4)
                        {
                            byte b = cdat[x / 4];
                            for (int x2 = 0; x2 < 4 && x + x2 < width; x2++)
                            {
                                byte ycol = (byte)((b >> 6) * 0x55);
                                byte acol = (byte)0xff;
                                if (ycol == ty) acol = 0;
                                nrgba!.SetNRGBA(x + x2, y, new Color.NRGBA(ycol, ycol, ycol, acol));
                                b <<= 2;
                            }
                        }
                    }
                    else
                    {
                        for (int x = 0; x < width; x += 4)
                        {
                            byte b = cdat[x / 4];
                            for (int x2 = 0; x2 < 4 && x + x2 < width; x2++)
                            {
                                gray!.SetGray(x + x2, y, new Color.Gray((byte)((b >> 6) * 0x55)));
                                b <<= 2;
                            }
                        }
                    }
                    break;

                case Cb.G4:
                    if (_useTransparent)
                    {
                        byte ty = _transparent[1];
                        for (int x = 0; x < width; x += 2)
                        {
                            byte b = cdat[x / 2];
                            for (int x2 = 0; x2 < 2 && x + x2 < width; x2++)
                            {
                                byte ycol = (byte)((b >> 4) * 0x11);
                                byte acol = (byte)0xff;
                                if (ycol == ty) acol = 0;
                                nrgba!.SetNRGBA(x + x2, y, new Color.NRGBA(ycol, ycol, ycol, acol));
                                b <<= 4;
                            }
                        }
                    }
                    else
                    {
                        for (int x = 0; x < width; x += 2)
                        {
                            byte b = cdat[x / 2];
                            for (int x2 = 0; x2 < 2 && x + x2 < width; x2++)
                            {
                                gray!.SetGray(x + x2, y, new Color.Gray((byte)((b >> 4) * 0x11)));
                                b <<= 4;
                            }
                        }
                    }
                    break;

                case Cb.G8:
                    if (_useTransparent)
                    {
                        byte ty = _transparent[1];
                        for (int x = 0; x < width; x++)
                        {
                            byte ycol = cdat[x];
                            byte acol = (byte)0xff;
                            if (ycol == ty) acol = 0;
                            nrgba!.SetNRGBA(x, y, new Color.NRGBA(ycol, ycol, ycol, acol));
                        }
                    }
                    else
                    {
                        cdat[..width].CopyTo(gray!.Pix.AsSpan(pixOffset));
                        pixOffset += gray.Stride;
                    }
                    break;

                case Cb.GA8:
                    for (int x = 0; x < width; x++)
                    {
                        byte ycol = cdat[2 * x];
                        nrgba!.SetNRGBA(x, y, new Color.NRGBA(ycol, ycol, ycol, cdat[2 * x + 1]));
                    }
                    break;

                case Cb.TC8:
                    if (_useTransparent)
                    {
                        var pix = nrgba!.Pix;
                        int i = pixOffset;
                        int j = 0;
                        byte tr = _transparent[1], tg = _transparent[3], tb = _transparent[5];
                        for (int x = 0; x < width; x++)
                        {
                            byte r = cdat[j];
                            byte g = cdat[j + 1];
                            byte b = cdat[j + 2];
                            byte a = (byte)0xff;
                            if (r == tr && g == tg && b == tb) a = 0;
                            pix[i] = r; pix[i + 1] = g; pix[i + 2] = b; pix[i + 3] = a;
                            i += 4; j += 3;
                        }
                        pixOffset += nrgba.Stride;
                    }
                    else
                    {
                        var pix = rgba!.Pix;
                        int i = pixOffset;
                        int j = 0;
                        for (int x = 0; x < width; x++)
                        {
                            pix[i] = cdat[j];
                            pix[i + 1] = cdat[j + 1];
                            pix[i + 2] = cdat[j + 2];
                            pix[i + 3] = 0xff;
                            i += 4; j += 3;
                        }
                        pixOffset += rgba.Stride;
                    }
                    break;

                case Cb.P1:
                    for (int x = 0; x < width; x += 8)
                    {
                        byte b = cdat[x / 8];
                        for (int x2 = 0; x2 < 8 && x + x2 < width; x2++)
                        {
                            byte idx = (byte)(b >> 7);
                            if (paletted!.Palette.Count <= idx)
                            {
                                while (paletted.Palette.Count <= idx)
                                    paletted.Palette.Add(new Color.RGBA(0, 0, 0, 0xff));
                            }
                            paletted.SetColorIndex(x + x2, y, idx);
                            b <<= 1;
                        }
                    }
                    break;

                case Cb.P2:
                    for (int x = 0; x < width; x += 4)
                    {
                        byte b = cdat[x / 4];
                        for (int x2 = 0; x2 < 4 && x + x2 < width; x2++)
                        {
                            byte idx = (byte)(b >> 6);
                            if (paletted!.Palette.Count <= idx)
                            {
                                while (paletted.Palette.Count <= idx)
                                    paletted.Palette.Add(new Color.RGBA(0, 0, 0, 0xff));
                            }
                            paletted.SetColorIndex(x + x2, y, idx);
                            b <<= 2;
                        }
                    }
                    break;

                case Cb.P4:
                    for (int x = 0; x < width; x += 2)
                    {
                        byte b = cdat[x / 2];
                        for (int x2 = 0; x2 < 2 && x + x2 < width; x2++)
                        {
                            byte idx = (byte)(b >> 4);
                            if (paletted!.Palette.Count <= idx)
                            {
                                while (paletted.Palette.Count <= idx)
                                    paletted.Palette.Add(new Color.RGBA(0, 0, 0, 0xff));
                            }
                            paletted.SetColorIndex(x + x2, y, idx);
                            b <<= 4;
                        }
                    }
                    break;

                case Cb.P8:
                    if (paletted!.Palette.Count != 256)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            if (paletted.Palette.Count <= cdat[x])
                            {
                                while (paletted.Palette.Count <= cdat[x])
                                    paletted.Palette.Add(new Color.RGBA(0, 0, 0, 0xff));
                            }
                        }
                    }
                    cdat[..width].CopyTo(paletted.Pix.AsSpan(pixOffset));
                    pixOffset += paletted.Stride;
                    break;

                case Cb.TCA8:
                    cdat[..(width * 4)].CopyTo(nrgba!.Pix.AsSpan(pixOffset));
                    pixOffset += nrgba.Stride;
                    break;

                case Cb.G16:
                    if (_useTransparent)
                    {
                        ushort ty = (ushort)((_transparent[0] << 8) | _transparent[1]);
                        for (int x = 0; x < width; x++)
                        {
                            ushort ycol = (ushort)((cdat[2 * x] << 8) | cdat[2 * x + 1]);
                            ushort acol = (ushort)0xffff;
                            if (ycol == ty) acol = 0;
                            nrgba64!.SetNRGBA64(x, y, new Color.NRGBA64(ycol, ycol, ycol, acol));
                        }
                    }
                    else
                    {
                        for (int x = 0; x < width; x++)
                        {
                            ushort ycol = (ushort)((cdat[2 * x] << 8) | cdat[2 * x + 1]);
                            gray16!.SetGray16(x, y, new Color.Gray16(ycol));
                        }
                    }
                    break;

                case Cb.GA16:
                    for (int x = 0; x < width; x++)
                    {
                        ushort ycol = (ushort)((cdat[4 * x] << 8) | cdat[4 * x + 1]);
                        ushort acol = (ushort)((cdat[4 * x + 2] << 8) | cdat[4 * x + 3]);
                        nrgba64!.SetNRGBA64(x, y, new Color.NRGBA64(ycol, ycol, ycol, acol));
                    }
                    break;

                case Cb.TC16:
                    if (_useTransparent)
                    {
                        ushort tr = (ushort)((_transparent[0] << 8) | _transparent[1]);
                        ushort tg = (ushort)((_transparent[2] << 8) | _transparent[3]);
                        ushort tb = (ushort)((_transparent[4] << 8) | _transparent[5]);
                        for (int x = 0; x < width; x++)
                        {
                            ushort rcol = (ushort)((cdat[6 * x] << 8) | cdat[6 * x + 1]);
                            ushort gcol = (ushort)((cdat[6 * x + 2] << 8) | cdat[6 * x + 3]);
                            ushort bcol = (ushort)((cdat[6 * x + 4] << 8) | cdat[6 * x + 5]);
                            ushort acol = (ushort)0xffff;
                            if (rcol == tr && gcol == tg && bcol == tb) acol = 0;
                            nrgba64!.SetNRGBA64(x, y, new Color.NRGBA64(rcol, gcol, bcol, acol));
                        }
                    }
                    else
                    {
                        for (int x = 0; x < width; x++)
                        {
                            ushort rcol = (ushort)((cdat[6 * x] << 8) | cdat[6 * x + 1]);
                            ushort gcol = (ushort)((cdat[6 * x + 2] << 8) | cdat[6 * x + 3]);
                            ushort bcol = (ushort)((cdat[6 * x + 4] << 8) | cdat[6 * x + 5]);
                            rgba64!.SetRGBA64(x, y, new Color.RGBA64(rcol, gcol, bcol, 0xffff));
                        }
                    }
                    break;

                case Cb.TCA16:
                    for (int x = 0; x < width; x++)
                    {
                        ushort rcol = (ushort)((cdat[8 * x] << 8) | cdat[8 * x + 1]);
                        ushort gcol = (ushort)((cdat[8 * x + 2] << 8) | cdat[8 * x + 3]);
                        ushort bcol = (ushort)((cdat[8 * x + 4] << 8) | cdat[8 * x + 5]);
                        ushort acol = (ushort)((cdat[8 * x + 6] << 8) | cdat[8 * x + 7]);
                        nrgba64!.SetNRGBA64(x, y, new Color.NRGBA64(rcol, gcol, bcol, acol));
                    }
                    break;
            }

            // The current row for y is the previous row for y+1.
            (pr, cr) = (cr, pr);
        }

        return img!;
    }

    // mergePassInto merges a single pass into a full sized image.
    private void MergePassInto(IImage dst, IImage src, int pass)
    {
        var p = Interlacing.Scans[pass];
        byte[] srcPix;
        byte[] dstPix;
        int stride;
        Rectangle rect;
        int bytesPerPixel;

        switch (dst)
        {
            case GrayImage g:
                srcPix = ((GrayImage)src).Pix;
                dstPix = g.Pix; stride = g.Stride; rect = g.Rect;
                bytesPerPixel = 1;
                break;
            case Gray16Image g16:
                srcPix = ((Gray16Image)src).Pix;
                dstPix = g16.Pix; stride = g16.Stride; rect = g16.Rect;
                bytesPerPixel = 2;
                break;
            case NRGBAImage n:
                srcPix = ((NRGBAImage)src).Pix;
                dstPix = n.Pix; stride = n.Stride; rect = n.Rect;
                bytesPerPixel = 4;
                break;
            case NRGBA64Image n64:
                srcPix = ((NRGBA64Image)src).Pix;
                dstPix = n64.Pix; stride = n64.Stride; rect = n64.Rect;
                bytesPerPixel = 8;
                break;
            case Paletted pt:
                var source = (Paletted)src;
                srcPix = source.Pix;
                dstPix = pt.Pix; stride = pt.Stride; rect = pt.Rect;
                bytesPerPixel = 1;
                if (pt.Palette.Count < source.Palette.Count)
                    pt.Palette = source.Palette;
                break;
            case RGBAImage r:
                srcPix = ((RGBAImage)src).Pix;
                dstPix = r.Pix; stride = r.Stride; rect = r.Rect;
                bytesPerPixel = 4;
                break;
            case RGBA64Image r64:
                srcPix = ((RGBA64Image)src).Pix;
                dstPix = r64.Pix; stride = r64.Stride; rect = r64.Rect;
                bytesPerPixel = 8;
                break;
            default:
                throw new NotSupportedException("png: unexpected image type in mergePassInto");
        }

        var srcBounds = src.Bounds();
        int s = 0;
        for (int y = srcBounds.Min.Y; y < srcBounds.Max.Y; y++)
        {
            int dBase = (y * p.YFactor + p.YOffset - rect.Min.Y) * stride +
                        (p.XOffset - rect.Min.X) * bytesPerPixel;
            for (int x = srcBounds.Min.X; x < srcBounds.Max.X; x++)
            {
                int d = dBase + x * p.XFactor * bytesPerPixel;
                Array.Copy(srcPix, s, dstPix, d, bytesPerPixel);
                s += bytesPerPixel;
            }
        }
    }

    private static void FilterPaeth(Span<byte> cdat, Span<byte> pdat, int bytesPerPixel)
    {
        for (int i = 0; i < bytesPerPixel; i++)
            cdat[i] += Paeth(0, pdat[i], 0);
        for (int i = bytesPerPixel; i < cdat.Length; i++)
            cdat[i] += Paeth(cdat[i - bytesPerPixel], pdat[i], pdat[i - bytesPerPixel]);
    }

    private static byte Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return (byte)a;
        if (pb <= pc) return (byte)b;
        return (byte)c;
    }

    // ParseIDAT handles the IDAT chunk.
    private void ParseIDAT(uint length)
    {
        _idatLength = length;
        _img = DecodeIdat();
        VerifyChecksum();
    }

    private void ParseIEND(uint length)
    {
        if (length != 0)
            throw new FormatException("png: bad IEND length");
        VerifyChecksum();
    }

    private void ParseChunk(bool configOnly)
    {
        // Read the length and chunk type.
        _r.ReadExactly(_tmp, 0, 8);
        uint length = BinaryPrimitives.ReadUInt32BigEndian(_tmp.AsSpan(0, 4));
        CrcReset();
        CrcWrite(_tmp, 4, 4); // CRC covers chunk type

        string chunkType = Encoding.ASCII.GetString(_tmp, 4, 4);
        switch (chunkType)
        {
            case "IHDR":
                if (_stage != DecodeStage.Start)
                    throw new FormatException("png: chunk out of order");
                _stage = DecodeStage.SeenIHDR;
                ParseIHDR(length);
                break;

            case "PLTE":
                if (_stage != DecodeStage.SeenIHDR)
                    throw new FormatException("png: chunk out of order");
                _stage = DecodeStage.SeenPLTE;
                ParsePLTE(length);
                break;

            case "tRNS":
                if (Cb.IsPaletted(_cb))
                {
                    if (_stage != DecodeStage.SeenPLTE)
                        throw new FormatException("png: chunk out of order");
                }
                else if (Cb.IsTrueColor(_cb))
                {
                    if (_stage != DecodeStage.SeenIHDR && _stage != DecodeStage.SeenPLTE)
                        throw new FormatException("png: chunk out of order");
                }
                else if (_stage != DecodeStage.SeenIHDR)
                {
                    throw new FormatException("png: chunk out of order");
                }
                _stage = DecodeStage.SeentRNS;
                ParsetRNS(length);
                break;

            case "IDAT":
                if (_stage < DecodeStage.SeenIHDR || _stage > DecodeStage.SeenIDAT ||
                    (_stage == DecodeStage.SeenIHDR && Cb.IsPaletted(_cb)))
                {
                    throw new FormatException("png: chunk out of order");
                }
                else if (_stage == DecodeStage.SeenIDAT)
                {
                    // Ignore trailing zero-length or garbage IDAT chunks.
                    break;
                }
                _stage = DecodeStage.SeenIDAT;
                if (!configOnly)
                    ParseIDAT(length);
                break;

            case "IEND":
                if (_stage != DecodeStage.SeenIDAT)
                    throw new FormatException("png: chunk out of order");
                _stage = DecodeStage.SeenIEND;
                ParseIEND(length);
                break;

            default:
                // Unknown chunk type — skip over it.
                if (length > 0x7fffffff)
                    throw new FormatException($"png: Bad chunk length: {length}");
                byte[] ignored = new byte[4096];
                while (length > 0)
                {
                    int toRead = (int)Math.Min(length, (uint)ignored.Length);
                    _r.ReadExactly(ignored, 0, toRead);
                    CrcWrite(ignored, 0, toRead);
                    length -= (uint)toRead;
                }
                VerifyChecksum();
                break;
        }
    }

    public IImage Decode()
    {
        while (_stage != DecodeStage.SeenIEND)
        {
            ParseChunk(false);
        }
        return _img!;
    }

    public Config DecodeConfig()
    {
        while (true)
        {
            ParseChunk(true);

            if (Cb.IsPaletted(_cb))
            {
                if (_stage >= DecodeStage.SeentRNS) break;
            }
            else
            {
                if (_stage >= DecodeStage.SeenIHDR) break;
            }
        }

        IModel cm;
        switch (_cb)
        {
            case Cb.G1:
            case Cb.G2:
            case Cb.G4:
            case Cb.G8:
                cm = ColorModels.GrayModel;
                break;
            case Cb.GA8:
                cm = ColorModels.NRGBAModel;
                break;
            case Cb.TC8:
                cm = ColorModels.RGBAModel;
                break;
            case Cb.P1:
            case Cb.P2:
            case Cb.P4:
            case Cb.P8:
                cm = _palette!;
                break;
            case Cb.TCA8:
                cm = ColorModels.NRGBAModel;
                break;
            case Cb.G16:
                cm = ColorModels.Gray16Model;
                break;
            case Cb.GA16:
                cm = ColorModels.NRGBA64Model;
                break;
            case Cb.TC16:
                cm = ColorModels.RGBA64Model;
                break;
            case Cb.TCA16:
                cm = ColorModels.NRGBA64Model;
                break;
            default:
                cm = ColorModels.RGBAModel;
                break;
        }

        return new Config { ColorModel = cm, Width = _width, Height = _height };
    }
}
