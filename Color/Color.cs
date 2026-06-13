// Port of Go's image/color package.
// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

namespace GoImage.Color;

/// <summary>
/// IColor can convert itself to alpha-premultiplied 16-bits per channel RGBA.
/// The conversion may be lossy.
/// </summary>
public interface IColor
{
    /// <summary>
    /// RGBA returns the alpha-premultiplied red, green, blue and alpha values
    /// for the color. Each value ranges within [0, 0xffff].
    /// </summary>
    (uint r, uint g, uint b, uint a) GetRGBA();
}

/// <summary>
/// RGBA represents a traditional 32-bit alpha-premultiplied color, having 8
/// bits for each of red, green, blue and alpha.
/// </summary>
public struct RGBA : IColor
{
    public byte R, G, B, A;

    public RGBA(byte r, byte g, byte b, byte a)
    {
        R = r; G = g; B = b; A = a;
    }

    public (uint r, uint g, uint b, uint a) GetRGBA()
    {
        uint r = R; r |= r << 8;
        uint g = G; g |= g << 8;
        uint b = B; b |= b << 8;
        uint a = A; a |= a << 8;
        return (r, g, b, a);
    }
}

/// <summary>
/// RGBA64 represents a 64-bit alpha-premultiplied color, having 16 bits for
/// each of red, green, blue and alpha.
/// </summary>
public struct RGBA64 : IColor
{
    public ushort R, G, B, A;

    public RGBA64(ushort r, ushort g, ushort b, ushort a)
    {
        R = r; G = g; B = b; A = a;
    }

    public (uint r, uint g, uint b, uint a) GetRGBA()
    {
        return (R, G, B, A);
    }
}

/// <summary>
/// NRGBA represents a non-alpha-premultiplied 32-bit color.
/// </summary>
public struct NRGBA : IColor
{
    public byte R, G, B, A;

    public NRGBA(byte r, byte g, byte b, byte a)
    {
        R = r; G = g; B = b; A = a;
    }

    public (uint r, uint g, uint b, uint a) GetRGBA()
    {
        uint r = R; r |= r << 8;
        r *= A; r /= 0xff;
        uint g = G; g |= g << 8;
        g *= A; g /= 0xff;
        uint b = B; b |= b << 8;
        b *= A; b /= 0xff;
        uint a = A; a |= a << 8;
        return (r, g, b, a);
    }
}

/// <summary>
/// NRGBA64 represents a non-alpha-premultiplied 64-bit color.
/// </summary>
public struct NRGBA64 : IColor
{
    public ushort R, G, B, A;

    public NRGBA64(ushort r, ushort g, ushort b, ushort a)
    {
        R = r; G = g; B = b; A = a;
    }

    public (uint r, uint g, uint b, uint a) GetRGBA()
    {
        uint r = R; r *= A; r /= 0xffff;
        uint g = G; g *= A; g /= 0xffff;
        uint b = B; b *= A; b /= 0xffff;
        uint a = A;
        return (r, g, b, a);
    }
}

/// <summary>
/// Alpha represents an 8-bit alpha color.
/// </summary>
public struct Alpha : IColor
{
    public byte A;

    public Alpha(byte a) { A = a; }

    public (uint r, uint g, uint b, uint a) GetRGBA()
    {
        uint a = A; a |= a << 8;
        return (a, a, a, a);
    }
}

/// <summary>
/// Alpha16 represents a 16-bit alpha color.
/// </summary>
public struct Alpha16 : IColor
{
    public ushort A;

    public Alpha16(ushort a) { A = a; }

    public (uint r, uint g, uint b, uint a) GetRGBA()
    {
        uint a = A;
        return (a, a, a, a);
    }
}

/// <summary>
/// Gray represents an 8-bit grayscale color.
/// </summary>
public struct Gray : IColor
{
    public byte Y;

    public Gray(byte y) { Y = y; }

    public (uint r, uint g, uint b, uint a) GetRGBA()
    {
        uint y = Y; y |= y << 8;
        return (y, y, y, 0xffff);
    }
}

/// <summary>
/// Gray16 represents a 16-bit grayscale color.
/// </summary>
public struct Gray16 : IColor
{
    public ushort Y;

    public Gray16(ushort y) { Y = y; }

    public (uint r, uint g, uint b, uint a) GetRGBA()
    {
        uint y = Y;
        return (y, y, y, 0xffff);
    }
}

/// <summary>
/// sqDiff returns the squared-difference of x and y, shifted by 2.
/// </summary>
internal static class ColorUtil
{
    public static uint SqDiff(uint x, uint y)
    {
        uint d = x - y;
        return (d * d) >> 2;
    }
}

/// <summary>
/// Standard colors.
/// </summary>
public static class StandardColors
{
    public static readonly Gray16 Black = new Gray16(0);
    public static readonly Gray16 White = new Gray16(0xffff);
    public static readonly Alpha16 Transparent = new Alpha16(0);
    public static readonly Alpha16 Opaque = new Alpha16(0xffff);
}
