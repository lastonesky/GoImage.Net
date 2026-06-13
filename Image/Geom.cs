// Port of Go's image/geom.go (Point and Rectangle).

using System.Numerics;

namespace GoImage.Image;

/// <summary>
/// A Point is an X, Y coordinate pair. The axes increase right and down.
/// </summary>
public struct Point
{
    public int X, Y;

    public Point(int x, int y) { X = x; Y = y; }

    public override string ToString() => $"({X},{Y})";

    public Point Add(Point q) => new Point(X + q.X, Y + q.Y);
    public Point Sub(Point q) => new Point(X - q.X, Y - q.Y);
    public Point Mul(int k) => new Point(X * k, Y * k);
    public Point Div(int k) => new Point(X / k, Y / k);

    public bool In(Rectangle r) =>
        r.Min.X <= X && X < r.Max.X &&
        r.Min.Y <= Y && Y < r.Max.Y;

    public bool Eq(Point q) => X == q.X && Y == q.Y;

    public static bool operator ==(Point a, Point b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(Point a, Point b) => !(a == b);
    public override bool Equals(object? obj) => obj is Point p && this == p;
    public override int GetHashCode() => X ^ Y;
}

/// <summary>
/// Pt is shorthand for new Point(x, y).
/// </summary>
public static class Pt
{
    public static Point New(int x, int y) => new Point(x, y);
}

/// <summary>
/// A Rectangle contains the points with Min.X &lt;= X &lt; Max.X, Min.Y &lt;= Y &lt; Max.Y.
/// </summary>
public struct Rectangle
{
    public Point Min, Max;

    public Rectangle(Point min, Point max) { Min = min; Max = max; }

    public override string ToString() => $"{Min}-{Max}";

    public int Dx() => Max.X - Min.X;
    public int Dy() => Max.Y - Min.Y;

    public Point Size() => new Point(Max.X - Min.X, Max.Y - Min.Y);

    public Rectangle Add(Point p) => new Rectangle(
        new Point(Min.X + p.X, Min.Y + p.Y),
        new Point(Max.X + p.X, Max.Y + p.Y));

    public Rectangle Sub(Point p) => new Rectangle(
        new Point(Min.X - p.X, Min.Y - p.Y),
        new Point(Max.X - p.X, Max.Y - p.Y));

    public Rectangle Intersect(Rectangle s)
    {
        var r = this;
        if (r.Min.X < s.Min.X) r.Min.X = s.Min.X;
        if (r.Min.Y < s.Min.Y) r.Min.Y = s.Min.Y;
        if (r.Max.X > s.Max.X) r.Max.X = s.Max.X;
        if (r.Max.Y > s.Max.Y) r.Max.Y = s.Max.Y;
        if (r.Empty()) return default;
        return r;
    }

    public Rectangle Union(Rectangle s)
    {
        if (Empty()) return s;
        if (s.Empty()) return this;
        var r = this;
        if (r.Min.X > s.Min.X) r.Min.X = s.Min.X;
        if (r.Min.Y > s.Min.Y) r.Min.Y = s.Min.Y;
        if (r.Max.X < s.Max.X) r.Max.X = s.Max.X;
        if (r.Max.Y < s.Max.Y) r.Max.Y = s.Max.Y;
        return r;
    }

    public bool Empty() => Min.X >= Max.X || Min.Y >= Max.Y;

    public bool Eq(Rectangle s) => this == s || (Empty() && s.Empty());

    public bool Overlaps(Rectangle s) =>
        !Empty() && !s.Empty() &&
        Min.X < s.Max.X && s.Min.X < Max.X &&
        Min.Y < s.Max.Y && s.Min.Y < Max.Y;

    public bool In(Rectangle s)
    {
        if (Empty()) return true;
        return s.Min.X <= Min.X && Max.X <= s.Max.X &&
               s.Min.Y <= Min.Y && Max.Y <= s.Max.Y;
    }

    public static bool operator ==(Rectangle a, Rectangle b) => a.Min == b.Min && a.Max == b.Max;
    public static bool operator !=(Rectangle a, Rectangle b) => !(a == b);
    public override bool Equals(object? obj) => obj is Rectangle r && this == r;
    public override int GetHashCode() => Min.GetHashCode() ^ Max.GetHashCode();
}

/// <summary>
/// Rect is shorthand for Rectangle{Pt(x0, y0), Pt(x1, y1)}.
/// </summary>
public static class Rect
{
    public static Rectangle New(int x0, int y0, int x1, int y1)
    {
        if (x0 > x1) { int t = x0; x0 = x1; x1 = t; }
        if (y0 > y1) { int t = y0; y0 = y1; y1 = t; }
        return new Rectangle(new Point(x0, y0), new Point(x1, y1));
    }
}

/// <summary>
/// Helper functions matching Go's image package.
/// </summary>
public static class ImageMath
{
    /// <summary>
    /// mul3NonNeg returns (x * y * z), unless at least one argument is negative or
    /// if the computation overflows the int type, in which case it returns -1.
    /// </summary>
    public static int Mul3NonNeg(int x, int y, int z)
    {
        if (x < 0 || y < 0 || z < 0) return -1;
        // Use BigInteger to check overflow
        BigInteger result = (BigInteger)x * y * z;
        if (result < 0 || result > int.MaxValue) return -1;
        return (int)result;
    }

    /// <summary>
    /// add2NonNeg returns (x + y), unless at least one argument is negative or if
    /// the computation overflows the int type, in which case it returns -1.
    /// </summary>
    public static int Add2NonNeg(int x, int y)
    {
        if (x < 0 || y < 0) return -1;
        long a = (long)x + y;
        if (a < 0 || a > int.MaxValue) return -1;
        return (int)a;
    }

    /// <summary>
    /// pixelBufferLength returns the length of the byte[] Pix field
    /// for the New* functions.
    /// </summary>
    public static int PixelBufferLength(int bytesPerPixel, Rectangle r, string imageTypeName)
    {
        int totalLength = Mul3NonNeg(bytesPerPixel, r.Dx(), r.Dy());
        if (totalLength < 0)
            throw new InvalidOperationException($"image: New{imageTypeName} Rectangle has huge or negative dimensions");
        return totalLength;
    }
}
