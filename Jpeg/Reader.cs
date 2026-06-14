// Port of Go's image/jpeg/reader.go (JPEG decoder core).

using GoImage.Color;
using GoImage.Image;

namespace GoImage.Jpeg;

/// <summary>
/// FormatError reports that the input is not a valid JPEG.
/// </summary>
public class FormatErrorException : Exception
{
    public FormatErrorException(string msg) : base($"invalid JPEG format: {msg}") { }
}

/// <summary>
/// UnsupportedError reports that the input uses a valid but unimplemented JPEG feature.
/// </summary>
public class UnsupportedErrorException : Exception
{
    public UnsupportedErrorException(string msg) : base($"unsupported JPEG feature: {msg}") { }
}

public class MissingFF00Exception : FormatErrorException
{
    public MissingFF00Exception() : base("missing 0xff00 sequence") { }
}

// Component specification (section B.2.2).
internal struct Component
{
    public int h;       // Horizontal sampling factor.
    public int v;       // Vertical sampling factor.
    public byte c;      // Component identifier.
    public byte tq;     // Quantization table destination selector.
    public int expandH; // Horizontal expansion factor for non-standard subsampling.
    public int expandV; // Vertical expansion factor for non-standard subsampling.
}

internal static class Const
{
    public const int dcTable = 0;
    public const int acTable = 1;
    public const int maxTc = 1;
    public const int maxTh = 3;
    public const int maxTq = 3;
    public const int maxComponents = 4;

    public const byte sof0Marker = 0xc0;
    public const byte sof1Marker = 0xc1;
    public const byte sof2Marker = 0xc2;
    public const byte dhtMarker  = 0xc4;
    public const byte rst0Marker = 0xd0;
    public const byte rst7Marker = 0xd7;
    public const byte soiMarker  = 0xd8;
    public const byte eoiMarker  = 0xd9;
    public const byte sosMarker  = 0xda;
    public const byte dqtMarker  = 0xdb;
    public const byte driMarker  = 0xdd;
    public const byte comMarker  = 0xfe;
    public const byte app0Marker  = 0xe0;
    public const byte app14Marker = 0xee;
    public const byte app15Marker = 0xef;

    public const byte adobeTransformUnknown = 0;
    public const byte adobeTransformYCbCr   = 1;
    public const byte adobeTransformYCbCrK  = 2;
}

// unzig maps from the zig-zag ordering to the natural ordering.
internal static class Unzig
{
    public static readonly int[] Table = {
        0, 1, 8, 16, 9, 2, 3, 10,
        17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    };
}

internal struct Bits
{
    public uint a; // accumulator
    public uint m; // mask
    public int n;  // number of unread bits in a
}

/// <summary>
/// JPEG Decoder. Implements reading JPEG from a Stream.
/// </summary>
public partial class Decoder
{
    internal Stream _r;
    internal Bits _bits;
    // Byte buffer similar to Go's bufio
    internal byte[] _bytesBuf = new byte[4096];
    internal int _bytesI, _bytesJ;
    internal int _nUnreadable;
    internal int _width, _height;

    internal GoImage.Image.Gray? _img1;
    internal GoImage.Image.YCbCr? _img3;
    internal byte[]? _blackPix;
    internal int _blackStride;

    internal bool _flex;
    internal int _maxH, _maxV;

    internal int _ri; // Restart Interval
    internal int _nComp;

    internal bool _baseline;
    internal bool _progressive;

    internal bool _jfif;
    internal bool _adobeTransformValid;
    internal byte _adobeTransform;
    internal ushort _eobRun;

    internal Component[] _comp = new Component[Const.maxComponents];
    internal Block[][]? _progCoeffs = new Block[Const.maxComponents][];
    internal Huffman[,] _huff = new Huffman[Const.maxTc + 1, Const.maxTh + 1];
    internal Block[] _quant = new Block[Const.maxTq + 1];
    internal byte[] _tmp = new byte[2 * Block.BlockSize];

