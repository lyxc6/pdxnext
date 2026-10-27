using System.Buffers.Binary;

namespace PdxLauncher.Imaging.Dds;

/// <summary>BC7（DXGI BC7_UNORM/SRGB）4x4 块解码器。</summary>
/// <remarks>
/// 算法与静态表移植自 Rich Geldreich 的 bc7decomp.cpp
/// （https://github.com/richgel999/bc7enc_rdo/blob/master/bc7decomp.cpp，MIT / 公有领域双许可）。
/// 这里是逐模式的字面转写：mode 0/2 三子集、mode 1/3/7 双子集、mode 4/5 双平面、mode 6 单子集。
/// </remarks>
internal static class Bc7Decoder
{
    private const int BlockBytes = 16;
    private const int BlockPixels = 16;
    private const int PixelBytes = 4;

    /// <summary>解码一个 16 字节 BC7 块，输出 16 个 RGBA 像素（64 字节，行优先）。</summary>
    /// <param name="block">16 字节压缩块（小端位流）。</param>
    /// <param name="rgba">至少 64 字节的输出缓冲。</param>
    /// <returns>解码成功返回 true；模式非法时返回 false（输出保持原样）。</returns>
    public static bool DecodeBlock(ReadOnlySpan<byte> block, Span<byte> rgba)
    {
        if (block.Length < BlockBytes || rgba.Length < BlockPixels * PixelBytes)
        {
            return false;
        }

        ulong low = BinaryPrimitives.ReadUInt64LittleEndian(block);
        ulong high = BinaryPrimitives.ReadUInt64LittleEndian(block[8..]);
        int mode = Bc7Tables.FirstByteToMode[block[0]];

        return mode switch
        {
            0 or 2 => DecodeMode02(mode, low, high, rgba),
            1 or 3 or 7 => DecodeMode137(mode, low, high, rgba),
            4 or 5 => DecodeMode45(mode, low, high, rgba),
            6 => DecodeMode6(low, high, rgba),
            _ => false
        };
    }

    private static void InsertWeightZero(ref ulong indexBits, int bitsPerIndex, int offset)
    {
        ulong lowBitMask = (1UL << (bitsPerIndex * (offset + 1) - 1)) - 1UL;
        ulong highBitMask = ~lowBitMask;
        indexBits = ((indexBits & highBitMask) << 1) | (indexBits & lowBitMask);
    }

    private static int Dequant(int value, int pbit, int valueBits)
    {
        int totalBits = valueBits + 1;
        int v = (value << 1) | pbit;
        v <<= 8 - totalBits;
        v |= v >> totalBits;
        return v & 0xFF;
    }

    private static int Dequant(int value, int valueBits)
    {
        int v = value << (8 - valueBits);
        v |= v >> valueBits;
        return v & 0xFF;
    }

    private static int Interp(int low, int high, int weight, int bits)
    {
        return bits switch
        {
            2 => (low * (64 - Bc7Tables.Weights2[weight]) + high * Bc7Tables.Weights2[weight] + 32) >> 6,
            3 => (low * (64 - Bc7Tables.Weights3[weight]) + high * Bc7Tables.Weights3[weight] + 32) >> 6,
            4 => (low * (64 - Bc7Tables.Weights4[weight]) + high * Bc7Tables.Weights4[weight] + 32) >> 6,
            _ => 0
        };
    }

    private static void WritePixel(Span<byte> rgba, int index, ReadOnlySpan<byte> color)
    {
        int offset = index * PixelBytes;
        rgba[offset] = color[0];
        rgba[offset + 1] = color[1];
        rgba[offset + 2] = color[2];
        rgba[offset + 3] = color[3];
    }

