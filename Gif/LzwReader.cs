// Port of Go's compress/lzw/reader.go (GIF/LSB variant).
// Copyright 2009 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

namespace GoImage.Gif;

/// <summary>
/// LZW reader for GIF-format compressed data (LSB bit order, up to 12-bit codes).
/// Port of Go's compress/lzw Reader.
/// </summary>
internal class LzwReader
{
    private const int MaxWidth = 12;
    private const ushort InvalidCode = 0xffff;
    private const int FlushBuffer = 1 << MaxWidth;

    private readonly System.IO.Stream _src;
    private uint _bits;
    private int _nBits;
    private int _width;

    private readonly int _litWidth;
    private ushort _clear, _eof, _hi, _overflow, _last;

    private readonly byte[] _suffix = new byte[1 << MaxWidth];
    private readonly ushort[] _prefix = new ushort[1 << MaxWidth];

    // output buffer: 2 * 1<<maxWidth bytes for right-to-left decoding
    private readonly byte[] _output = new byte[2 * 1 << MaxWidth];
    private int _o;
    private byte[]? _toRead;
    private int _toReadOffset;
    private int _toReadLength;

    private bool _eofReached;
    private System.IO.IOException? _error;

    public LzwReader(System.IO.Stream src, int litWidth)
    {
        if (litWidth < 2 || litWidth > 8)
            throw new System.ArgumentException($"lzw: litWidth {litWidth} out of range [2,8]");

        _src = src;
        _litWidth = litWidth;
        _width = 1 + litWidth;
        _clear = (ushort)(1u << litWidth);
        _eof = (ushort)(_clear + 1);
        _hi = _eof;
        _overflow = (ushort)(1u << _width);
        _last = InvalidCode;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        int totalRead = 0;

        while (totalRead < count)
        {
            // Drain pending output from previous decode
            if (_toRead != null)
            {
                int remaining = _toReadLength - _toReadOffset;
                int toCopy = System.Math.Min(remaining, count - totalRead);
                System.Array.Copy(_toRead, _toReadOffset, buffer, offset + totalRead, toCopy);
                _toReadOffset += toCopy;
                totalRead += toCopy;

                if (_toReadOffset >= _toReadLength)
                {
                    _toRead = null;
                    _toReadOffset = _toReadLength = 0;
                }

                if (totalRead >= count)
                    return totalRead;
            }

            if (_eofReached)
                return totalRead;

            if (_error != null)
                throw _error;

            Decode();
        }

        return totalRead;
    }

    private void Decode()
    {
        while (true)
        {
            ushort code;
            try
            {
                code = ReadLSB();
            }
            catch (System.IO.EndOfStreamException)
            {
                _error = new System.IO.IOException("lzw: unexpected EOF",
                    new System.IO.EndOfStreamException());
                break;
            }

            if (code < _clear)
            {
                // Literal code: output it directly
                _output[_o] = (byte)code;
                _o++;

                if (_last != InvalidCode)
                {
                    // Save what the hi code expands to
                    _suffix[_hi] = (byte)code;
                    _prefix[_hi] = _last;
                }
            }
            else if (code == _clear)
            {
                _width = 1 + _litWidth;
                _hi = _eof;
                _overflow = (ushort)(1u << _width);
                _last = InvalidCode;
                continue;
            }
            else if (code == _eof)
            {
                _eofReached = true;
                break;
            }
            else if (code <= _hi)
            {
                // Decode a non-literal code by walking the prefix chain
                int i = _output.Length - 1;
                ushort c = code;

                if (code == _hi && _last != InvalidCode)
                {
                    // code == hi special case: last expansion followed by head of last expansion
                    c = _last;
                    while (c >= _clear)
                        c = _prefix[c];
                    _output[i] = (byte)c;
                    i--;
                    c = _last;
                }

                // Walk prefix chain, writing suffixes from right to left
                while (c >= _clear)
                {
                    _output[i] = _suffix[c];
                    i--;
                    c = _prefix[c];
                }
                _output[i] = (byte)c;

                // Copy decoded sequence to output
                int decodedLen = _output.Length - i;
                System.Array.Copy(_output, i, _output, _o, decodedLen);
                _o += decodedLen;

                if (_last != InvalidCode)
                {
                    // Save what the hi code expands to
                    _suffix[_hi] = (byte)c;
                    _prefix[_hi] = _last;
                }
            }
            else
            {
                _error = new System.IO.IOException("lzw: invalid code");
                break;
            }

            _last = code;
            _hi++;

            if (_hi >= _overflow)
            {
                if (_width == MaxWidth)
                {
                    _last = InvalidCode;
                    _hi--; // undo the increment; maintain hi < overflow invariant
                }
                else
                {
                    _width++;
                    _overflow = (ushort)(1u << _width);
                }
            }

            if (_o >= FlushBuffer)
                break;
        }

        // Flush pending output
        if (_o > 0)
        {
            _toRead = _output;
            _toReadOffset = 0;
            _toReadLength = _o;
            _o = 0;
        }
    }

    /// <summary>
    /// Read one LSB code from the underlying stream.
    /// </summary>
    private ushort ReadLSB()
    {
        while (_nBits < _width)
        {
            int b = _src.ReadByte();
            if (b < 0)
                throw new System.IO.EndOfStreamException();
            _bits |= (uint)((byte)b) << _nBits;
            _nBits += 8;
        }

        ushort code = (ushort)(_bits & ((1u << _width) - 1));
        _bits >>= _width;
        _nBits -= _width;
        return code;
    }

    public void Close()
    {
        _eofReached = true;
        _error = new System.IO.IOException("lzw: reader closed");
    }
}
