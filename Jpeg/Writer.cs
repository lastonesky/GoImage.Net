// Port of Go's image/jpeg/writer.go.

using GoImage.Color;
using GoImage.Image;

namespace GoImage.Jpeg;

internal enum QuantIndex
{
    Luminance = 0,
    Chrominance = 1,
}

internal enum HuffIndex
{
    LuminanceDC = 0,
    LuminanceAC = 1,
    ChrominanceDC = 2,
    ChrominanceAC = 3,
}

/// <summary>
/// huffmanSpec specifies a Huffman encoding.
/// </summary>
internal struct HuffmanSpec
{
    public byte[] Count; // count[i] = number of codes of length i+1 bits
    public byte[] Value; // value[i] = decoded value of the i'th codeword
}

/// <summary>
/// JPEG Encoder.
/// </summary>
public class Encoder
{
    static int Div(int a, int b)
    {
        if (a >= 0) return (a + (b >> 1)) / b;
        return -((-a + (b >> 1)) / b);
    }

    static readonly byte[] BitCount = {
        0, 1, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4,
        5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
        6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
        6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
        7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
        7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
        7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
        7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
    };

    // Unscaled quantization tables in zig-zag order.
    static readonly byte[][] UnscaledQuant = new byte[][]
    {
        // Luminance
        new byte[] {
            16, 11, 12, 14, 12, 10, 16, 14,
            13, 14, 18, 17, 16, 19, 24, 40,
            26, 24, 22, 22, 24, 49, 35, 37,
            29, 40, 58, 51, 61, 60, 57, 51,
            56, 55, 64, 72, 92, 78, 64, 68,
            87, 69, 55, 56, 80, 109, 81, 87,
            95, 98, 103, 104, 103, 62, 77, 113,
            121, 112, 100, 120, 92, 101, 103, 99,
        },
        // Chrominance
        new byte[] {
            17, 18, 18, 24, 21, 24, 47, 26,
            26, 47, 99, 66, 56, 66, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
        },
    };

    static readonly HuffmanSpec[] TheHuffmanSpec = new HuffmanSpec[]
    {
        // Luminance DC
        new HuffmanSpec {
            Count = new byte[] { 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0 },
            Value = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
        },
        // Luminance AC
        new HuffmanSpec {
            Count = new byte[] { 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 125 },
            Value = new byte[] {
                0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12,
                0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
                0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xa1, 0x08,
                0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0,
                0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16,
                0x17, 0x18, 0x19, 0x1a, 0x25, 0x26, 0x27, 0x28,
                0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
                0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
                0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
                0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
                0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
                0x7a, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
                0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
                0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
                0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6,
                0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5,
                0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4,
                0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xe1, 0xe2,
                0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea,
                0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
                0xf9, 0xfa,
            },
        },
        // Chrominance DC
        new HuffmanSpec {
            Count = new byte[] { 0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 },
            Value = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
        },
        // Chrominance AC
        new HuffmanSpec {
            Count = new byte[] { 0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 119 },
            Value = new byte[] {
                0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21,
                0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
                0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91,
                0xa1, 0xb1, 0xc1, 0x09, 0x23, 0x33, 0x52, 0xf0,
                0x15, 0x62, 0x72, 0xd1, 0x0a, 0x16, 0x24, 0x34,
                0xe1, 0x25, 0xf1, 0x17, 0x18, 0x19, 0x1a, 0x26,
                0x27, 0x28, 0x29, 0x2a, 0x35, 0x36, 0x37, 0x38,
                0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
                0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
                0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
                0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
                0x79, 0x7a, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
                0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96,
                0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5,
                0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4,
                0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3,
                0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2,
                0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda,
                0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9,
                0xea, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
                0xf9, 0xfa,
            },
        },
    };

    // Compiled Huffman LUT: high 8 bits = codeword size, low 24 bits = codeword.
    static uint[][] TheHuffmanLUT = new uint[4][];

    static Encoder()
    {
        for (int i = 0; i < 4; i++)
        {
            TheHuffmanLUT[i] = CompileHuffmanLUT(TheHuffmanSpec[i]);
        }
    }

    static uint[] CompileHuffmanLUT(HuffmanSpec s)
    {
        int maxValue = 0;
        foreach (var v in s.Value)
            if (v > maxValue) maxValue = v;
        var h = new uint[maxValue + 1];
        uint code = 0;
        int k = 0;
        for (int i = 0; i < s.Count.Length; i++)
        {
            uint nBits = (uint)(i + 1) << 24;
            for (int j = 0; j < s.Count[i]; j++)
            {
                h[s.Value[k]] = nBits | code;
                code++;
                k++;
            }
            code <<= 1;
        }
        return h;
    }