    private static bool DecodeMode02(int mode, ulong low, ulong high, Span<byte> rgba)
    {
        const int endpoints = 6;
        const int comps = 3;

        int weightBits = mode == 0 ? 3 : 2;
        int weightMask = (1 << weightBits) - 1;
        int endpointBits = mode == 0 ? 4 : 5;
        int endpointMask = (1 << endpointBits) - 1;
        int pbits = mode == 0 ? 6 : 0;
        int weightValues = 1 << weightBits;
        int partBits = mode == 0 ? 4 : 6;
        int partMask = (1 << partBits) - 1;

        int part = (int)((low >> (mode + 1)) & (ulong)partMask);

        Span<ulong> channels = stackalloc ulong[3];
        if (mode == 0)
        {
            channels[0] = low >> 5;
            channels[1] = low >> 29;
            channels[2] = (low >> 53) | (high << 11);
        }
        else
        {
            channels[0] = low >> 9;
            channels[1] = (low >> 39) | (high << 25);
            channels[2] = high >> 5;
        }

        int[,] endpointColors = new int[endpoints, PixelBytes];
        for (int c = 0; c < comps; c++)
        {
            ulong chunk = channels[c];
            for (int e = 0; e < endpoints; e++)
            {
                endpointColors[e, c] = (int)(chunk & (ulong)endpointMask);
                chunk >>= endpointBits;
            }
        }

        Span<int> pbitValues = stackalloc int[6];
        if (mode == 0)
        {
            int pbitChunk = (int)((high >> 13) & 0xFF);
            for (int p = 0; p < pbits; p++)
            {
                pbitValues[p] = (pbitChunk >> p) & 1;
            }
        }

        ulong weightChunk = high >> (67 - 16 * weightBits);
        InsertWeightZero(ref weightChunk, weightBits, 0);
        int anchor1 = Bc7Tables.AnchorIndexThirdSubset1[part];
        int anchor2 = Bc7Tables.AnchorIndexThirdSubset2[part];
        InsertWeightZero(ref weightChunk, weightBits, Math.Min(anchor1, anchor2));
        InsertWeightZero(ref weightChunk, weightBits, Math.Max(anchor1, anchor2));

        Span<int> weights = stackalloc int[BlockPixels];
        for (int i = 0; i < BlockPixels; i++)
        {
            weights[i] = (int)(weightChunk & (ulong)weightMask);
            weightChunk >>= weightBits;
        }

        for (int e = 0; e < endpoints; e++)
        {
            for (int c = 0; c < PixelBytes; c++)
            {
                endpointColors[e, c] = c == 3
                    ? 255
                    : pbits > 0
                        ? Dequant(endpointColors[e, c], pbitValues[e], endpointBits)
                        : Dequant(endpointColors[e, c], endpointBits);
            }
        }

        int blockColorsCount = 3 * 8;
        Span<byte> blockColors = stackalloc byte[blockColorsCount * PixelBytes];
        for (int s = 0; s < 3; s++)
        {
            for (int i = 0; i < weightValues; i++)
            {
                int offset = (s * 8 + i) * PixelBytes;
                for (int c = 0; c < 3; c++)
                {
                    blockColors[offset + c] = (byte)Interp(endpointColors[s * 2, c], endpointColors[s * 2 + 1, c], i, weightBits);
                }

                blockColors[offset + 3] = 255;
            }
        }

        Span<byte> pixel = stackalloc byte[PixelBytes];
        for (int i = 0; i < BlockPixels; i++)
        {
            int subset = Bc7Tables.Partition3[part * 16 + i];
            int offset = (subset * 8 + weights[i]) * PixelBytes;
            pixel[0] = blockColors[offset];
            pixel[1] = blockColors[offset + 1];
            pixel[2] = blockColors[offset + 2];
            pixel[3] = blockColors[offset + 3];
            WritePixel(rgba, i, pixel);
        }

        return true;
    }

