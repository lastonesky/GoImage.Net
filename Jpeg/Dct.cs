// Port of Go's image/jpeg/dct.go (Discrete Cosine Transformation).
//
// Uses the algorithm from Christoph Loeffler, Adriaan Lightenberg, and George S. Mostchytz,
// "Practical Fast 1-D DCT Algorithms with 11 Multiplications," ICASSP 1989.

namespace GoImage.Jpeg;

/// <summary>
/// A block is an 8x8 input to a 2D DCT.
/// </summary>
public struct Block
{
    public const int BlockSize = 8 * 8;
    // Fixed-size storage matching Go's [blockSize]int32.
    // Stored as an array for performance.
    public int[] Data;

    public Block()
    {
        Data = new int[BlockSize];
    }

    public int this[int index]
    {
        get => Data[index];
        set => Data[index] = value;
    }
}

/// <summary>
/// DCT implementations - both forward (FDCT) and inverse (IDCT).
/// </summary>
public static class Dct
{
    // Constants needed for the implementation.
    // These are all 60-bit precision fixed-point constants.
    const long cos1          = 1130768441178740757L;
    const long sin1          = 224923827593068887L;
    const long cos3          = 958619196450722178L;
    const long sin3          = 640528868967736374L;
    const long sqrt2         = 1630477228166597777L;
    const long sqrt2_cos6    = 623956622067911264L;
    const long sqrt2_sin6    = 1506364539328854985L;
    const long sqrt2inv      = 815238614083298888L;
    const long sqrt2inv_cos6 = 311978311033955632L;
    const long sqrt2inv_sin6 = 753182269664427492L;

    static int C(long x, int bits)
    {
        return (int)((x + (1L << (59 - bits))) >> (60 - bits));
    }

    /// <summary>
    /// dctBox implements a 3-multiply, 3-add rotation+scaling.
    /// </summary>
    static void DctBox(int x0, int x1, int kcos, int ksin,
                       out int y0, out int y1)
    {
        int ksum = kcos * (x0 + x1);
        y0 = ksum + (ksin - kcos) * x1;
        y1 = ksum - (kcos + ksin) * x0;
    }

    // ---- Forward DCT ----

    /// <summary>
    /// fdct implements the forward DCT.
    /// Inputs are UQ8.0; outputs are Q13.0.
    /// </summary>
    public static void Fdct(ref Block b)
    {
        FdctCols(ref b);
        FdctRows(ref b);
    }

    static void FdctCols(ref Block b)
    {
        var d = b.Data;
        for (int i = 0; i < 8; i++)
        {
            int x0 = d[0 * 8 + i];
            int x1 = d[1 * 8 + i];
            int x2 = d[2 * 8 + i];
            int x3 = d[3 * 8 + i];
            int x4 = d[4 * 8 + i];
            int x5 = d[5 * 8 + i];
            int x6 = d[6 * 8 + i];
            int x7 = d[7 * 8 + i];

            // Stage 1
            int t0 = x0 + x7; x7 = x0 - x7; x0 = t0;
            t0 = x1 + x6; x6 = x1 - x6; x1 = t0;
            t0 = x2 + x5; x5 = x2 - x5; x2 = t0;
            t0 = x3 + x4; x4 = x3 - x4; x3 = t0;

            // Stage 2
            DctBox(x4, x7, C(cos3, 18), C(sin3, 18), out x4, out x7);
            DctBox(x5, x6, C(cos1, 18), C(sin1, 18), out x5, out x6);
            t0 = x0 + x3; x3 = x0 - x3; x0 = t0;
            t0 = x1 + x2; x2 = x1 - x2; x1 = t0;

            // Stage 3
            DctBox(x2, x3, C(sqrt2_cos6, 18), C(sqrt2_sin6, 18), out x2, out x3);
            t0 = x0 + x1; x1 = x0 - x1; x0 = t0;

            d[0 * 8 + i] = (x0 - 128 * 8) << 18;
            d[4 * 8 + i] = x1 << 18;
            d[2 * 8 + i] = x2;
            d[6 * 8 + i] = x3;

            t0 = x4 + x6; x6 = x4 - x6; x4 = t0;
            t0 = x7 + x5; x5 = x7 - x5; x7 = t0;

            // Stage 4
            x5 = (x5 >> 12) * C(sqrt2, 12);
            x6 = (x6 >> 12) * C(sqrt2, 12);
            t0 = x7 + x4; x4 = x7 - x4; x7 = t0;

            d[1 * 8 + i] = x7;
            d[3 * 8 + i] = x5;
            d[5 * 8 + i] = x6;
            d[7 * 8 + i] = x4;
        }
    }

