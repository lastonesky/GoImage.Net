// Wu's color quantization algorithm for optimal palette generation.
// Reference: Xiaolin Wu, "Efficient Statistical Computations for Optimal
// Color Quantization", Graphics Gems II, 1991.
//
// Algorithm overview:
//   1. Build a 3D histogram of pixel colors in a quantized RGB grid (33×33×33).
//   2. Compute 3D prefix sums for O(1) box-statistic queries.
//   3. Iteratively split boxes along the axis that maximizes between-class
//      variance, producing up to N optimal color regions.
//   4. Each box's centroid becomes a palette entry.

using GoImage.Color;
using GoImage.Image;

namespace GoImage.Gif;

public class WuQuantizer
{
    // Grid indices 0..32.  Pixels map to 1..32 (5-bit per channel, shifted by 1).
    // Index 0 on each axis is always zero — it serves as the prefix-sum boundary.
    private const int NQ = 33;

    // 3D moment arrays stored flat: index = r * NQ² + g * NQ + b.
    private long[] _wt = null!;   // weight  (pixel count)
    private long[] _mr = null!;   // moment  (sum of R)
    private long[] _mg = null!;   // moment  (sum of G)
    private long[] _mb = null!;   // moment  (sum of B)

    // After Quantize(), maps each voxel to its box (palette) index.
    private int[] _tag = null!;

    private struct Box
    {
        public int R0, R1, G0, G1, B0, B1; // inclusive bounds
    }

    /// <summary>
    /// Generate an optimal <paramref name="numColors"/>-color palette for
    /// <paramref name="img"/> using Wu's quantization.
    /// </summary>
    public Palette Quantize(IImage img, int numColors = 256)
    {
        numColors = Math.Clamp(numColors, 2, 256);

        int size = NQ * NQ * NQ;
        _wt = new long[size];
        _mr = new long[size];
        _mg = new long[size];
        _mb = new long[size];

        // ---------- 1. Build histogram ----------
        BuildHistogram(img);

        // ---------- 2. Build 3-D prefix sums ----------
        ComputePrefixSums();

        // ---------- 3. Top-down box splitting ----------
        var boxes = new Box[numColors];
        boxes[0] = new Box { R0 = 1, R1 = 32, G0 = 1, G1 = 32, B0 = 1, B1 = 32 };
        int numBoxes = 1;

        while (numBoxes < numColors)
        {
            float bestScore = -1f;
            int bestBox = -1, bestDir = -1, bestPos = -1;

            for (int i = 0; i < numBoxes; i++)
            {
                for (int dir = 0; dir < 3; dir++)
                {
                    float score = FindCut(boxes[i], dir, out int pos);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestBox = i;
                        bestDir = dir;
                        bestPos = pos;
                    }
                }
            }

            if (bestBox < 0)
                break; // no further split possible

            var b = boxes[bestBox];
            Box nb;

            switch (bestDir)
            {
                case 0: // R
                    nb = new Box { R0 = bestPos + 1, R1 = b.R1, G0 = b.G0, G1 = b.G1, B0 = b.B0, B1 = b.B1 };
                    b.R1 = bestPos;
                    break;
                case 1: // G
                    nb = new Box { R0 = b.R0, R1 = b.R1, G0 = bestPos + 1, G1 = b.G1, B0 = b.B0, B1 = b.B1 };
                    b.G1 = bestPos;
                    break;
                default: // B
                    nb = new Box { R0 = b.R0, R1 = b.R1, G0 = b.G0, G1 = b.G1, B0 = bestPos + 1, B1 = b.B1 };
                    b.B1 = bestPos;
                    break;
            }

            boxes[bestBox] = b;
            boxes[numBoxes] = nb;
            numBoxes++;
        }

        // ---------- 4. Generate palette from box centroids ----------
        var palette = new Palette();
        for (int i = 0; i < numBoxes; i++)
        {
            long w = Vol(_wt, boxes[i]);
            if (w > 0)
            {
                byte r = (byte)(Vol(_mr, boxes[i]) / w);
                byte g = (byte)(Vol(_mg, boxes[i]) / w);
                byte bl = (byte)(Vol(_mb, boxes[i]) / w);
                palette.Add(new Color.RGBA(r, g, bl, 255));
            }
        }

        // ---------- 5. Tag voxels with their box index ----------
        _tag = new int[size];
        for (int i = 0; i < numBoxes; i++)
        {
            var box = boxes[i];
            for (int r = box.R0; r <= box.R1; r++)
                for (int g = box.G0; g <= box.G1; g++)
                    for (int bv = box.B0; bv <= box.B1; bv++)
                        _tag[Idx(r, g, bv)] = i;
        }

