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
/// IImage64 is an IImage whose pixels can be converted directly to a RGBA64.
/// </summary>
public interface IImage64 : IImage
{
    RGBA64 RGBA64At(int x, int y);
}

/// <summary>
/// Config holds an image's color model and dimensions.
/// </summary>
public struct Config
{
    public IModel ColorModel;
    public int Width, Height;
}