    static void FdctRows(ref Block b)
    {
        var d = b.Data;
        for (int i = 0; i < 8; i++)
        {
            int b8i = 8 * i;
            int x0 = d[b8i + 0];
            int x1 = d[b8i + 1];
            int x2 = d[b8i + 2];
            int x3 = d[b8i + 3];
            int x4 = d[b8i + 4];
            int x5 = d[b8i + 5];
            int x6 = d[b8i + 6];
            int x7 = d[b8i + 7];

            // Stage 1
            int t0 = x0 + x7; x7 = x0 - x7; x0 = t0;
            t0 = x1 + x6; x6 = x1 - x6; x1 = t0;
            t0 = x2 + x5; x5 = x2 - x5; x2 = t0;
            t0 = x3 + x4; x4 = x3 - x4; x3 = t0;

            // Stage 2
            DctBox(x4 >> 14, x7 >> 14, C(cos3, 14), C(sin3, 14), out x4, out x7);
            DctBox(x5 >> 14, x6 >> 14, C(cos1, 14), C(sin1, 14), out x5, out x6);
            t0 = x0 + x3; x3 = x0 - x3; x0 = t0;
            t0 = x1 + x2; x2 = x1 - x2; x1 = t0;

            // Stage 3
            DctBox(x2 >> 14, x3 >> 14, C(sqrt2_cos6, 14), C(sqrt2_sin6, 14), out x2, out x3);
            t0 = x0 + x1; x1 = x0 - x1; x0 = t0;
            t0 = x4 + x6; x6 = x4 - x6; x4 = t0;
            t0 = x7 + x5; x5 = x7 - x5; x7 = t0;

            // Stage 4
            x5 = (x5 >> 14) * C(sqrt2, 14);
            x6 = (x6 >> 14) * C(sqrt2, 14);
            t0 = x7 + x4; x4 = x7 - x4; x7 = t0;

            // Cut from Q13.18 to Q13.0
            x0 = (x0 + (1 << 17)) >> 18;
            x1 = (x1 + (1 << 17)) >> 18;
            x2 = (x2 + (1 << 17)) >> 18;
            x3 = (x3 + (1 << 17)) >> 18;
            x4 = (x4 + (1 << 17)) >> 18;
            x5 = (x5 + (1 << 17)) >> 18;
            x6 = (x6 + (1 << 17)) >> 18;
            x7 = (x7 + (1 << 17)) >> 18;

            // Permute
            d[b8i + 0] = x0;
            d[b8i + 1] = x7;
            d[b8i + 2] = x2;
            d[b8i + 3] = x5;
            d[b8i + 4] = x1;
            d[b8i + 5] = x6;
            d[b8i + 6] = x3;
            d[b8i + 7] = x4;
        }
    }

    // ---- Inverse DCT ----

    /// <summary>
    /// idct implements the inverse DCT.
    /// Inputs are UQ8.0; outputs are Q10.3.
    /// </summary>
    public static void Idct(ref Block b)
    {
        IdctRows(ref b);
        IdctCols(ref b);
    }

