// Port of Go's image/gif/reader.go (GIF image decoder).
// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using GoImage.Color;
using GoImage.Image;
using System.Buffers.Binary;

namespace GoImage.Gif;

/// <summary>
/// GIF represents the possibly multiple images stored in a GIF file.
/// Port of Go's image/gif.GIF struct.
/// </summary>
public class GIF
{
    public List<Paletted> Image = new();
    public List<int> Delay = new();
    public List<byte> Disposal = new();
    public int LoopCount = -1; // -1 means show each frame only once
    public Config Config;
    public byte BackgroundIndex;

    public int Width => Config.Width;
    public int Height => Config.Height;
}

public static class GifReader
{
    public static void Register()
    {
        ImageRegistry.RegisterFormat("gif", "GIF87a", Decode, DecodeConfig);
        ImageRegistry.RegisterFormat("gif", "GIF89a", Decode, DecodeConfig);
    }

    public static IImage? Decode(Stream r)
    {
        var g = DecodeAll(r);
        return g?.Image[0];
    }

    public static Config DecodeConfig(Stream r)
    {
        var d = new GifDecoder(r);
        return d.DecodeConfig();
    }

    public static GIF DecodeAll(Stream r)
    {
        var d = new GifDecoder(r);
        return d.Decode();
    }
}

internal class GifDecoder
{
    private readonly Stream _r;
    private string _vers = "";
    private int _width, _height;
    private Palette? _globalPalette;
    private byte _backgroundIndex;
    private int _loopCount = -1;
    private int _delayTime;
    private bool _hasTransparentIndex;
    private byte _transparentIndex;
    private byte _disposalMethod;

    // Buffered blocks for LZW reading: mimics Go's blockReader
    private byte[] _tmp = new byte[1024]; // must be >= 768 for color table

    // Accumulated frames
    private readonly List<Paletted> _images = new();
    private readonly List<int> _delays = new();
    private readonly List<byte> _disposals = new();

    // Constants
    private const int FColorTable = 1 << 7;
    private const int FInterlace = 1 << 6;
    private const int FColorTableBitsMask = 7;
    private const int GcTransparentColorSet = 1 << 0;
    private const int GcDisposalMethodMask = 7 << 2;
    private const int SExtension = 0x21;
    private const int SImageDescriptor = 0x2C;
    private const int STrailer = 0x3B;
    private const int EGraphicControl = 0xF9;
    private const int EApplication = 0xFF;
    private const int EComment = 0xFE;
    private const int EText = 0x01;

    public GifDecoder(Stream r)
    {
        _r = r;
    }

    public Config DecodeConfig()
    {
        ReadHeader();
        ReadLSD();
        return new Config
        {
            Width = _width,
            Height = _height,
            ColorModel = _globalPalette!
        };
    }

    public GIF Decode()
    {
        ReadHeader();
        ReadLSD();

        _loopCount = -1;

        while (true)
        {
            int blockType = _r.ReadByte();
            if (blockType == -1) break;

            switch (blockType)
            {
                case SExtension:
                    ReadExtension();
                    break;
                case SImageDescriptor:
                    ReadImageDescriptor(true);
                    break;
                case STrailer:
                    if (_images.Count == 0)
                        throw new FormatException("gif: missing image data");
                    return CreateGIF();
                default:
                    throw new FormatException($"gif: unknown block type: 0x{blockType:x2}");
            }
        }

        if (_images.Count == 0)
            throw new FormatException("gif: missing image data");
        return CreateGIF();
    }

    private GIF CreateGIF()
    {
        return new GIF
        {
            Image = _images,
            Delay = _delays,
            Disposal = _disposals,
            LoopCount = _loopCount,
            Config = new Config
            {
                ColorModel = _globalPalette ?? new Palette(),
                Width = _width,
                Height = _height
            },
            BackgroundIndex = _backgroundIndex
        };
    }

    private void ReadHeader()
    {
        byte[] h = new byte[6];
        ReadExact(h);
        _vers = System.Text.Encoding.ASCII.GetString(h);
        if (_vers != "GIF87a" && _vers != "GIF89a")
            throw new FormatException("gif: invalid format");
    }

