// Port of Go's image/jpeg/scan.go.

using GoImage.Image;

namespace GoImage.Jpeg;

public partial class Decoder
{
    /// <summary>
    /// makeImg allocates and initializes the destination image.
    /// </summary>
    internal void MakeImg(int mxx, int myy)
    {
        if (_nComp == 1)
        {
            var m = GoImage.Image.Gray.NewGray(Rect.New(0, 0, 8 * mxx, 8 * myy));
            // Match Go: SubImage just adjusts the Rect, shares the buffer.
            m.Rect = Rect.New(0, 0, _width, _height);
            _img1 = m;
            return;
        }

        // Determine subsample ratio
        var subsampleRatio = YCbCrSubsampleRatio.YCbCrSubsampleRatio444;
        if (_comp[1].h != _comp[2].h || _comp[1].v != _comp[2].v ||
            _maxH != _comp[0].h || _maxV != _comp[0].v)
        {
            _flex = true;
        }
        else
        {
            int hRatio = _maxH / _comp[1].h;
            int vRatio = _maxV / _comp[1].v;
            switch ((hRatio << 4) | vRatio)
            {
                case 0x11: subsampleRatio = YCbCrSubsampleRatio.YCbCrSubsampleRatio444; break;
                case 0x12: subsampleRatio = YCbCrSubsampleRatio.YCbCrSubsampleRatio440; break;
                case 0x21: subsampleRatio = YCbCrSubsampleRatio.YCbCrSubsampleRatio422; break;
                case 0x22: subsampleRatio = YCbCrSubsampleRatio.YCbCrSubsampleRatio420; break;
                case 0x41: subsampleRatio = YCbCrSubsampleRatio.YCbCrSubsampleRatio411; break;
                case 0x42: subsampleRatio = YCbCrSubsampleRatio.YCbCrSubsampleRatio410; break;
                default: _flex = true; break;
            }
        }

        var m3 = GoImage.Image.YCbCr.NewYCbCr(
            Rect.New(0, 0, 8 * _maxH * mxx, 8 * _maxV * myy), subsampleRatio);
        // Match Go: SubImage just adjusts the Rect, shares the buffer.
        m3.Rect = Rect.New(0, 0, _width, _height);
        _img3 = m3;

        if (_nComp == 4)
        {
            int h3 = _comp[3].h, v3 = _comp[3].v;
            _blackPix = new byte[8 * h3 * mxx * 8 * v3 * myy];
            _blackStride = 8 * h3 * mxx;
        }
    }

    /// <summary>