    public Decoder()
    {
        _r = Stream.Null;
        // Initialize Block.Data arrays (C# default struct init doesn't call constructors)
        for (int i = 0; i < _quant.Length; i++)
            _quant[i] = new Block();
        for (int i = 0; i < _huff.GetLength(0); i++)
            for (int j = 0; j < _huff.GetLength(1); j++)
                _huff[i, j] = Huffman.New();
    }

    // ---- Byte reading ----

    internal void Fill()
    {
        if (_bytesI != _bytesJ)
            throw new InvalidOperationException("jpeg: fill called when unread bytes exist");
        if (_bytesJ > 2)
        {
            _bytesBuf[0] = _bytesBuf[_bytesJ - 2];
            _bytesBuf[1] = _bytesBuf[_bytesJ - 1];
            _bytesI = 2;
            _bytesJ = 2;
        }
        int n = _r.Read(_bytesBuf, _bytesJ, _bytesBuf.Length - _bytesJ);
        _bytesJ += n;
        if (n > 0) return;
        throw new FormatErrorException("unexpected EOF");
    }

    internal void UnreadByteStuffedByte()
    {
        _bytesI -= _nUnreadable;
        _nUnreadable = 0;
        if (_bits.n >= 8)
        {
            _bits.a >>= 8;
            _bits.n -= 8;
            _bits.m >>= 8;
        }
    }

    internal byte ReadByte()
    {
        while (_bytesI == _bytesJ) Fill();
        byte x = _bytesBuf[_bytesI];
        _bytesI++;
        _nUnreadable = 0;
        return x;
    }

    internal byte ReadByteStuffedByte()
    {
        if (_bytesI + 2 <= _bytesJ)
        {
            byte x = _bytesBuf[_bytesI];
            _bytesI++;
            _nUnreadable = 1;
            if (x != 0xff) return x;
            if (_bytesBuf[_bytesI] != 0x00)
                throw new MissingFF00Exception();
            _bytesI++;
            _nUnreadable = 2;
            return 0xff;
        }

        _nUnreadable = 0;
        byte xx = ReadByte();
        _nUnreadable = 1;
        if (xx != 0xff) return xx;
        xx = ReadByte();
        _nUnreadable = 2;
        if (xx != 0x00)
            throw new MissingFF00Exception();
        return 0xff;
    }

    internal void ReadFull(byte[] p, int offset, int length)
    {
        if (_nUnreadable != 0)
        {
            if (_bits.n >= 8) UnreadByteStuffedByte();
            _nUnreadable = 0;
        }

        while (length > 0)
        {
            int n = Math.Min(length, _bytesJ - _bytesI);
            if (n > 0)
            {
                Array.Copy(_bytesBuf, _bytesI, p, offset, n);
                _bytesI += n;
                offset += n;
                length -= n;
            }
            if (length > 0) Fill();
        }
    }

    internal void Ignore(int n)
    {
        if (_nUnreadable != 0)
        {
            if (_bits.n >= 8) UnreadByteStuffedByte();
            _nUnreadable = 0;
        }

        while (n > 0)
        {
            int m = _bytesJ - _bytesI;
            if (m > n) m = n;
            _bytesI += m;
            n -= m;
            if (n == 0) break;
            Fill();
        }
    }

    // ---- Marker processing ----

