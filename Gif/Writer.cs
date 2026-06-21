// Port of Go's image/gif/writer.go (GIF image encoder).
// Copyright 2013 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using GoImage.Color;
using GoImage.Image;
using GoImage.Draw;
using System.Buffers.Binary;

namespace GoImage.Gif;

public static class GifWriter
{
    public static void Encode(Stream w, IImage img)
    {
        var b = img.Bounds();
        int width = b.Dx();
        int height = b.Dy();

        // Header
        w.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));

        // Logical Screen Descriptor
        byte[] lsd = new byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(lsd.AsSpan(0, 2), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(lsd.AsSpan(2, 2), (ushort)height);
        
        // Use a simple global palette if possible. For simplicity, we'll generate one.
        var paletted = Paletted.NewPaletted(b, Palette.Plan9); // Simple default palette
        Drawer.Draw(paletted, b, img, b.Min, Op.Src);
        
        lsd[4] = 0x80 | 7; // Has global palette, 256 colors
        lsd[5] = 0; // Background color index
        lsd[6] = 0; // Pixel aspect ratio
        w.Write(lsd);

        // Global Palette
        foreach (IColor c in Palette.Plan9)
        {
            var (r, g, b1, _) = c.GetRGBA();
            w.WriteByte((byte)(r >> 8));
            w.WriteByte((byte)(g >> 8));
            w.WriteByte((byte)(b1 >> 8));
        }

        // Image Descriptor
        w.WriteByte(0x2C);
        byte[] id = new byte[9];
        BinaryPrimitives.WriteUInt16LittleEndian(id.AsSpan(0, 2), (ushort)b.Min.X);
        BinaryPrimitives.WriteUInt16LittleEndian(id.AsSpan(2, 2), (ushort)b.Min.Y);
        BinaryPrimitives.WriteUInt16LittleEndian(id.AsSpan(4, 2), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(id.AsSpan(6, 2), (ushort)height);
        id[8] = 0; // No local palette
        w.Write(id);

        // Image Data (LZW)
        WriteLZWData(w, paletted);

        // Trailer
        w.WriteByte(0x3B);
    }

    private static void WriteLZWData(Stream w, Paletted p)
    {
        int litWidth = 8;
        w.WriteByte((byte)litWidth);
        var encoder = new LzwEncoder(litWidth);
        encoder.Encode(w, p.Pix);
    }

    private class LzwEncoder
    {
        private readonly int _litWidth;
        private readonly int _clearCode;
        private readonly int _eofCode;
        private int _codeSize;
        private int _nextCode;
        private int _maxCode;

        private readonly int[] _hashTable = new int[5003]; // Prime size for hashing

        public LzwEncoder(int litWidth)
        {
            _litWidth = litWidth;
            _clearCode = 1 << litWidth;
            _eofCode = _clearCode + 1;
            Reset();
        }

        private void Reset()
        {
            _codeSize = _litWidth + 1;
            _nextCode = _eofCode + 1;
            _maxCode = (1 << _codeSize);
            Array.Fill(_hashTable, -1);
        }

        public void Encode(Stream w, byte[] data)
        {
            using var bw = new BitWriter(w);
            bw.WriteBits(_clearCode, _codeSize);

            if (data.Length > 0)
            {
                int ent = data[0];
                for (int i = 1; i < data.Length; i++)
                {
                    int c = data[i];
                    int key = (ent << 8) | c;
                    int hash = (key % 5003);
                    int offset = 1;

                    int nextEnt = -1;
                    while (_hashTable[hash] != -1)
                    {
                        if ((_hashTable[hash] >> 12) == key)
                        {
                            nextEnt = _hashTable[hash] & 0xFFF;
                            break;
                        }
                        hash = (hash + offset) % 5003;
                    }

                    if (nextEnt != -1)
                    {
                        ent = nextEnt;
                    }
                    else
                    {
                        bw.WriteBits(ent, _codeSize);
                        
                        if (_nextCode < 4096)
                        {
                            _hashTable[hash] = (key << 12) | _nextCode;
                            _nextCode++;
                            
                            if (_nextCode > _maxCode && _codeSize < 12)
                            {
                                _codeSize++;
                                _maxCode = (1 << _codeSize);
                            }
                        }
                        
                        if (_nextCode == 4096)
                        {
                            bw.WriteBits(_clearCode, _codeSize);
                            Reset();
                        }
                        
                        ent = c;
                    }
                }
                bw.WriteBits(ent, _codeSize);
            }

            bw.WriteBits(_eofCode, _codeSize);
            bw.Flush();
        }
    }

    private class BitWriter : IDisposable
    {
        private readonly Stream _w;
        private readonly byte[] _block = new byte[256];
        private int _blockLen = 0;
        private ulong _bits = 0;
        private int _nBits = 0;

        public BitWriter(Stream w) => _w = w;

        public void WriteBits(int bits, int size)
        {
            _bits |= (ulong)bits << _nBits;
            _nBits += size;
            while (_nBits >= 8)
            {
                WriteByte((byte)_bits);
                _bits >>= 8;
                _nBits -= 8;
            }
        }

        private void WriteByte(byte b)
        {
            _block[_blockLen++] = b;
            if (_blockLen == 255) FlushBlock();
        }

        private void FlushBlock()
        {
            if (_blockLen > 0)
            {
                _w.WriteByte((byte)_blockLen);
                _w.Write(_block, 0, _blockLen);
                _blockLen = 0;
            }
        }

        public void Flush()
        {
            if (_nBits > 0)
            {
                WriteByte((byte)_bits);
            }
            FlushBlock();
            _w.WriteByte(0); // Terminating block
        }

        public void Dispose() { }
    }
}