    // Encoder state
    Stream _w = Stream.Null;
    Exception? _err;
    byte[] _buf = new byte[16];
    uint _encBits, _nBits;
    byte[][] _encQuant = new byte[2][]; // [nQuantIndex][blockSize]

    static readonly byte[] SosHeaderY = {
        0xff, 0xda, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3f, 0x00,
    };

    static readonly byte[] SosHeaderYCbCr = {
        0xff, 0xda, 0x00, 0x0c, 0x03, 0x01, 0x00, 0x02,
        0x11, 0x03, 0x11, 0x00, 0x3f, 0x00,
    };

    public const int DefaultQuality = 75;

    void Write(byte[] p)
    {
        if (_err != null) return;
        try { _w.Write(p, 0, p.Length); }
        catch (Exception ex) { _err = ex; }
    }

    void Write(byte[] p, int offset, int count)
    {
        if (_err != null) return;
        try { _w.Write(p, offset, count); }
        catch (Exception ex) { _err = ex; }
    }

    void WriteByte(byte b)
    {
        if (_err != null) return;
        try { _w.WriteByte(b); }
        catch (Exception ex) { _err = ex; }
    }

    void Flush()
    {
        if (_err != null) return;
        try { _w.Flush(); }
        catch (Exception ex) { _err = ex; }
    }

    void Emit(uint bits, uint nBits)
    {
        nBits += _nBits;
        bits <<= (int)(32 - nBits);
        bits |= _encBits;
        while (nBits >= 8)
        {
            byte b = (byte)(bits >> 24);
            WriteByte(b);
            if (b == 0xff) WriteByte(0x00);
            bits <<= 8;
            nBits -= 8;
        }
        _encBits = bits;
        _nBits = nBits;
    }

    void EmitHuff(HuffIndex h, int value)
    {
        uint x = TheHuffmanLUT[(int)h][value];
        Emit(x & ((1 << 24) - 1), x >> 24);
    }

    void EmitHuffRLE(HuffIndex h, int runLength, int value)
    {
        int a = value, b = value;
        if (a < 0) { a = -value; b = value - 1; }
        uint nBits;
        if (a < 0x100) nBits = BitCount[a];
        else nBits = (uint)(8 + BitCount[a >> 8]);
        EmitHuff(h, (runLength << 4) | (int)nBits);
        if (nBits > 0) Emit((uint)b & ((1u << (int)nBits) - 1), nBits);
    }

    void WriteMarkerHeader(byte marker, int markerlen)
    {
        _buf[0] = 0xff;
        _buf[1] = marker;
        _buf[2] = (byte)(markerlen >> 8);
        _buf[3] = (byte)(markerlen & 0xff);
        Write(_buf, 0, 4);
    }

    void WriteDQT()
    {
        const int markerlen = 2 + 2 * (1 + Block.BlockSize);
        WriteMarkerHeader(Const.dqtMarker, markerlen);
        for (int i = 0; i < 2; i++)
        {
            WriteByte((byte)i);
            Write(_encQuant[i]);
        }
    }

    void WriteSOF0(Point size, int nComponent)
    {
        int markerlen = 8 + 3 * nComponent;
        WriteMarkerHeader(Const.sof0Marker, markerlen);
        _buf[0] = 8; // 8-bit color
        _buf[1] = (byte)(size.Y >> 8);
        _buf[2] = (byte)(size.Y & 0xff);
        _buf[3] = (byte)(size.X >> 8);
        _buf[4] = (byte)(size.X & 0xff);
        _buf[5] = (byte)nComponent;
        if (nComponent == 1)
        {
            _buf[6] = 1;
            _buf[7] = 0x11;
            _buf[8] = 0x00;
        }
        else
        {
            byte[] hvTable = { 0x22, 0x11, 0x11 };
            byte[] tqTable = { 0x00, 0x01, 0x01 };
            for (int i = 0; i < nComponent; i++)
            {
                _buf[3 * i + 6] = (byte)(i + 1);
                _buf[3 * i + 7] = hvTable[i];
                _buf[3 * i + 8] = tqTable[i];
            }
        }
        Write(_buf, 0, 3 * (nComponent - 1) + 9);
    }

