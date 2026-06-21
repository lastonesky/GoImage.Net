// Port of Go's image/png/writer.go (PNG image encoder).
// Copyright 2009 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using GoImage.Color;
using GoImage.Image;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace GoImage.Png;

public static class PngWriter
{
    private static readonly byte[] pngSignature = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };

    public static void Encode(Stream w, IImage img)
    {
        w.Write(pngSignature);

        var b = img.Bounds();
        int width = b.Dx();
        int height = b.Dy();

        // Write IHDR
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = 8; // Bit depth
        ihdr[9] = 6; // Color type (RGBA)
        ihdr[10] = 0; // Compression
        ihdr[11] = 0; // Filter
        ihdr[12] = 0; // Interlace
        WriteChunk(w, "IHDR", ihdr);

        // Write IDAT
        using (var ms = new MemoryStream())
        {
            using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, true))
            {
                byte[] row = new byte[width * 4 + 1];
                for (int y = b.Min.Y; y < b.Max.Y; y++)
                {
                    row[0] = 0; // Filter type: None
                    for (int x = b.Min.X; x < b.Max.X; x++)
                    {
                        var c = (Color.RGBA)ColorModels.RGBAModel.Convert(img.At(x, y));
                        int off = 1 + (x - b.Min.X) * 4;
                        row[off] = c.R;
                        row[off + 1] = c.G;
                        row[off + 2] = c.B;
                        row[off + 3] = c.A;
                    }
                    zlib.Write(row);
                }
            }
            WriteChunk(w, "IDAT", ms.ToArray());
        }

        // Write IEND
        WriteChunk(w, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream w, string type, byte[] data)
    {
        byte[] lenBuf = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lenBuf, data.Length);
        w.Write(lenBuf);

        byte[] typeBuf = System.Text.Encoding.ASCII.GetBytes(type);
        w.Write(typeBuf);

        w.Write(data);

        // CRC
        uint crc = Crc32(typeBuf, data);
        byte[] crcBuf = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBuf, crc);
        w.Write(crcBuf);
    }

    private static readonly uint[] crcTable = GenerateCrcTable();

    private static uint[] GenerateCrcTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int j = 0; j < 8; j++)
            {
                if ((c & 1) != 0) c = 0xedb88320 ^ (c >> 1);
                else c >>= 1;
            }
            table[i] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xffffffff;
        foreach (byte b in type) crc = crcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
        foreach (byte b in data) crc = crcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
        return crc ^ 0xffffffff;
    }
}