    static void IdctRows(ref Block b)
    {
        var d = b.Data;
        for (int i = 0; i < 8; i++)
        {
            int b8i = 8 * i;
            int x0 = d[b8i + 0];
            int x7 = d[b8i + 1];
            int x2 = d[b8i + 2];
            int x5 = d[b8i + 3];
            int x1 = d[b8i + 4];
            int x6 = d[b8i + 5];
            int x3 = d[b8i + 6];
            int x4 = d[b8i + 7];

            // Stages 4, 3, 2: x0, x1, x2, x3.
            x0 <<= 17;
            x1 <<= 17;
            int t0 = x0 + x1; x1 = x0 - x1; x0 = t0;

            DctBox(x2, x3, C(sqrt2inv_cos6, 18), -C(sqrt2inv_sin6, 18), out x2, out x3);
            t0 = x1 + x2; x2 = x1 - x2; x1 = t0;
            t0 = x0 + x3; x3 = x0 - x3; x0 = t0;

            // Stages 4, 3, 2: x4, x5, x6, x7.
            x4 <<= 7;
            x7 <<= 7;
            t0 = x7 + x4; x4 = x7 - x4; x7 = t0;

            x6 = x6 * C(sqrt2inv, 8);
            x5 = x5 * C(sqrt2inv, 8);

            t0 = x7 + x5; x5 = x7 - x5; x7 = t0;
            t0 = x4 + x6; x6 = x4 - x6; x4 = t0;

            DctBox(x4 >> 2, x7 >> 2, C(cos3, 12), -C(sin3, 12), out x4, out x7);
            DctBox(x5 >> 2, x6 >> 2, C(cos1, 12), -C(sin1, 12), out x5, out x6);

            // Stage 1
            t0 = x0 + x7; x7 = x0 - x7; x0 = t0;
            t0 = x1 + x6; x6 = x1 - x6; x1 = t0;
            t0 = x2 + x5; x5 = x2 - x5; x2 = t0;
            t0 = x3 + x4; x4 = x3 - x4; x3 = t0;

            d[b8i + 0] = x0;
            d[b8i + 1] = x1;
            d[b8i + 2] = x2;
            d[b8i + 3] = x3;
            d[b8i + 4] = x4;
            d[b8i + 5] = x5;
            d[b8i + 6] = x6;
            d[b8i + 7] = x7;
        }
    }

    static void IdctCols(ref Block b)
    {
        var d = b.Data;
        for (int i = 0; i < 8; i++)
        {
            int x0 = d[0 * 8 + i];
            int x7 = d[1 * 8 + i];
            int x2 = d[2 * 8 + i];
            int x5 = d[3 * 8 + i];
            int x1 = d[4 * 8 + i];
            int x6 = d[5 * 8 + i];
            int x3 = d[6 * 8 + i];
            int x4 = d[7 * 8 + i];

            x0 += 1 << 19;

            // Stages 4, 3, 2: x0, x1, x2, x3.
            int t0 = (x0 + x1) >> 2; x1 = (x0 - x1) >> 2; x0 = t0;
            DctBox(x2 >> 13, x3 >> 13, C(sqrt2inv_cos6, 12), -C(sqrt2inv_sin6, 12), out x2, out x3);

            t0 = x1 + x2; x2 = x1 - x2; x1 = t0;
            t0 = x0 + x3; x3 = x0 - x3; x0 = t0;

            // Stages 4, 3, 2: x4, x5, x6, x7.
            t0 = x7 + x4; x4 = x7 - x4; x7 = t0;

            x5 = (x5 >> 13) * C(sqrt2inv, 14);
            x6 = (x6 >> 13) * C(sqrt2inv, 14);

            t0 = x7 + x5; x5 = x7 - x5; x7 = t0;
            t0 = x4 + x6; x6 = x4 - x6; x4 = t0;

            DctBox(x4 >> 14, x7 >> 14, C(cos3, 12), -C(sin3, 12), out x4, out x7);
            DctBox(x5 >> 14, x6 >> 14, C(cos1, 12), -C(sin1, 12), out x5, out x6);

            t0 = x0 + x7; x7 = x0 - x7; x0 = t0;
            t0 = x1 + x6; x6 = x1 - x6; x1 = t0;
            t0 = x2 + x5; x5 = x2 - x5; x2 = t0;
            t0 = x3 + x4; x4 = x3 - x4; x3 = t0;

            x0 >>= 18;
            x1 >>= 18;
            x2 >>= 18;
            x3 >>= 18;
            x4 >>= 18;
            x5 >>= 18;
            x6 >>= 18;
            x7 >>= 18;

            d[0 * 8 + i] = x0;
            d[1 * 8 + i] = x1;
            d[2 * 8 + i] = x2;
            d[3 * 8 + i] = x3;
            d[4 * 8 + i] = x4;
            d[5 * 8 + i] = x5;
            d[6 * 8 + i] = x6;
            d[7 * 8 + i] = x7;
        }
    }
}