    void WriteDHT(int nComponent)
    {
        int markerlen = 2;
        var specs = nComponent == 1
            ? new[] { TheHuffmanSpec[0], TheHuffmanSpec[1] }
            : TheHuffmanSpec;
        foreach (var s in specs)
            markerlen += 1 + 16 + s.Value.Length;
        WriteMarkerHeader(Const.dhtMarker, markerlen);
        byte[] idxTable = { 0x00, 0x10, 0x01, 0x11 };
        for (int i = 0; i < specs.Length; i++)
        {
            WriteByte(idxTable[i]);
            Write(specs[i].Count);
            Write(specs[i].Value);
        }
    }

    int WriteBlock(ref Block b, QuantIndex q, int prevDC)
    {
        Dct.Fdct(ref b);
        int dc = Div(b[0], 8 * (int)_encQuant[(int)q][0]);
        EmitHuffRLE((HuffIndex)(2 * (int)q + 0), 0, dc - prevDC);
        var h = (HuffIndex)(2 * (int)q + 1);
        int runLength = 0;
        for (int zig = 1; zig < Block.BlockSize; zig++)
        {
            int ac = Div(b[Unzig.Table[zig]], 8 * (int)_encQuant[(int)q][zig]);
            if (ac == 0) { runLength++; }
            else
            {
                while (runLength > 15)
                {
                    EmitHuff(h, 0xf0);
                    runLength -= 16;
                }
                EmitHuffRLE(h, runLength, ac);
                runLength = 0;
            }
        }
        if (runLength > 0) EmitHuff(h, 0x00);
        return dc;
    }

    static void ToYCbCr(IImage m, Point p, ref Block yBlock, ref Block cbBlock, ref Block crBlock)
    {
        var b = m.Bounds();
        int xmax = b.Max.X - 1, ymax = b.Max.Y - 1;
        for (int j = 0; j < 8; j++)
        {
            for (int i = 0; i < 8; i++)
            {
                var (r, g, bl, _) = m.At(Math.Min(p.X + i, xmax), Math.Min(p.Y + j, ymax)).GetRGBA();
                var (yy, cb, cr) = YCbCrUtil.RGBToYCbCr((byte)(r >> 8), (byte)(g >> 8), (byte)(bl >> 8));
                yBlock[8 * j + i] = yy;
                cbBlock[8 * j + i] = cb;
                crBlock[8 * j + i] = cr;
            }
        }
    }

    static void GrayToY(GoImage.Image.Gray m, Point p, ref Block yBlock)
    {
        var b = m.Bounds();
        int xmax = b.Max.X - 1, ymax = b.Max.Y - 1;
        for (int j = 0; j < 8; j++)
            for (int i = 0; i < 8; i++)
                yBlock[8 * j + i] = m.Pix[m.PixOffset(Math.Min(p.X + i, xmax), Math.Min(p.Y + j, ymax))];
    }