    private static bool DecodeMode137(int mode, ulong low, ulong high, Span<byte> rgba)
    {
        const int endpoints = 4;

        int comps = mode == 7 ? 4 : 3;
        int weightBits = mode == 1 ? 3 : 2;
        int weightMask = (1 << weightBits) - 1;
        int endpointBits = mode == 7 ? 5 : mode == 1 ? 6 : 7;
        int endpointMask = (1 << endpointBits) - 1;
        int pbits = mode == 1 ? 2 : 4;
        bool sharedPbits = mode == 1;
        int weightValues = 1 << weightBits;

        int part = (int)((low >> (mode + 1)) & 0x3F);

        Span<ulong> channels = stackalloc ulong[4];
        ulong pbitChunk;
        ulong weightChunk;

        channels[0] = low >> (mode + 7);
        switch (mode)
        {
            case 1:
                channels[1] = low >> 32;
                channels[2] = (low >> 56) | (high << 8);
                pbitChunk = high >> 16;
                weightChunk = high >> 18;
                break;
            case 3:
                channels[1] = (low >> 38) | (high << 26);
                channels[2] = high >> 2;
                pbitChunk = high >> 30;
                weightChunk = high >> 34;
                break;
            case 7:
                channels[1] = low >> 34;
                channels[2] = (low >> 54) | (high << 10);
                channels[3] = high >> 10;
                pbitChunk = high >> 30;
                weightChunk = high >> 34;
                break;
            default:
                return false;
        }

        int[,] endpointColors = new int[endpoints, PixelBytes];
        for (int c = 0; c < comps; c++)
        {
            ulong chunk = channels[c];
            for (int e = 0; e < endpoints; e++)
            {
                endpointColors[e, c] = (int)(chunk & (ulong)endpointMask);
                chunk >>= endpointBits;
            }
        }

        Span<int> pbitValues = stackalloc int[4];
        for (int p = 0; p < pbits; p++)
        {
            pbitValues[p] = (int)((pbitChunk >> p) & 1UL);
        }

        InsertWeightZero(ref weightChunk, weightBits, 0);
        InsertWeightZero(ref weightChunk, weightBits, Bc7Tables.AnchorIndexSecondSubset[part]);

        Span<int> weights = stackalloc int[BlockPixels];
        for (int i = 0; i < BlockPixels; i++)
        {
            weights[i] = (int)(weightChunk & (ulong)weightMask);
            weightChunk >>= weightBits;
        }

        for (int e = 0; e < endpoints; e++)
        {
            for (int c = 0; c < PixelBytes; c++)
            {
                if (mode != 7 && c == 3)
                {
                    endpointColors[e, c] = 255;
                    continue;
                }

                int pbit = pbitValues[sharedPbits ? e >> 1 : e];
                endpointColors[e, c] = Dequant(endpointColors[e, c], pbit, endpointBits);
            }
        }

        int blockColorsCount = 2 * 8;
        Span<byte> blockColors = stackalloc byte[blockColorsCount * PixelBytes];
        for (int s = 0; s < 2; s++)
        {
            for (int i = 0; i < weightValues; i++)
            {
                int offset = (s * 8 + i) * PixelBytes;
                for (int c = 0; c < comps; c++)
                {
                    blockColors[offset + c] = (byte)Interp(endpointColors[s * 2, c], endpointColors[s * 2 + 1, c], i, weightBits);
                }

                if (comps == 3)
                {
                    blockColors[offset + 3] = 255;
                }
                else
                {
                    blockColors[offset + 3] = (byte)Interp(endpointColors[s * 2, 3], endpointColors[s * 2 + 1, 3], i, weightBits);
                }
            }
        }

        Span<byte> pixel = stackalloc byte[PixelBytes];
        for (int i = 0; i < BlockPixels; i++)
        {
            int subset = Bc7Tables.Partition2[part * 16 + i];
            int offset = (subset * 8 + weights[i]) * PixelBytes;
            pixel[0] = blockColors[offset];
            pixel[1] = blockColors[offset + 1];
            pixel[2] = blockColors[offset + 2];
            pixel[3] = blockColors[offset + 3];
            WritePixel(rgba, i, pixel);
        }

        return true;
    }