    private void ReadLSD()
    {
        byte[] b = new byte[7];
        ReadExact(b);
        _width = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(0, 2));
        _height = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(2, 2));
        byte fields = b[4];
        _backgroundIndex = b[5];
        // b[6] is pixel aspect ratio, ignored

        if ((fields & FColorTable) != 0)
        {
            int paletteSize = 1 << ((fields & FColorTableBitsMask) + 1);
            _globalPalette = ReadPalette(paletteSize);
        }
    }

    private Palette ReadPalette(int size)
    {
        byte[] b = new byte[size * 3];
        ReadExact(b);
        var colors = new IColor[size];
        for (int i = 0; i < size; i++)
        {
            colors[i] = new Color.RGBA(b[i * 3], b[i * 3 + 1], b[i * 3 + 2], 0xff);
        }
        return new Palette(colors);
    }

    private void ReadExtension()
    {
        int extType = _r.ReadByte();
        if (extType < 0) throw new EndOfStreamException("gif: reading extension");

        switch (extType)
        {
            case EText:
                // Plain Text Extension: 13 bytes of header + data blocks
                int textSize = ReadBlock();
                if (textSize > 0) { /* skip header, already in _tmp */ }
                break;

            case EGraphicControl:
                ReadGraphicControl();
                return;

            case EComment:
                // Nothing to do but skip the data
                break;

            case EApplication:
                ReadApplicationExtension();
                return;

            default:
                // Skip unknown extension data
                break;
        }

        // Skip remaining sub-blocks
        while (true)
        {
            int n = ReadBlock();
            if (n <= 0) break;
        }
    }

    private void ReadGraphicControl()
    {
        byte[] b = new byte[6];
        ReadExact(b);

        if (b[0] != 4)
            throw new FormatException($"gif: invalid graphic control extension block size: {b[0]}");

        byte flags = b[1];
        _disposalMethod = (byte)((flags & GcDisposalMethodMask) >> 2);
        _delayTime = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(2, 2));
        if ((flags & GcTransparentColorSet) != 0)
        {
            _transparentIndex = b[4];
            _hasTransparentIndex = true;
        }
        else
        {
            _hasTransparentIndex = false;
        }
        // b[5] is block terminator (should be 0)
    }

    private void ReadApplicationExtension()
    {
        int size = _r.ReadByte();
        if (size < 0) throw new EndOfStreamException("gif: reading application extension");

        // Read the application identifier (must be >= 11 for "NETSCAPE2.0" or "ANIMEXTS")
        if (size > 0)
        {
            if (_tmp.Length < size)
                Array.Resize(ref _tmp, size);
            ReadExact(_tmp, size);
        }

        // Check for NETSCAPE2.0 loop count extension
        if (size >= 11 &&
            _tmp[0] == 'N' && _tmp[1] == 'E' && _tmp[2] == 'T' && _tmp[3] == 'S' &&
            _tmp[4] == 'C' && _tmp[5] == 'A' && _tmp[6] == 'P' && _tmp[7] == 'E' &&
            _tmp[8] == '2' && _tmp[9] == '.' && _tmp[10] == '0')
        {
            // Read the data sub-block
            int n = ReadBlock();
            if (n >= 3 && _tmp[0] == 1)
            {
                _loopCount = _tmp[1] | (_tmp[2] << 8);
            }
        }

        // Skip remaining sub-blocks
        while (true)
        {
            int n = ReadBlock();
            if (n <= 0) break;
        }
    }

    private void ReadImageDescriptor(bool keepAllFrames)
    {
        byte[] b = new byte[9];
        ReadExact(b);

        int left = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(0, 2));
        int top = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(2, 2));
        int imgWidth = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(4, 2));
        int imgHeight = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(6, 2));
        byte imageFields = b[8];

        // Validate frame bounds
        if (left + imgWidth > _width || top + imgHeight > _height)
            throw new FormatException("gif: frame bounds larger than image bounds");

        // Determine palette
        Palette palette;
        bool useLocalPalette = (imageFields & FColorTable) != 0;
        if (useLocalPalette)
        {
            int palSize = 1 << ((imageFields & FColorTableBitsMask) + 1);
            palette = ReadPalette(palSize);
        }
        else
        {
            if (_globalPalette == null)
                throw new FormatException("gif: no color table");
            palette = _globalPalette;
        }

        // Handle transparency
        if (_hasTransparentIndex)
        {
            if (!useLocalPalette)
            {
                // Clone global palette to modify
                var clonedColors = new IColor[palette.Count];
                palette.CopyTo(clonedColors, 0);
                palette = new Palette(clonedColors);
            }
            if (_transparentIndex < palette.Count)
            {
                palette[_transparentIndex] = new Color.RGBA(0, 0, 0, 0);
            }
            else
            {
                // Enlarge palette with transparent colors
                var enlarged = new IColor[_transparentIndex + 1];
                palette.CopyTo(enlarged, 0);
                for (int i = palette.Count; i < enlarged.Length; i++)
                    enlarged[i] = new Color.RGBA(0, 0, 0, 0);
                palette = new Palette(enlarged);
            }
        }

        var rect = Rect.New(left, top, left + imgWidth, imgHeight);
        var m = Paletted.NewPaletted(rect, palette);

        // Read LZW minimum code size
        int litWidth = _r.ReadByte();
        if (litWidth < 2 || litWidth > 8)
            throw new FormatException($"gif: pixel size out of range: {litWidth}");

        // Decode LZW-compressed image data
        DecodeLZW(m, litWidth);

        // Verify palette bounds
        if (palette.Count < 256)
        {
            for (int i = 0; i < m.Pix.Length; i++)
            {
                if (m.Pix[i] >= palette.Count)
                    throw new FormatException("gif: invalid pixel value");
            }
        }

        // Undo interlacing if necessary
        if ((imageFields & FInterlace) != 0)
            Uninterlace(m);

        if (keepAllFrames || _images.Count == 0)
        {
            _images.Add(m);
            _delays.Add(_delayTime);
            _disposals.Add(_disposalMethod);
        }

        // Reset GCE fields for next frame
        _delayTime = 0;
        _hasTransparentIndex = false;
    }

    private void DecodeLZW(Paletted p, int litWidth)
    {
        // Read data sub-blocks into a single stream
        using var ms = new MemoryStream();
        while (true)
        {
            int blockSize = _r.ReadByte();
            if (blockSize <= 0) break;
            byte[] block = new byte[blockSize];
            _r.ReadExactly(block);
            ms.Write(block);
        }
        ms.Position = 0;

        // Decompress LZW data
        var lzw = new LzwReader(ms, litWidth);
        int totalPixels = p.Pix.Length;
        int offset = 0;

        while (offset < totalPixels)
        {
            int read = lzw.Read(p.Pix, offset, totalPixels - offset);
            if (read <= 0) break;
            offset += read;
        }
    }

    /// <summary>
    /// ReadExact reads exactly len bytes from the underlying stream.
    /// </summary>
    private void ReadExact(byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int n = _r.Read(buffer, offset, buffer.Length - offset);
            if (n <= 0) throw new EndOfStreamException("gif: unexpected EOF");
            offset += n;
        }
    }

    private void ReadExact(byte[] buffer, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int n = _r.Read(buffer, offset, count - offset);
            if (n <= 0) throw new EndOfStreamException("gif: unexpected EOF");
            offset += n;
        }
    }

    /// <summary>
    /// ReadBlock reads one data sub-block (the block byte followed by the block data).
    /// Returns the number of bytes read, or 0 if the block terminator was reached.
    /// Data is stored in _tmp.
    /// </summary>
    private int ReadBlock()
    {
        int n = _r.ReadByte();
        if (n <= 0) return 0;
        if (_tmp.Length < n)
            Array.Resize(ref _tmp, n);
        ReadExact(_tmp, n);
        return n;
    }

    /// <summary>
    /// Interlacing scan definitions for GIF.
    /// </summary>
    private static readonly (int skip, int start)[] Interlacing =
    {
        (8, 0),  // Group 1: every 8th row, starting with row 0
        (8, 4),  // Group 2: every 8th row, starting with row 4
        (4, 2),  // Group 3: every 4th row, starting with row 2
        (2, 1),  // Group 4: every 2nd row, starting with row 1
    };

    /// <summary>
    /// Uninterlace rearranges the pixels in m to account for interlaced input.
    /// Port of Go's image/gif.uninterlace.
    /// </summary>
    private static void Uninterlace(Paletted m)
    {
        int dx = m.Rect.Dx();
        int dy = m.Rect.Dy();
        byte[] nPix = new byte[dx * dy];
        int offset = 0; // steps through the input by sequential scan lines

        foreach (var (skip, start) in Interlacing)
        {
            int nOffset = start * dx; // steps through the output as defined by pass
            for (int y = start; y < dy; y += skip)
            {
                Array.Copy(m.Pix, offset, nPix, nOffset, dx);
                offset += dx;
                nOffset += dx * skip;
            }
        }

        m.Pix = nPix;
    }
}