        // Pad palette to exactly numColors entries.
        while (palette.Count < numColors)
            palette.Add(new Color.RGBA(0, 0, 0, 255));

        return palette;
    }

    /// <summary>
    /// Map a pixel colour to its palette index via the voxel grid.
    /// Call <see cref="Quantize"/> first.
    /// </summary>
    public int MapColor(IColor c)
    {
        var (r16, g16, b16, _) = c.GetRGBA();
        int r = Math.Clamp(((int)(r16 >> 8) >> 3) + 1, 1, 32);
        int g = Math.Clamp(((int)(g16 >> 8) >> 3) + 1, 1, 32);
        int b = Math.Clamp(((int)(b16 >> 8) >> 3) + 1, 1, 32);
        return _tag[Idx(r, g, b)];
    }

    // -------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------

    private static int Idx(int r, int g, int b) => r * NQ * NQ + g * NQ + b;

    // --- Histogram construction ---

    private void BuildHistogram(IImage img)
    {
        var bounds = img.Bounds();
        int dx = bounds.Dx();

        if (img is GoImage.Image.RGBA rgba)
        {
            // Fast path: directly read RGBA byte values.
            for (int y = bounds.Min.Y; y < bounds.Max.Y; y++)
            {
                var row = rgba.GetRowSpan(y);
                for (int x = 0; x < dx; x++)
                {
                    var p = row[x];
                    AddPixel(p.R, p.G, p.B, p.A);
                }
            }
        }
        else
        {
            // Generic path: go through IColor → 16-bit premultiplied RGBA.
            for (int y = bounds.Min.Y; y < bounds.Max.Y; y++)
            {
                for (int x = bounds.Min.X; x < bounds.Max.X; x++)
                {
                    var (r16, g16, b16, a16) = img.At(x, y).GetRGBA();
                    if (a16 == 0) continue;

                    if (a16 < 0xffff)
                    {
                        // Un-premultiply to recover the true surface colour.
                        r16 = Math.Min(r16 * 0xffff / a16, 0xffff);
                        g16 = Math.Min(g16 * 0xffff / a16, 0xffff);
                        b16 = Math.Min(b16 * 0xffff / a16, 0xffff);
                    }
                    AddPixel((byte)(r16 >> 8), (byte)(g16 >> 8), (byte)(b16 >> 8), (byte)(a16 >> 8));
                }
            }
        }
    }

    private void AddPixel(byte r, byte g, byte b, byte a)
    {
        if (a == 0) return; // skip fully-transparent pixels

        int ir = (r >> 3) + 1; // 0-255 → 1-32
        int ig = (g >> 3) + 1;
        int ib = (b >> 3) + 1;

        int idx = Idx(ir, ig, ib);
        _wt[idx]++;
        _mr[idx] += r;
        _mg[idx] += g;
        _mb[idx] += b;
    }

    // --- 3-D prefix sums (in-place) ---

    private void ComputePrefixSums()
    {
        for (int r = 1; r < NQ; r++)
        {
            for (int g = 1; g < NQ; g++)
            {
                for (int b = 1; b < NQ; b++)
                {
                    int i   = Idx(r,     g,     b);
                    int i0  = Idx(r - 1, g,     b);
                    int i1  = Idx(r,     g - 1, b);
                    int i2  = Idx(r,     g,     b - 1);
                    int i3  = Idx(r - 1, g - 1, b);
                    int i4  = Idx(r - 1, g,     b - 1);
                    int i5  = Idx(r,     g - 1, b - 1);
                    int i6  = Idx(r - 1, g - 1, b - 1);

                    _wt[i] += _wt[i0] + _wt[i1] + _wt[i2] - _wt[i3] - _wt[i4] - _wt[i5] + _wt[i6];
                    _mr[i] += _mr[i0] + _mr[i1] + _mr[i2] - _mr[i3] - _mr[i4] - _mr[i5] + _mr[i6];
                    _mg[i] += _mg[i0] + _mg[i1] + _mg[i2] - _mg[i3] - _mg[i4] - _mg[i5] + _mg[i6];
                    _mb[i] += _mb[i0] + _mb[i1] + _mb[i2] - _mb[i3] - _mb[i4] - _mb[i5] + _mb[i6];
                }
            }
        }
    }

    // --- Box statistics via inclusion-exclusion on prefix sums ---

    /// <summary>
    /// Sum of <paramref name="arr"/> over the inclusive box
    /// [R0,R1]×[G0,G1]×[B0,B1].  Requires R0,G0,B0 ≥ 1.
    /// </summary>
    private long Vol(long[] arr, Box b)
        => Vol(arr, b.R0, b.R1, b.G0, b.G1, b.B0, b.B1);

    private long Vol(long[] arr, int r0, int r1, int g0, int g1, int b0, int b1)
    {
        return arr[Idx(r1, g1, b1)]
             - arr[Idx(r0 - 1, g1, b1)]
             - arr[Idx(r1, g0 - 1, b1)]
             - arr[Idx(r1, g1, b0 - 1)]
             + arr[Idx(r0 - 1, g0 - 1, b1)]
             + arr[Idx(r0 - 1, g1, b0 - 1)]
             + arr[Idx(r1, g0 - 1, b0 - 1)]
             - arr[Idx(r0 - 1, g0 - 1, b0 - 1)];
    }

    // --- Cut-point search ---

    /// <summary>
    /// For <paramref name="box"/>, try every split along axis
    /// <paramref name="dir"/> (0=R, 1=G, 2=B) and return the one that
    /// maximises the between-class variance score.
    /// </summary>
    private float FindCut(Box box, int dir, out int cutPos)
    {
        long wWhole = Vol(_wt, box);
        long rWhole = Vol(_mr, box);
        long gWhole = Vol(_mg, box);
        long bWhole = Vol(_mb, box);

        float maxScore = 0;
        cutPos = -1;

        switch (dir)
        {
            case 0: // R
                for (int r = box.R0; r < box.R1; r++)
                {
                    long wBot = Vol(_wt, box.R0, r, box.G0, box.G1, box.B0, box.B1);
                    if (wBot == 0) continue;
                    long wTop = wWhole - wBot;
                    if (wTop == 0) continue;

                    long rBot = Vol(_mr, box.R0, r, box.G0, box.G1, box.B0, box.B1);
                    long gBot = Vol(_mg, box.R0, r, box.G0, box.G1, box.B0, box.B1);
                    long bBot = Vol(_mb, box.R0, r, box.G0, box.G1, box.B0, box.B1);

                    float score = BetweenScore(
                        rBot, gBot, bBot, wBot,
                        rWhole - rBot, gWhole - gBot, bWhole - bBot, wTop);

                    if (score > maxScore) { maxScore = score; cutPos = r; }
                }
                break;

            case 1: // G
                for (int g = box.G0; g < box.G1; g++)
                {
                    long wBot = Vol(_wt, box.R0, box.R1, box.G0, g, box.B0, box.B1);
                    if (wBot == 0) continue;
                    long wTop = wWhole - wBot;
                    if (wTop == 0) continue;

                    long rBot = Vol(_mr, box.R0, box.R1, box.G0, g, box.B0, box.B1);
                    long gBot = Vol(_mg, box.R0, box.R1, box.G0, g, box.B0, box.B1);
                    long bBot = Vol(_mb, box.R0, box.R1, box.G0, g, box.B0, box.B1);

                    float score = BetweenScore(
                        rBot, gBot, bBot, wBot,
                        rWhole - rBot, gWhole - gBot, bWhole - bBot, wTop);

                    if (score > maxScore) { maxScore = score; cutPos = g; }
                }
                break;

            case 2: // B
                for (int bv = box.B0; bv < box.B1; bv++)
                {
                    long wBot = Vol(_wt, box.R0, box.R1, box.G0, box.G1, box.B0, bv);
                    if (wBot == 0) continue;
                    long wTop = wWhole - wBot;
                    if (wTop == 0) continue;

                    long rBot = Vol(_mr, box.R0, box.R1, box.G0, box.G1, box.B0, bv);
                    long gBot = Vol(_mg, box.R0, box.R1, box.G0, box.G1, box.B0, bv);
                    long bBot = Vol(_mb, box.R0, box.R1, box.G0, box.G1, box.B0, bv);

                    float score = BetweenScore(
                        rBot, gBot, bBot, wBot,
                        rWhole - rBot, gWhole - gBot, bWhole - bBot, wTop);

                    if (score > maxScore) { maxScore = score; cutPos = bv; }
                }
                break;
        }

        return maxScore;
    }

    /// <summary>
    /// Between-class variance score:  Σ (moment² / weight) for both halves.
    /// Maximising this minimises total within-class variance.
    /// </summary>
    private static float BetweenScore(
        long rBot, long gBot, long bBot, long wBot,
        long rTop, long gTop, long bTop, long wTop)
    {
        return (float)(
            ((double)rBot * rBot + (double)gBot * gBot + (double)bBot * bBot) / wBot
          + ((double)rTop * rTop + (double)gTop * gTop + (double)bTop * bTop) / wTop);
    }
}