    static void Scale(ref Block dst, ref Block src0, ref Block src1, ref Block src2, ref Block src3)
    {
        var srcArr = new[] { src0.Data, src1.Data, src2.Data, src3.Data };
        for (int i = 0; i < 4; i++)
        {
            int dstOff = ((i & 2) << 4) | ((i & 1) << 2);
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    int j = 16 * y + 2 * x;
                    int sum = srcArr[i][j] + srcArr[i][j + 1] + srcArr[i][j + 8] + srcArr[i][j + 9];
                    dst[8 * y + x + dstOff] = (sum + 2) >> 2;
                }
            }
        }
    }

    void WriteSOS(IImage m)
    {
        if (m is GoImage.Image.Gray) Write(SosHeaderY);
        else Write(SosHeaderYCbCr);

        Block b = new Block();
        Block[] cb = { new Block(), new Block(), new Block(), new Block() };
        Block[] cr = { new Block(), new Block(), new Block(), new Block() };
        int prevDCY = 0, prevDCCb = 0, prevDCCr = 0;

        var bounds = m.Bounds();
        if (m is GoImage.Image.Gray gray)
        {
            for (int y = bounds.Min.Y; y < bounds.Max.Y; y += 8)
            {
                for (int x = bounds.Min.X; x < bounds.Max.X; x += 8)
                {
                    var p = Pt.New(x, y);
                    GrayToY(gray, p, ref b);
                    prevDCY = WriteBlock(ref b, QuantIndex.Luminance, prevDCY);
                }
            }
        }
        else
        {
            var rgba = m as GoImage.Image.RGBA;
            var ycbcr = m as GoImage.Image.YCbCr;
            for (int y = bounds.Min.Y; y < bounds.Max.Y; y += 16)
            {
                for (int x = bounds.Min.X; x < bounds.Max.X; x += 16)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        int xOff = (i & 1) * 8;
                        int yOff = (i & 2) * 4;
                        var p = Pt.New(x + xOff, y + yOff);
                        if (rgba != null)
                            RgbaToYCbCr(rgba, p, ref b, ref cb[i], ref cr[i]);
                        else if (ycbcr != null)
                            YCbCrToYCbCrEnc(ycbcr, p, ref b, ref cb[i], ref cr[i]);
                        else
                            ToYCbCr(m, p, ref b, ref cb[i], ref cr[i]);
                        prevDCY = WriteBlock(ref b, QuantIndex.Luminance, prevDCY);
                    }
                    Scale(ref b, ref cb[0], ref cb[1], ref cb[2], ref cb[3]);
                    prevDCCb = WriteBlock(ref b, QuantIndex.Chrominance, prevDCCb);
                    Scale(ref b, ref cr[0], ref cr[1], ref cr[2], ref cr[3]);
                    prevDCCr = WriteBlock(ref b, QuantIndex.Chrominance, prevDCCr);
                }
            }
        }
        Emit(0x7f, 7); // Pad last byte with 1's
    }

    static void RgbaToYCbCr(GoImage.Image.RGBA m, Point p, ref Block yBlock, ref Block cbBlock, ref Block crBlock)
    {
        var b = m.Bounds();
        int xmax = b.Max.X - 1, ymax = b.Max.Y - 1;
        for (int j = 0; j < 8; j++)
        {
            int sj = Math.Min(p.Y + j, ymax);
            int offset = (sj - b.Min.Y) * m.Stride - b.Min.X * 4;
            for (int i = 0; i < 8; i++)
            {
                int sx = Math.Min(p.X + i, xmax);
                int pix = offset + sx * 4;
                var (yy, cb, cr) = YCbCrUtil.RGBToYCbCr(m.Pix[pix], m.Pix[pix + 1], m.Pix[pix + 2]);
                yBlock[8 * j + i] = yy;
                cbBlock[8 * j + i] = cb;
                crBlock[8 * j + i] = cr;
            }
        }
    }

    static void YCbCrToYCbCrEnc(GoImage.Image.YCbCr m, Point p, ref Block yBlock, ref Block cbBlock, ref Block crBlock)
    {
        var b = m.Bounds();
        int xmax = b.Max.X - 1, ymax = b.Max.Y - 1;
        for (int j = 0; j < 8; j++)
        {
            int sy = Math.Min(p.Y + j, ymax);
            for (int i = 0; i < 8; i++)
            {
                int sx = Math.Min(p.X + i, xmax);
                int yi = m.YOffset(sx, sy);
                int ci = m.COffset(sx, sy);
                yBlock[8 * j + i] = m.GetY(yi);
                cbBlock[8 * j + i] = m.GetCb(ci);
                crBlock[8 * j + i] = m.GetCr(ci);
            }
        }
    }

    /// <summary>
    /// Encode writes the Image m to w in JPEG 4:2:0 baseline format.
    /// </summary>
    public static void Encode(Stream w, IImage m, Options? o = null)
    {
        var b = m.Bounds();
        if (b.Dx() >= (1 << 16) || b.Dy() >= (1 << 16))
            throw new ArgumentException("jpeg: image is too large to encode");

        var e = new Encoder();
        e._w = w;

        int quality = DefaultQuality;
        if (o != null)
        {
            quality = o.Quality;
            if (quality < 1) quality = 1;
            else if (quality > 100) quality = 100;
        }

        int scale;
        if (quality < 50) scale = 5000 / quality;
        else scale = 200 - quality * 2;

        // Initialize quantization tables
        e._encQuant[0] = new byte[Block.BlockSize];
        e._encQuant[1] = new byte[Block.BlockSize];
        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < Block.BlockSize; j++)
            {
                int x = (UnscaledQuant[i][j] * scale + 50) / 100;
                if (x < 1) x = 1;
                else if (x > 255) x = 255;
                e._encQuant[i][j] = (byte)x;
            }
        }

        int nComponent = m is GoImage.Image.Gray ? 1 : 3;

        // SOI
        e._buf[0] = 0xff; e._buf[1] = 0xd8;
        e.Write(e._buf, 0, 2);

        e.WriteDQT();
        e.WriteSOF0(b.Size(), nComponent);
        e.WriteDHT(nComponent);
        e.WriteSOS(m);

        // EOI
        e._buf[0] = 0xff; e._buf[1] = 0xd9;
        e.Write(e._buf, 0, 2);
        e.Flush();

        if (e._err != null) throw e._err;
    }
}

/// <summary>
/// Options are the encoding parameters.
/// </summary>
public class Options
{
    public int Quality;
}