    private static bool DecodeMode45(int mode, ulong low, ulong high, Span<byte> rgba)
    {
        const int endpoints = 2;

        int weightBits = 2;
        int weightMask = (1 << weightBits) - 1;
        int alphaWeightBits = mode == 4 ? 3 : 2;
        int alphaWeightMask = (1 << alphaWeightBits) - 1;
        int endpointBits = mode == 4 ? 5 : 7;
        int endpointMask = (1 << endpointBits) - 1;
        int alphaEndpointBits = mode == 4 ? 6 : 8;
        int alphaEndpointMask = (1 << alphaEndpointBits) - 1;

        int compRot = (int)((low >> (mode + 1)) & 0x3);
        int indexMode = mode == 4 ? (int)((low >> 7) & 1) : 0;

        ulong colorReadBits = low >> 8;
        int[,] endpointColors = new int[endpoints, PixelBytes];
        for (int c = 0; c < 3; c++)
        {
            for (int e = 0; e < endpoints; e++)
            {
                endpointColors[e, c] = (int)(colorReadBits & (ulong)endpointMask);
                colorReadBits >>= endpointBits;
            }
        }

        ulong rgbWeightsChunk;
        ulong alphaWeightsChunk;
        if (mode == 4)
        {
            endpointColors[0, 3] = (int)(colorReadBits & (ulong)alphaEndpointMask);
            endpointColors[1, 3] = (int)((colorReadBits >> alphaEndpointBits) & (ulong)alphaEndpointMask);
            rgbWeightsChunk = (low >> 50) | (high << 14);
            alphaWeightsChunk = high >> 17;
        }
        else
        {
            endpointColors[0, 3] = (int)(colorReadBits & (ulong)alphaEndpointMask);
            endpointColors[1, 3] = (int)(((low >> 58) | (high << 6)) & (ulong)alphaEndpointMask);
            rgbWeightsChunk = high >> 2;
            alphaWeightsChunk = high >> 33;
        }

        InsertWeightZero(ref rgbWeightsChunk, weightBits, 0);
        InsertWeightZero(ref alphaWeightsChunk, alphaWeightBits, 0);

        Span<int> weightBitsByPlane = stackalloc int[2];
        weightBitsByPlane[0] = indexMode != 0 ? alphaWeightBits : weightBits;
        weightBitsByPlane[1] = indexMode != 0 ? weightBits : alphaWeightBits;
        Span<int> weightMasks = stackalloc int[2];
        weightMasks[0] = indexMode != 0 ? alphaWeightMask : weightMask;
        weightMasks[1] = indexMode != 0 ? weightMask : alphaWeightMask;

        Span<int> weights = stackalloc int[BlockPixels];
        Span<int> alphaWeights = stackalloc int[BlockPixels];

        if (indexMode != 0)
        {
            (rgbWeightsChunk, alphaWeightsChunk) = (alphaWeightsChunk, rgbWeightsChunk);
        }

        for (int i = 0; i < BlockPixels; i++)
        {
            weights[i] = (int)(rgbWeightsChunk & (ulong)weightMasks[0]);
            rgbWeightsChunk >>= weightBitsByPlane[0];
        }

        for (int i = 0; i < BlockPixels; i++)
        {
            alphaWeights[i] = (int)(alphaWeightsChunk & (ulong)weightMasks[1]);
            alphaWeightsChunk >>= weightBitsByPlane[1];
        }

        for (int e = 0; e < endpoints; e++)
        {
            for (int c = 0; c < PixelBytes; c++)
            {
                endpointColors[e, c] = Dequant(
                    endpointColors[e, c],
                    c == 3 ? alphaEndpointBits : endpointBits);
            }
        }

        Span<byte> blockColors = stackalloc byte[8 * PixelBytes];
        for (int i = 0; i < (1 << weightBitsByPlane[0]); i++)
        {
            int offset = i * PixelBytes;
            for (int c = 0; c < 3; c++)
            {
                blockColors[offset + c] = (byte)Interp(endpointColors[0, c], endpointColors[1, c], i, weightBitsByPlane[0]);
            }
        }

        // alpha 平面从第 8 档开始存放，避免与 rgb 档位重叠（上游 C++ 复用同一数组，这里拆成两段更直观）。
        Span<byte> alphaColors = stackalloc byte[8];
        for (int i = 0; i < (1 << weightBitsByPlane[1]); i++)
        {
            alphaColors[i] = (byte)Interp(endpointColors[0, 3], endpointColors[1, 3], i, weightBitsByPlane[1]);
        }

        Span<byte> pixel = stackalloc byte[PixelBytes];
        for (int i = 0; i < BlockPixels; i++)
        {
            int offset = weights[i] * PixelBytes;
            pixel[0] = blockColors[offset];
            pixel[1] = blockColors[offset + 1];
            pixel[2] = blockColors[offset + 2];
            pixel[3] = alphaColors[alphaWeights[i]];
            if (compRot >= 1)
            {
                (pixel[3], pixel[compRot - 1]) = (pixel[compRot - 1], pixel[3]);
            }

            WritePixel(rgba, i, pixel);
        }

        return true;
    }

