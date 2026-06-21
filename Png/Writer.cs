// Port of Go's image/png/writer.go (PNG image encoder).
// Copyright 2009 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using GoImage.Color;
using GoImage.Image;
using System.Buffers;
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
        // We use a custom stream to wrap the IDAT chunk writing to avoid MemoryStream.ToArray()
        using (var idatWriter = new IdatWriter(w))
        {
            using (var zlib = new ZLibStream(idatWriter, CompressionLevel.Optimal, true))
            {
                int rowSize = width * 4 + 1;
                byte[] row = ArrayPool<byte>.Shared.Rent(rowSize);
                try
                {
                    for (int y = b.Min.Y; y < b.Max.Y; y++)
                    {
                        row[0] = 0; // Filter type: None
                        var rowSpan = row.AsSpan(1, width * 4);

                        if (img is GoImage.Image.RGBA rgbaImg)
                        {
                            var srcRow = rgbaImg.GetRowSpan(y);
                            var dstRow = MemoryMarshal.Cast<byte, Color.RGBA>(rowSpan);
                            srcRow.CopyTo(dstRow);
                        }
                        else
                        {
                            for (int x = b.Min.X; x < b.Max.X; x++)
                            {
                                var c = (Color.RGBA)ColorModels.RGBAModel.Convert(img.At(x, y));
                                int off = (x - b.Min.X) * 4;
                                rowSpan[off] = c.R;
                                rowSpan[off + 1] = c.G;
                                rowSpan[off + 2] = c.B;
                                rowSpan[off + 3] = c.A;
                            }
                        }
                        zlib.Write(row, 0, rowSize);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(row);
                }
            }
        }

        // Write IEND
        WriteChunk(w, "IEND", Array.Empty<byte>());
    }

    private class IdatWriter : Stream
    {
        private readonly Stream _w;
        private readonly byte[] _buffer;
        private int _pos;

        public IdatWriter(Stream w)
        {
            _w = w;
            _buffer = ArrayPool<byte>.Shared.Rent(32768);
            _pos = 0;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int toCopy = Math.Min(count, _buffer.Length - _pos);
                Buffer.BlockCopy(buffer, offset, _buffer, _pos, toCopy);
                _pos += toCopy;
                offset += toCopy;
                count -= toCopy;

                if (_pos == _buffer.Length) FlushBuffer();
            }
        }

        private void FlushBuffer()
        {
            if (_pos > 0)
            {
                WriteChunk(_w, "IDAT", _buffer.AsSpan(0, _pos));
                _pos = 0;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                FlushBuffer();
                ArrayPool<byte>.Shared.Return(_buffer);
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _w.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static void WriteChunk(Stream w, string type, ReadOnlySpan<byte> data)
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

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xffffffff;
        foreach (byte b in type) crc = crcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
        foreach (byte b in data) crc = crcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
        return crc ^ 0xffffffff;
    }
}
