// Port of Go's image/bmp/reader.go (BMP image decoder).
// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using GoImage.Color;
using GoImage.Image;

namespace GoImage.Bmp;

/// <summary>
/// BmpReader implements a BMP image decoder.
/// The BMP specification is at http://www.digicamsoft.com/bmp/bmp.html.
/// </summary>
public static class BmpReader
{
    /// <summary>
    /// ErrUnsupported means that the input BMP image uses a valid but unsupported feature.
    /// </summary>
    public static readonly Exception ErrUnsupported = new NotSupportedException("bmp: unsupported BMP image");

    private static readonly Exception errInvalidPaletteIndex = new FormatException("bmp: invalid palette index");

    private static ushort ReadUint16(byte[] b, int offset)
    {
        return (ushort)(b[offset] | (b[offset + 1] << 8));
    }

    private static uint ReadUint32(byte[] b, int offset)
    {
        return (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24));
    }

    /// <summary>
    /// ReadFull reads exactly buf.Length bytes from the stream into buf.
    /// </summary>
    private static void ReadFull(Stream r, byte[] buf)
    {
        int totalRead = 0;
        while (totalRead < buf.Length)
        {
            int n = r.Read(buf, totalRead, buf.Length - totalRead);
            if (n == 0)
                throw new EndOfStreamException(totalRead == 0 ? "unexpected EOF at start" : "unexpected EOF");
            totalRead += n;
        }
    }

    /// <summary>
    /// ReadFull reads exactly len bytes from the stream into a segment of buf.
    /// </summary>
    private static void ReadFull(Stream r, byte[] buf, int offset, int len)
    {
        int totalRead = 0;
        while (totalRead < len)
        {
            int n = r.Read(buf, offset + totalRead, len - totalRead);
            if (n == 0)
                throw new EndOfStreamException(totalRead == 0 ? "unexpected EOF at start" : "unexpected EOF");
            totalRead += n;
        }
    }

    /// <summary>
    /// decodePaletted reads a 1, 2, 4 or 8 bit-per-pixel BMP image from r.
    /// If topDown is false, the image rows will be read bottom-up.
    /// </summary>
    private static IImage DecodePaletted(Stream r, Config c, bool topDown, int bpp)
    {
        var palette = (Palette)c.ColorModel;
        var paletted = Paletted.NewPaletted(Rect.New(0, 0, c.Width, c.Height), palette);
        if (c.Width == 0 || c.Height == 0)
            return paletted;

        int y0, y1, yDelta;
        if (topDown)
        {
            y0 = 0; y1 = c.Height; yDelta = 1;
        }
        else
        {
            y0 = c.Height - 1; y1 = -1; yDelta = -1;
        }

        int pixelsPerByte = 8 / bpp;
        // Pad up to ensure each row is 4-bytes aligned.
        int bytesPerRow = ((c.Width + pixelsPerByte - 1) / pixelsPerByte + 3) & ~3;
        byte[] b = new byte[bytesPerRow];

        for (int y = y0; y != y1; y += yDelta)
        {
            int pixBase = y * paletted.Stride;
            ReadFull(r, b);
            int byteIndex = 0, bitIndex = 8;
            byte mask = (byte)((1 << bpp) - 1);
            for (int pixIndex = 0; pixIndex < c.Width; pixIndex++)
            {
                bitIndex -= bpp;
                byte paletteIndex = (byte)((b[byteIndex] >> bitIndex) & mask);
                if (paletteIndex >= palette.Count)
                    throw errInvalidPaletteIndex;
                paletted.Pix[pixBase + pixIndex] = paletteIndex;
                if (bitIndex == 0)
                {
                    byteIndex++;
                    bitIndex = 8;
                }
            }
        }
        return paletted;
    }

    /// <summary>
    /// decodeRGB reads a 24 bit-per-pixel BMP image from r.
    /// If topDown is false, the image rows will be read bottom-up.
    /// </summary>
    private static IImage DecodeRGB(Stream r, Config c, bool topDown)
    {
        var rgba = Image.RGBA.NewRGBA(Rect.New(0, 0, c.Width, c.Height));
        if (c.Width == 0 || c.Height == 0)
            return rgba;

        // There are 3 bytes per pixel, and each row is 4-byte aligned.
        byte[] b = new byte[(3 * c.Width + 3) & ~3];
        int y0, y1, yDelta;
        if (topDown)
        {
            y0 = 0; y1 = c.Height; yDelta = 1;
        }
        else
        {
            y0 = c.Height - 1; y1 = -1; yDelta = -1;
        }

        for (int y = y0; y != y1; y += yDelta)
        {
            ReadFull(r, b);
            int pBase = y * rgba.Stride;
            int pLen = c.Width * 4;
            for (int i = 0, j = 0; i < pLen; i += 4, j += 3)
            {
                // BMP images are stored in BGR order rather than RGB order.
                rgba.Pix[pBase + i + 0] = b[j + 2];
                rgba.Pix[pBase + i + 1] = b[j + 1];
                rgba.Pix[pBase + i + 2] = b[j + 0];
                rgba.Pix[pBase + i + 3] = 0xFF;
            }
        }
        return rgba;
    }

    /// <summary>
    /// decodeNRGBA reads a 32 bit-per-pixel BMP image from r.
    /// If topDown is false, the image rows will be read bottom-up.
    /// </summary>
    private static IImage DecodeNRGBA(Stream r, Config c, bool topDown, bool allowAlpha)
    {
        var nrgba = Image.NRGBA.NewNRGBA(Rect.New(0, 0, c.Width, c.Height));
        if (c.Width == 0 || c.Height == 0)
            return nrgba;

        int y0, y1, yDelta;
        if (topDown)
        {
            y0 = 0; y1 = c.Height; yDelta = 1;
        }
        else
        {
            y0 = c.Height - 1; y1 = -1; yDelta = -1;
        }

        for (int y = y0; y != y1; y += yDelta)
        {
            int pBase = y * nrgba.Stride;
            int pLen = c.Width * 4;
            ReadFull(r, nrgba.Pix, pBase, pLen);
            for (int i = pBase; i < pBase + pLen; i += 4)
            {
                // BMP images are stored in BGRA order rather than RGBA order.
                (nrgba.Pix[i + 0], nrgba.Pix[i + 2]) = (nrgba.Pix[i + 2], nrgba.Pix[i + 0]);
                if (!allowAlpha)
                    nrgba.Pix[i + 3] = 0xFF;
            }
        }
        return nrgba;
    }

    /// <summary>
    /// Decode reads a BMP image from r and returns it as an IImage.
    /// Limitation: The file must be 1, 2, 4, 8, 24 or 32 bits per pixel.
    /// </summary>
    public static IImage Decode(Stream r)
    {
        var (c, bpp, topDown, allowAlpha) = DecodeConfigInternal(r);
        switch (bpp)
        {
            case 1:
            case 2:
            case 4:
            case 8:
                return DecodePaletted(r, c, topDown, bpp);
            case 24:
                return DecodeRGB(r, c, topDown);
            case 32:
                return DecodeNRGBA(r, c, topDown, allowAlpha);
        }
        throw ErrUnsupported;
    }

    /// <summary>
    /// DecodeConfig returns the color model and dimensions of a BMP image without
    /// decoding the entire image.
    /// Limitation: The file must be 1, 2, 4, 8, 24 or 32 bits per pixel.
    /// </summary>
    public static Config DecodeConfig(Stream r)
    {
        var (c, _, _, _) = DecodeConfigInternal(r);
        return c;
    }

    private static (Config config, int bitsPerPixel, bool topDown, bool allowAlpha) DecodeConfigInternal(Stream r)
    {
        // We only support those BMP images with one of the following DIB headers:
        // - BITMAPINFOHEADER (40 bytes)
        // - BITMAPV4HEADER (108 bytes)
        // - BITMAPV5HEADER (124 bytes)
        const int fileHeaderLen = 14;
        const int infoHeaderLen = 40;
        const int v4InfoHeaderLen = 108;
        const int v5InfoHeaderLen = 124;

        byte[] b = new byte[1024];
        ReadFull(r, b, 0, fileHeaderLen + 4);

        if (b[0] != 'B' || b[1] != 'M')
            throw new FormatException("bmp: invalid format");

        uint offset = ReadUint32(b, 10);
        uint infoLen = ReadUint32(b, 14);
        if (infoLen != infoHeaderLen && infoLen != v4InfoHeaderLen && infoLen != v5InfoHeaderLen)
            throw ErrUnsupported;

        ReadFull(r, b, fileHeaderLen + 4, (int)(infoLen - 4));

        int width = (int)(int)ReadUint32(b, 18);
        int height = (int)(int)ReadUint32(b, 22);
        bool topDown = false;
        if (height < 0)
        {
            height = -height;
            topDown = true;
        }
        if (width < 0 || height < 0)
            throw ErrUnsupported;

        // We only support 1 plane and 8, 24 or 32 bits per pixel and no compression.
        ushort planes = ReadUint16(b, 26);
        ushort bpp = ReadUint16(b, 28);
        uint compression = ReadUint32(b, 30);

        // if compression is set to BI_BITFIELDS, but the bitmask is set to the default bitmask
        // that would be used if compression was set to 0, we can continue as if compression was 0
        if (compression == 3 && infoLen > infoHeaderLen &&
            ReadUint32(b, 54) == 0xff0000 && ReadUint32(b, 58) == 0xff00 &&
            ReadUint32(b, 62) == 0xff && ReadUint32(b, 66) == 0xff000000)
        {
            compression = 0;
        }

        if (planes != 1 || compression != 0)
            throw ErrUnsupported;

        switch (bpp)
        {
            case 1:
            case 2:
            case 4:
            case 8:
            {
                uint colorUsed = ReadUint32(b, 46);
                if (colorUsed == 0)
                    colorUsed = (uint)(1 << bpp);
                else if (colorUsed > (1 << bpp))
                    throw ErrUnsupported;

                if (offset != fileHeaderLen + infoLen + colorUsed * 4)
                    throw ErrUnsupported;

                ReadFull(r, b, 0, (int)(colorUsed * 4));
                var pcm = new Palette();
                for (int i = 0; i < (int)colorUsed; i++)
                {
                    // BMP images are stored in BGR order rather than RGB order.
                    // Every 4th byte is padding.
                    pcm.Add(new Color.RGBA(b[4 * i + 2], b[4 * i + 1], b[4 * i + 0], 0xFF));
                }
                return (new Config { ColorModel = pcm, Width = width, Height = height }, bpp, topDown, false);
            }
            case 24:
            {
                if (offset != fileHeaderLen + infoLen)
                    throw ErrUnsupported;
                return (new Config { ColorModel = ColorModels.RGBAModel, Width = width, Height = height }, 24, topDown, false);
            }
            case 32:
            {
                if (offset != fileHeaderLen + infoLen)
                    throw ErrUnsupported;
                // 32 bits per pixel is possibly RGBX (X is padding) or RGBA.
                // See the extensive comment in Go's reader.go about alpha handling.
                bool allowAlpha = infoLen > infoHeaderLen;
                return (new Config { ColorModel = ColorModels.RGBAModel, Width = width, Height = height }, 32, topDown, allowAlpha);
            }
        }
        throw ErrUnsupported;
    }

    /// <summary>
    /// Register the BMP format with the global image registry.
    /// Magic: "BM????\x00\x00\x00\x00" (first 2 bytes are "BM", next 4 are file size,
    /// last 4 are reserved zeros).
    /// </summary>
    public static void Register()
    {
        ImageRegistry.RegisterFormat("bmp", "BM????\x00\x00\x00\x00", Decode, DecodeConfig);
    }
}