    internal void ProcessSOF(int n)
    {
        if (_nComp != 0) throw new FormatErrorException("multiple SOF markers");
        switch (n)
        {
            case 6 + 3 * 1: _nComp = 1; break;
            case 6 + 3 * 3: _nComp = 3; break;
            case 6 + 3 * 4: _nComp = 4; break;
            default: throw new UnsupportedErrorException("number of components");
        }
        ReadFull(_tmp, 0, n);
        if (_tmp[0] != 8) throw new UnsupportedErrorException("precision");
        _height = (_tmp[1] << 8) + _tmp[2];
        _width = (_tmp[3] << 8) + _tmp[4];
        if (_tmp[5] != _nComp) throw new FormatErrorException("SOF has wrong length");

        for (int i = 0; i < _nComp; i++)
        {
            _comp[i].c = _tmp[6 + 3 * i];
            for (int j = 0; j < i; j++)
            {
                if (_comp[i].c == _comp[j].c)
                    throw new FormatErrorException("repeated component identifier");
            }
            _comp[i].tq = _tmp[8 + 3 * i];
            if (_comp[i].tq > Const.maxTq) throw new FormatErrorException("bad Tq value");

            int hv = _tmp[7 + 3 * i];
            int h = hv >> 4, v = hv & 0x0f;
            if (h < 1 || 4 < h || v < 1 || 4 < v)
                throw new FormatErrorException("luma/chroma subsampling ratio");
            if (h == 3 || v == 3)
                throw new UnsupportedErrorException("luma/chroma subsampling ratio");

            switch (_nComp)
            {
                case 1: h = 1; v = 1; break;
                case 3: break; // Flex mode handles non-standard
                case 4:
                    switch (i)
                    {
                        case 0:
                            if (hv != 0x11 && hv != 0x22)
                                throw new UnsupportedErrorException("luma/chroma subsampling ratio");
                            break;
                        case 1:
                        case 2:
                            if (hv != 0x11)
                                throw new UnsupportedErrorException("luma/chroma subsampling ratio");
                            break;
                        case 3:
                            if (_comp[0].h != h || _comp[0].v != v)
                                throw new UnsupportedErrorException("luma/chroma subsampling ratio");
                            break;
                    }
                    break;
            }
            _maxH = Math.Max(_maxH, h);
            _maxV = Math.Max(_maxV, v);
            _comp[i].h = h;
            _comp[i].v = v;
        }

        // Validate for 3-component images
        if (_nComp == 3)
        {
            for (int i = 0; i < 3; i++)
            {
                if (_maxH % _comp[i].h != 0 || _maxV % _comp[i].v != 0)
                    throw new UnsupportedErrorException("luma/chroma subsampling ratio");
            }
        }

        // Compute expansion factors
        for (int i = 0; i < _nComp; i++)
        {
            _comp[i].expandH = _maxH / _comp[i].h;
            _comp[i].expandV = _maxV / _comp[i].v;
        }
    }

    internal void ProcessDQT(int n)
    {
        while (n > 0)
        {
            n--;
            byte x = ReadByte();
            int tq = x & 0x0f;
            if (tq > Const.maxTq) throw new FormatErrorException("bad Tq value");
            switch (x >> 4)
            {
                case 0:
                    if (n < Block.BlockSize) return;
                    n -= Block.BlockSize;
                    ReadFull(_tmp, 0, Block.BlockSize);
                    for (int i = 0; i < Block.BlockSize; i++)
                        _quant[tq][i] = _tmp[i];
                    break;
                case 1:
                    if (n < 2 * Block.BlockSize) return;
                    n -= 2 * Block.BlockSize;
                    ReadFull(_tmp, 0, 2 * Block.BlockSize);
                    for (int i = 0; i < Block.BlockSize; i++)
                        _quant[tq][i] = (_tmp[2 * i] << 8) + _tmp[2 * i + 1];
                    break;
                default:
                    throw new FormatErrorException("bad Pq value");
            }
        }
        if (n != 0) throw new FormatErrorException("DQT has wrong length");
    }

    internal void ProcessDRI(int n)
    {
        if (n != 2) throw new FormatErrorException("DRI has wrong length");
        ReadFull(_tmp, 0, 2);
        _ri = (_tmp[0] << 8) + _tmp[1];
    }

    internal void ProcessApp0Marker(int n)
    {
        if (n < 5) { Ignore(n); return; }
        ReadFull(_tmp, 0, 5);
        n -= 5;
        _jfif = _tmp[0] == 'J' && _tmp[1] == 'F' && _tmp[2] == 'I' && _tmp[3] == 'F' && _tmp[4] == '\x00';
        if (n > 0) Ignore(n);
    }

    internal void ProcessApp14Marker(int n)
    {
        if (n < 12) { Ignore(n); return; }
        ReadFull(_tmp, 0, 12);
        n -= 12;
        if (_tmp[0] == 'A' && _tmp[1] == 'd' && _tmp[2] == 'o' && _tmp[3] == 'b' && _tmp[4] == 'e')
        {
            _adobeTransformValid = true;
            _adobeTransform = _tmp[11];
        }
        if (n > 0) Ignore(n);
    }