    /// <summary>
    /// processSOS processes a Start Of Scan marker (section B.2.3).
    /// </summary>
    internal void ProcessSOS(int n)
    {
        if (_nComp == 0) throw new FormatErrorException("missing SOF marker");
        if (n < 6 || 4 + 2 * _nComp < n || n % 2 != 0)
            throw new FormatErrorException("SOS has wrong length");
        ReadFull(_tmp, 0, n);
        int nComp = _tmp[0];
        if (n != 4 + 2 * nComp)
            throw new FormatErrorException("SOS length inconsistent with number of components");

        var scanCompIndex = new byte[Const.maxComponents];
        var scanTd = new byte[Const.maxComponents];
        var scanTa = new byte[Const.maxComponents];
        int totalHV = 0;

        for (int i = 0; i < nComp; i++)
        {
            byte cs = _tmp[1 + 2 * i];
            int compIndex = -1;
            for (int j = 0; j < _nComp; j++)
            {
                if (cs == _comp[j].c) compIndex = j;
            }
            if (compIndex < 0) throw new FormatErrorException("unknown component selector");
            scanCompIndex[i] = (byte)compIndex;
            for (int j = 0; j < i; j++)
            {
                if (scanCompIndex[i] == scanCompIndex[j])
                    throw new FormatErrorException("repeated component selector");
            }
            totalHV += _comp[compIndex].h * _comp[compIndex].v;
            scanTd[i] = (byte)(_tmp[2 + 2 * i] >> 4);
            if (scanTd[i] > Const.maxTh || (_baseline && scanTd[i] > 1))
                throw new FormatErrorException("bad Td value");
            scanTa[i] = (byte)(_tmp[2 + 2 * i] & 0x0f);
            if (scanTa[i] > Const.maxTh || (_baseline && scanTa[i] > 1))
                throw new FormatErrorException("bad Ta value");
        }

        if (_nComp > 1 && totalHV > 10)
            throw new FormatErrorException("total sampling factors too large");

        int zigStart = 0, zigEnd = Block.BlockSize - 1;
        uint ah = 0, al = 0;
        if (_progressive)
        {
            zigStart = _tmp[1 + 2 * nComp];
            zigEnd = _tmp[2 + 2 * nComp];
            ah = (uint)(_tmp[3 + 2 * nComp] >> 4);
            al = (uint)(_tmp[3 + 2 * nComp] & 0x0f);
            if ((zigStart == 0 && zigEnd != 0) || zigStart > zigEnd || Block.BlockSize <= zigEnd)
                throw new FormatErrorException("bad spectral selection bounds");
            if (zigStart != 0 && nComp != 1)
                throw new FormatErrorException("progressive AC coefficients for more than one component");
            if (ah != 0 && ah != al + 1)
                throw new FormatErrorException("bad successive approximation values");
        }

        int mxx = (_width + 8 * _maxH - 1) / (8 * _maxH);
        int myy = (_height + 8 * _maxV - 1) / (8 * _maxV);
        if (_img1 == null && _img3 == null) MakeImg(mxx, myy);

        if (_progressive)
        {
            for (int i = 0; i < nComp; i++)
            {
                int ci = scanCompIndex[i];
                if (_progCoeffs![ci] == null)
                    _progCoeffs[ci] = new Block[mxx * myy * _comp[ci].h * _comp[ci].v];
            }
        }

        _bits = default;
        int mcu = 0;
        byte expectedRST = Const.rst0Marker;
        var dc = new int[Const.maxComponents];
        int bx = 0, by = 0, blockCount = 0;

        for (int my = 0; my < myy; my++)
        {
            for (int mx = 0; mx < mxx; mx++)
            {
                for (int i = 0; i < nComp; i++)
                {
                    int compIndex = scanCompIndex[i];
                    int hi = _comp[compIndex].h;
                    int vi = _comp[compIndex].v;
                    for (int j = 0; j < hi * vi; j++)
                    {
                        if (nComp != 1)
                        {
                            bx = hi * mx + j % hi;
                            by = vi * my + j / hi;
                        }
                        else
                        {
                            int q = mxx * hi;
                            bx = blockCount % q;
                            by = blockCount / q;
                            blockCount++;
                            if (bx * 8 >= _width || by * 8 >= _height) continue;
                        }

                        Block b;
                        if (_progressive)
                            b = _progCoeffs![compIndex][by * mxx * hi + bx];
                        else
                            b = new Block();

                        if (ah != 0)
                        {
                            Refine(ref b, ref _huff[Const.acTable, scanTa[i]], zigStart, zigEnd, 1 << (int)al);
                        }
                        else
                        {
                            int zig = zigStart;
                            if (zig == 0)
                            {
                                zig++;
                                byte value = DecodeHuffman(ref _huff[Const.dcTable, scanTd[i]]);
                                if (value > 16) throw new UnsupportedErrorException("excessive DC component");
                                int dcDelta = ReceiveExtend(value);
                                dc[compIndex] += dcDelta;
                                b[0] = dc[compIndex] << (int)al;
                            }

                            if (zig <= zigEnd && _eobRun > 0)
                            {
                                _eobRun--;
                            }
                            else
                            {
                                ref Huffman huff = ref _huff[Const.acTable, scanTa[i]];
                                for (; zig <= zigEnd; zig++)
                                {
                                    byte value = DecodeHuffman(ref huff);
                                    int val0 = value >> 4;
                                    int val1 = value & 0x0f;
                                    if (val1 != 0)
                                    {
                                        zig += val0;
                                        if (zig > zigEnd) break;
                                        int ac = ReceiveExtend((byte)val1);
                                        b[Unzig.Table[zig]] = ac << (int)al;
                                    }
                                    else
                                    {
                                        if (val0 != 0x0f)
                                        {
                                            _eobRun = (ushort)(1 << val0);
                                            if (val0 != 0)
                                            {
                                                uint bits = DecodeBits(val0);
                                                _eobRun |= (ushort)bits;
                                            }
                                            _eobRun--;
                                            break;
                                        }
                                        zig += 0x0f;
                                    }
                                }
                            }
                        }

                        if (_progressive)
                        {
                            _progCoeffs![compIndex][by * mxx * hi + bx] = b;
                            continue;
                        }
                        ReconstructBlock(ref b, bx, by, compIndex);
                    }
                }
                mcu++;
                if (_ri > 0 && mcu % _ri == 0 && mcu < mxx * myy)
                {
                    if (!TryReadRST(expectedRST))
                        FindRST(expectedRST);
                    expectedRST++;
                    if (expectedRST == Const.rst7Marker + 1) expectedRST = Const.rst0Marker;
                    _bits = default;
                    dc = new int[Const.maxComponents];
                    _eobRun = 0;
                }
            }
        }
    }

    private bool TryReadRST(byte expectedRST)
    {
        ReadFull(_tmp, 0, 2);
        return _tmp[0] == 0xff && _tmp[1] == expectedRST;
    }

