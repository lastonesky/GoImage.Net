// Port of Go's image/image.go core interfaces and Config.

using GoImage.Color;

namespace GoImage.Image;

/// <summary>
/// IImage is a finite rectangular grid of IColor values taken from a color model.
/// </summary>
public interface IImage
{
    IModel ColorModel();
    Rectangle Bounds();
    IColor At(int x, int y);
}

/// <summary>
/// IImageT is a generic interface for images with a specific pixel type.
/// </summary>
public interface IImage<TPixel> : IImage where TPixel : unmanaged, IColor
{
    TPixel this[int x, int y] { get; set; }
    Span<TPixel> GetRowSpan(int y);
}

/// <summary>
/// IImage64 is an IImage whose pixels can be converted directly to a RGBA64.
/// </summary>
public interface IImage64 : IImage
{
    RGBA64 RGBA64At(int x, int y);
}

/// <summary>
/// IDrawImage is an IImage with a Set method to change a single pixel.
/// </summary>
public interface IDrawImage : IImage
{
    void Set(int x, int y, IColor c);
}

/// <summary>
/// Config holds an image's color model and dimensions.
/// </summary>
public struct Config
{
    public IModel ColorModel;
    public int Width, Height;
}