    // ---- Main decode logic ----

    internal IImage? DecodeInternal(Stream r, bool configOnly)
    {
        _r = r;

        // Check SOI marker
        ReadFull(_tmp, 0, 2);
        if (_tmp[0] != 0xff || _tmp[1] != Const.soiMarker)
            throw new FormatErrorException("missing SOI marker");

        // Process segments until EOI
        while (true)
        {
            try
            {
                ReadFull(_tmp, 0, 2);
            }
            catch (EndOfStreamException)
            {
                // Go handles EOF gracefully: if we already decoded an image, return it
                if (_img1 != null || _img3 != null) break;
                throw;
            }
            catch (FormatErrorException) when (_img1 != null || _img3 != null)
            {
                // After SOS data, EOF during marker scan is acceptable if image exists
                break;
            }
            while (_tmp[0] != 0xff)
            {
                _tmp[0] = _tmp[1];
                _tmp[1] = ReadByte();
            }
            byte marker = _tmp[1];
            if (marker == 0) continue;
            while (marker == 0xff)
            {
                marker = ReadByte();
            }
            if (marker == Const.eoiMarker) break;
            if (Const.rst0Marker <= marker && marker <= Const.rst7Marker) continue;

            ReadFull(_tmp, 0, 2);
            int nn = (_tmp[0] << 8) + _tmp[1] - 2;
            if (nn < 0) throw new FormatErrorException("short segment length");

            switch (marker)
            {
                case Const.sof0Marker:
                case Const.sof1Marker:
                case Const.sof2Marker:
                    _baseline = marker == Const.sof0Marker;
                    _progressive = marker == Const.sof2Marker;
                    ProcessSOF(nn);
                    if (configOnly && _jfif) return null;
                    break;
                case Const.dhtMarker:
                    if (configOnly) Ignore(nn); else ProcessDHT(nn);
                    break;
                case Const.dqtMarker:
                    if (configOnly) Ignore(nn); else ProcessDQT(nn);
                    break;
                case Const.sosMarker:
                    if (configOnly) return null;
                    ProcessSOS(nn);
                    break;
                case Const.driMarker:
                    if (configOnly) Ignore(nn); else ProcessDRI(nn);
                    break;
                case Const.app0Marker:
                    ProcessApp0Marker(nn);
                    break;
                case Const.app14Marker:
                    ProcessApp14Marker(nn);
                    break;
                default:
                    if ((Const.app0Marker <= marker && marker <= Const.app15Marker) || marker == Const.comMarker)
                        Ignore(nn);
                    else if (marker < 0xc0)
                        throw new FormatErrorException("unknown marker");
                    else
                        throw new UnsupportedErrorException("unknown marker");
                    break;
            }
        }

        if (_progressive)
            ReconstructProgressiveImage();

        if (_img1 != null) return _img1;
        if (_img3 != null)
        {
            if (_blackPix != null) return ApplyBlack();
            if (IsRGB()) return ConvertToRGB();
            return _img3;
        }
        throw new FormatErrorException("missing SOS marker");
    }

    internal bool IsRGB()
    {
        if (_jfif) return false;
        if (_adobeTransformValid && _adobeTransform == Const.adobeTransformUnknown) return true;
        return _comp[0].c == 'R' && _comp[1].c == 'G' && _comp[2].c == 'B';
    }

    internal IImage ConvertToRGB()
    {
        int h0 = _comp[0].h, h1 = _comp[1].h, h2 = _comp[2].h;
        int v0 = _comp[0].v, v1 = _comp[1].v, v2 = _comp[2].v;
        if ((h1 != h2) || (h0 % h1 != 0) || (v1 != v2) || (v0 % v1 != 0))
            throw new UnsupportedErrorException("luma/chroma subsampling ratio");

        int cScale = h0 / h1;
        var bounds = _img3!.Bounds();
        var img = GoImage.Image.RGBA.NewRGBA(bounds);
        for (int y = bounds.Min.Y; y < bounds.Max.Y; y++)
        {
            int po = img.PixOffset(bounds.Min.X, y);
            int yo = _img3.YOffset(bounds.Min.X, y);
            int co = _img3.COffset(bounds.Min.X, y);
            for (int i = 0, iMax = bounds.Max.X - bounds.Min.X; i < iMax; i++)
            {
                img.Pix[po + 4 * i + 0] = _img3.GetY(yo + i);
                img.Pix[po + 4 * i + 1] = _img3.GetCb(co + i / cScale);
                img.Pix[po + 4 * i + 2] = _img3.GetCr(co + i / cScale);
                img.Pix[po + 4 * i + 3] = 255;
            }
        }
        return img;
    }