    private static bool DecodeMode6(ulong low, ulong high, Span<byte> rgba)
    {
        // 位布局：mode(7) r0(7) r1(7) g0(7) g1(7) b0(7) b1(7) a0(7) a1(7) p0(1) | p1(1) s00(3) s10..s33(4x15)
        int r0 = (int)((low >> 7) & 0x7F);
        int r1 = (int)((low >> 14) & 0x7F);
        int g0 = (int)((low >> 21) & 0x7F);
        int g1 = (int)((low >> 28) & 0x7F);
        int b0 = (int)((low >> 35) & 0x7F);
        int b1 = (int)((low >> 42) & 0x7F);
        int a0 = (int)((low >> 49) & 0x7F);
        int a1 = (int)((low >> 56) & 0x7F);
        int p0 = (int)((low >> 63) & 1);
        int p1 = (int)(high & 1);

        r0 = (r0 << 1) | p0;
        g0 = (g0 << 1) | p0;
        b0 = (b0 << 1) | p0;
        a0 = (a0 << 1) | p0;
        r1 = (r1 << 1) | p1;
        g1 = (g1 << 1) | p1;
        b1 = (b1 << 1) | p1;
        a1 = (a1 << 1) | p1;

        Span<byte> values = stackalloc byte[16 * PixelBytes];
        for (int i = 0; i < 16; i++)
        {
            int w = Bc7Tables.Weights4[i];
            int inverse = 64 - w;
            int offset = i * PixelBytes;
            values[offset] = (byte)((r0 * inverse + r1 * w + 32) >> 6);
            values[offset + 1] = (byte)((g0 * inverse + g1 * w + 32) >> 6);
            values[offset + 2] = (byte)((b0 * inverse + b1 * w + 32) >> 6);
            values[offset + 3] = (byte)((a0 * inverse + a1 * w + 32) >> 6);
        }

        ulong indexBits = high >> 1;
        Span<int> indices = stackalloc int[16];
        indices[0] = (int)(indexBits & 0x7);
        indexBits >>= 3;
        for (int i = 1; i < 16; i++)
        {
            indices[i] = (int)(indexBits & 0xF);
            indexBits >>= 4;
        }

        for (int i = 0; i < BlockPixels; i++)
        {
            int offset = indices[i] * PixelBytes;
            rgba[i * PixelBytes] = values[offset];
            rgba[i * PixelBytes + 1] = values[offset + 1];
            rgba[i * PixelBytes + 2] = values[offset + 2];
            rgba[i * PixelBytes + 3] = values[offset + 3];
        }

        return true;
    }
}
