// Port of Go's image/format.go (image format registration and decode dispatch).

using System.Collections.Concurrent;

namespace GoImage.Image;

/// <summary>
/// ErrFormat indicates that decoding encountered an unknown format.
/// </summary>
public class ErrFormatException : Exception
{
    public ErrFormatException() : base("image: unknown format") { }
}

/// <summary>
/// A format holds an image format's name, magic header and how to decode it.
/// </summary>
internal struct ImageFormat
{
    public string Name;
    public byte[] Magic;
    public Func<Stream, IImage?> Decode;
    public Func<Stream, Config> DecodeConfig;
}

/// <summary>
/// RegisterFormat registers an image format for use by Decode.
/// </summary>
public static class ImageRegistry
{
    private static readonly object _lock = new();
    private static readonly List<ImageFormat> _formats = new();

    public static void RegisterFormat(string name, string magic,
        Func<Stream, IImage?> decode, Func<Stream, Config> decodeConfig)
    {
        lock (_lock)
        {
            _formats.Add(new ImageFormat
            {
                Name = name,
                Magic = System.Text.Encoding.Latin1.GetBytes(magic),
                Decode = decode,
                DecodeConfig = decodeConfig
            });
        }
    }

    /// <summary>
    /// Decode decodes an image that has been encoded in a registered format.
    /// </summary>
    public static (IImage? image, string formatName) Decode(Stream r)
    {
        var (f, reader) = Sniff(r);
        if (f.Decode == null!) throw new ErrFormatException();
        var m = f.Decode(reader);
        return (m, f.Name);
    }

    /// <summary>
    /// DecodeConfig decodes the color model and dimensions of an image.
    /// </summary>
    public static (Config config, string formatName) DecodeConfig(Stream r)
    {
        var (f, reader) = Sniff(r);
        if (f.DecodeConfig == null!) throw new ErrFormatException();
        var c = f.DecodeConfig(reader);
        return (c, f.Name);
    }

    private static (ImageFormat, Stream) Sniff(Stream r)
    {
        // If the stream supports seeking/peeking, use it; otherwise wrap with a buffered stream
        Stream reader = r;
        if (!r.CanSeek)
        {
            reader = new BufferedStream(r, 4096);
        }

        lock (_lock)
        {
            foreach (var f in _formats)
            {
                var magicLen = f.Magic.Length;
                var buf = new byte[magicLen];

                long pos = reader.Position;
                int n = reader.Read(buf, 0, magicLen);
                reader.Position = pos;

                if (n == magicLen && Match(f.Magic, buf))
                    return (f, reader);
            }
        }
        throw new ErrFormatException();
    }

    private static bool Match(byte[] magic, byte[] data)
    {
        if (magic.Length != data.Length) return false;
        for (int i = 0; i < magic.Length; i++)
        {
            // '?' wildcard matches any byte
            if (magic[i] != (byte)'?' && magic[i] != data[i]) return false;
        }
        return true;
    }
}