    internal IImage ApplyBlack()
    {
        if (!_adobeTransformValid)
            throw new UnsupportedErrorException("unknown color model: 4-component JPEG doesn't have Adobe APP14 metadata");

        if (_adobeTransform != Const.adobeTransformUnknown)
        {
            var bounds = _img3!.Bounds();
            var img = GoImage.Image.RGBA.NewRGBA(bounds);
            Internal.ImageUtil.DrawYCbCr(img, bounds, _img3, bounds.Min);
            for (int iBase = 0, y = bounds.Min.Y; y < bounds.Max.Y; iBase += img.Stride, y++)
            {
                for (int i = iBase + 3, x = bounds.Min.X; x < bounds.Max.X; i += 4, x++)
                {
                    img.Pix[i] = (byte)(255 - _blackPix![(y - bounds.Min.Y) * _blackStride + (x - bounds.Min.X)]);
                }
            }
            return new GoImage.Image.CMYK(img.Pix, img.Stride, img.Rect);
        }

        // Unknown transform: direct CMYK
        var bnds = _img3!.Bounds();
        var cmykImg = GoImage.Image.CMYK.NewCMYK(bnds);
        // Use Buffer + offsets to match Go's slice semantics
        var translations = new (byte[] src, int srcOff, int stride)[]
        {
            (_img3.Buffer, _img3.YOff, _img3.YStride),
            (_img3.Buffer, _img3.CbOff, _img3.CStride),
            (_img3.Buffer, _img3.CrOff, _img3.CStride),
            (_blackPix!, 0, _blackStride),
        };
        for (int t = 0; t < 4; t++)
        {
            bool subsample = _comp[t].h != _comp[0].h || _comp[t].v != _comp[0].v;
            for (int iBase = 0, y = bnds.Min.Y; y < bnds.Max.Y; iBase += cmykImg.Stride, y++)
            {
                int sy = y - bnds.Min.Y;
                if (subsample) sy /= 2;
                for (int i = iBase + t, x = bnds.Min.X; x < bnds.Max.X; i += 4, x++)
                {
                    int sx = x - bnds.Min.X;
                    if (subsample) sx /= 2;
                    cmykImg.Pix[i] = (byte)(255 - translations[t].src[translations[t].srcOff + sy * translations[t].stride + sx]);
                }
            }
        }
        return cmykImg;
    }

    // ---- Public API ----

    /// <summary>
    /// Decode reads a JPEG image from r and returns it as an IImage.
    /// </summary>
    public static IImage? Decode(Stream r)
    {
        var d = new Decoder();
        return d.DecodeInternal(r, false);
    }

    /// <summary>
    /// DecodeConfig returns the color model and dimensions of a JPEG image.
    /// </summary>
    public static Config DecodeConfig(Stream r)
    {
        var d = new Decoder();
        d.DecodeInternal(r, true);
        IModel cm;
        switch (d._nComp)
        {
            case 1:
                cm = ColorModels.GrayModel;
                break;
            case 3:
                cm = d.IsRGB() ? ColorModels.RGBAModel : YCbCrModelStatic.YCbCrModel;
                break;
            case 4:
                cm = CMYKModelStatic.CMYKModel;
                break;
            default:
                throw new FormatErrorException("missing SOF marker");
        }
        return new Config { ColorModel = cm, Width = d._width, Height = d._height };
    }

    static Decoder()
    {
        ImageRegistry.RegisterFormat("jpeg", "\xff\xd8",
            s => Decode(s),
            s => DecodeConfig(s));
    }
}
