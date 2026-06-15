// Port of Go's image/jpeg/huffman.go.

namespace GoImage.Jpeg;

using System.Runtime.CompilerServices;

/// <summary>
/// errShortHuffmanData means an unexpected EOF occurred while decoding Huffman data.
/// </summary>
public class ShortHuffmanDataException : FormatErrorException
{
    public ShortHuffmanDataException() : base("short Huffman data") { }
}

/// <summary>
/// huffman is a Huffman decoder, specified in section C.
/// </summary>
public struct Huffman
{
    public const int MaxCodeLength = 16;
    public const int MaxNCodes = 256;
    public const int LutSize = 8;

    // The number of codes in the tree.
    public int NCodes;

    // LUT for the next LutSize bits. High 8 bits = encoded value, low 8 bits = 1 + code length (or 0 if too large).
    public ushort[] Lut; // [1 << LutSize]

    // Decoded values sorted by their encoding.
    public byte[] Vals; // [MaxNCodes]

    // MinCodes[i] is the minimum code of length i, or -1.
    public int[] MinCodes; // [MaxCodeLength]

    // MaxCodes[i] is the maximum code of length i, or -1.
    public int[] MaxCodes; // [MaxCodeLength]

    // ValsIndices[i] is the index into vals of MinCodes[i].
    public int[] ValsIndices; // [MaxCodeLength]

    public static Huffman New()
    {
        return new Huffman
        {
            Lut = new ushort[1 << LutSize],
            Vals = new byte[MaxNCodes],
            MinCodes = new int[MaxCodeLength],
            MaxCodes = new int[MaxCodeLength],
            ValsIndices = new int[MaxCodeLength],
        };
    }
}

/// <summary>
/// Huffman decoder methods for the Decoder struct.
/// </summary>
public partial class Decoder
{
    /// <summary>
    /// ensureNBits reads bytes from the byte buffer to ensure that d.bits.n is at least n.
    /// </summary>
    internal void EnsureNBits(int n)
    {
        while (true)
        {
            byte c = ReadByteStuffedByte();
            _bits.a = (_bits.a << 8) | c;
            _bits.n += 8;
            if (_bits.m == 0)
                _bits.m = 1 << 7;
            else
                _bits.m <<= 8;
            if (_bits.n >= n) break;
        }
    }

    /// <summary>
    /// receiveExtend is the composition of RECEIVE and EXTEND (section F.2.2.1).
    /// </summary>
    internal int ReceiveExtend(byte t)
    {
        if (_bits.n < t)
            EnsureNBits(t);
        _bits.n -= t;
        _bits.m >>= t;
        int s = 1 << t;
        int x = (int)((_bits.a >> (byte)_bits.n) & (uint)(s - 1));

        // Branchless sign extension
        int sign = (x >> (t - 1)) - 1;
        x += sign & ((-1 << t) + 1);

        return x;
    }

    /// <summary>
    /// processDHT processes a Define Huffman Table marker.
    /// </summary>
    internal void ProcessDHT(int n)
    {
        while (n > 0)
        {
            if (n < 17) throw new FormatErrorException("DHT has wrong length");
            ReadFull(_tmp, 0, 17);
            byte tc = (byte)(_tmp[0] >> 4);
            if (tc > Const.maxTc) throw new FormatErrorException("bad Tc value");
            byte th = (byte)(_tmp[0] & 0x0f);
            if (th > Const.maxTh || (_baseline && th > 1))
                throw new FormatErrorException("bad Th value");

            ref Huffman h = ref _huff[tc, th];

            // Read nCodes
            h.NCodes = 0;
            var nCodes = new int[Huffman.MaxCodeLength];
            for (int i = 0; i < nCodes.Length; i++)
            {
                nCodes[i] = _tmp[i + 1];
                h.NCodes += nCodes[i];
            }
            if (h.NCodes == 0) throw new FormatErrorException("Huffman table has zero length");
            if (h.NCodes > Huffman.MaxNCodes) throw new FormatErrorException("Huffman table has excessive length");
            n -= h.NCodes + 17;
            if (n < 0) throw new FormatErrorException("DHT has wrong length");
            ReadFull(h.Vals, 0, h.NCodes);

            // Derive the LUT
            Array.Clear(h.Lut);
            uint x = 0, code = 0;
            for (uint i = 0; i < Huffman.LutSize; i++)
            {
                code <<= 1;
                for (int j = 0; j < nCodes[i]; j++)
                {
                    byte bbase = (byte)(code << (7 - (int)i));
                    ushort lutValue = (ushort)(((uint)h.Vals[x] << 8) | (uint)(2 + i));
                    for (int k = 0; k < (1 << (7 - (int)i)); k++)
                    {
                        h.Lut[bbase | k] = lutValue;
                    }
                    code++;
                    x++;
                }
            }

            // Derive minCodes, maxCodes, and valsIndices
            int c = 0, index = 0;
            for (int i = 0; i < nCodes.Length; i++)
            {
                if (nCodes[i] == 0)
                {
                    h.MinCodes[i] = -1;
                    h.MaxCodes[i] = -1;
                    h.ValsIndices[i] = -1;
                }
                else
                {
                    h.MinCodes[i] = c;
                    h.MaxCodes[i] = c + nCodes[i] - 1;
                    h.ValsIndices[i] = index;
                    c += nCodes[i];
                    index += nCodes[i];
                }
                c <<= 1;
            }
        }
    }

    /// <summary>
    /// decodeHuffman returns the next Huffman-coded value from the bit-stream.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal byte DecodeHuffman(ref Huffman h)
    {
        if (h.NCodes == 0)
            throw new FormatErrorException("uninitialized Huffman table");

        if (_bits.n < 8)
        {
            try
            {
                EnsureNBits(8);
            }
            catch (MissingFF00Exception)
            {
                goto slowPath;
            }
            catch (ShortHuffmanDataException)
            {
                goto slowPath;
            }
        }
        {
            ushort v = h.Lut[(_bits.a >> (int)(_bits.n - Huffman.LutSize)) & 0xff];
            if (v != 0)
            {
                int nn = (v & 0xff) - 1;
                _bits.n -= nn;
                _bits.m >>= nn;
                return (byte)(v >> 8);
            }
        }

    slowPath:
        for (int i = 0, code = 0; i < Huffman.MaxCodeLength; i++)
        {
            if (_bits.n == 0) EnsureNBits(1);
            if ((_bits.a & _bits.m) != 0) code |= 1;
            _bits.n--;
            _bits.m >>= 1;
            if (code <= h.MaxCodes[i])
                return h.Vals[h.ValsIndices[i] + code - h.MinCodes[i]];
            code <<= 1;
        }
        throw new FormatErrorException("bad Huffman code");
    }

    internal bool DecodeBit()
    {
        if (_bits.n == 0) EnsureNBits(1);
        bool ret = (_bits.a & _bits.m) != 0;
        _bits.n--;
        _bits.m >>= 1;
        return ret;
    }

    internal uint DecodeBits(int n)
    {
        if (_bits.n < n) EnsureNBits(n);
        uint ret = _bits.a >> (int)(_bits.n - n);
        ret &= (1u << n) - 1;
        _bits.n -= n;
        _bits.m >>= n;
        return ret;
    }
}