    /// <summary>
    /// refine decodes a successive approximation refinement block (section G.1.2).
    /// </summary>
    internal void Refine(ref Block b, ref Huffman h, int zigStart, int zigEnd, int delta)
    {
        if (zigStart == 0)
        {
            if (zigEnd != 0) throw new InvalidOperationException("unreachable");
            bool bit = DecodeBit();
            if (bit) b[0] |= delta;
            return;
        }

        int zig = zigStart;
        if (_eobRun == 0)
        {
            for (; zig <= zigEnd; zig++)
            {
                int z = 0;
                byte value = DecodeHuffman(ref h);
                int val0 = value >> 4;
                int val1 = value & 0x0f;

                if (val1 == 0)
                {
                    if (val0 != 0x0f)
                    {
                        _eobRun = (ushort)(1 << val0);
                        if (val0 != 0)
                        {
                            uint bits = DecodeBits(val0);
                            _eobRun |= (ushort)bits;
                        }
                        goto breakLoop;
                    }
                }
                else if (val1 == 1)
                {
                    z = delta;
                    bool bit = DecodeBit();
                    if (!bit) z = -z;
                }
                else
                {
                    throw new FormatErrorException("unexpected Huffman code");
                }

                RefineNonZeroes(ref b, ref zig, zigEnd, val0, delta);
                if (zig > zigEnd) throw new FormatErrorException("too many coefficients");
                if (z != 0) b[Unzig.Table[zig]] = z;
            }
        }
    breakLoop:
        if (_eobRun > 0)
        {
            _eobRun--;
            RefineNonZeroes(ref b, ref zig, zigEnd, -1, delta);
        }
    }

    internal void RefineNonZeroes(ref Block b, ref int zig, int zigEnd, int nz, int delta)
    {
        for (; zig <= zigEnd; zig++)
        {
            int u = Unzig.Table[zig];
            if (b[u] == 0)
            {
                if (nz == 0) break;
                nz--;
                continue;
            }
            bool bit = DecodeBit();
            if (!bit) continue;
            if (b[u] >= 0) b[u] += delta;
            else b[u] -= delta;
        }
    }

    /// <summary>
    /// reconstructProgressiveImage processes all saved progressive coefficients.
    /// </summary>
    internal void ReconstructProgressiveImage()
    {
        int mxx = (_width + 8 * _maxH - 1) / (8 * _maxH);
        for (int i = 0; i < _nComp; i++)
        {
            if (_progCoeffs![i] == null) continue;
            int v = 8 * _maxV / _comp[i].v;
            int h = 8 * _maxH / _comp[i].h;
            int stride = mxx * _comp[i].h;
            for (int byy = 0; byy * v < _height; byy++)
            {
                for (int bxx = 0; bxx * h < _width; bxx++)
                {
                    ReconstructBlock(ref _progCoeffs[i][byy * stride + bxx], bxx, byy, i);
                }
            }
        }
    }

    /// <summary>
    /// reconstructBlock dequantizes, performs IDCT, and stores the block to the image.
    /// </summary>
    internal void ReconstructBlock(ref Block b, int bx, int by, int compIndex)
    {
        ref Block qt = ref _quant[_comp[compIndex].tq];
        var bData = b.Data;
        var qtData = qt.Data;
        for (int zz = 0; zz < Block.BlockSize; zz++)
        {
            bData[Unzig.Table[zz]] *= qtData[zz];
        }
        Dct.Idct(ref b);

        int h = 1, v = 1;
        if (_flex)
        {
            h = _comp[compIndex].expandH;
            v = _comp[compIndex].expandV;
            bx *= h;
            by *= v;
        }

        byte[] dst;
        int stride;
        int channelOff;
        if (_nComp == 1)
        {
            dst = _img1!.Pix;
            stride = _img1.Stride;
            channelOff = 0;
        }
        else
        {
            dst = _img3!.Buffer;
            switch (compIndex)
            {
                case 0: stride = _img3!.YStride; channelOff = _img3!.YOff; break;
                case 1: stride = _img3!.CStride; channelOff = _img3!.CbOff; break;
                case 2: stride = _img3!.CStride; channelOff = _img3!.CrOff; break;
                case 3: dst = _blackPix!; stride = _blackStride; channelOff = 0; break;
                default: throw new UnsupportedErrorException("too many components");
            }
        }

        int dstBase = channelOff + 8 * (by * stride + bx);
        if (_flex)
        {
            for (int y = 0; y < 8; y++)
            {
                int y8 = y * 8, yv = y * v;
                for (int x = 0; x < 8; x++)
                {
                    byte val = (byte)Math.Clamp(bData[y8 + x] + 128, 0, 255);
                    int xh = x * h;
                    for (int yy = 0; yy < v; yy++)
                        for (int xx = 0; xx < h; xx++)
                            dst[dstBase + (yv + yy) * stride + xh + xx] = val;
                }
            }
        }
        else
        {
            for (int y = 0; y < 8; y++)
            {
                int y8 = y * 8, yStride = y * stride;
                for (int x = 0; x < 8; x++)
                    dst[dstBase + yStride + x] = (byte)Math.Clamp(bData[y8 + x] + 128, 0, 255);
            }
        }
    }

    /// <summary>
    /// findRST advances past the next RST restart marker that matches expectedRST.
    /// </summary>
    internal void FindRST(byte expectedRST)
    {
        while (true)
        {
            int i = 0;
            if (_tmp[0] == 0xff)
            {
                if (_tmp[1] == expectedRST) return;
                else if (_tmp[1] == 0xff) i = 1;
                else if (_tmp[1] != 0x00)
                    throw new FormatErrorException("bad RST marker");
            }
            else if (_tmp[1] == 0xff)
            {
                _tmp[0] = 0xff;
                i = 1;
            }
            ReadFull(_tmp, i, 2 - i);
        }
    }
}
