using System.Buffers.Binary;

namespace PdxLauncher.Imaging.Dds;

/// <summary>经典 BC 块压缩格式（BC1/BC2/BC3/BC4/BC5）的解码器。</summary>
internal static class BlockDecoder
{
    private const int PixelBytes = 4;

    /// <summary>解码 BC1（DXT1）数据。</summary>
    public static void DecodeBc1(ReadOnlySpan<byte> data, int width, int height, Span<byte> rgba)
    {
        Span<byte> block = stackalloc byte[16 * PixelBytes];
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int sourceOffset = (by * blocksX + bx) * 8;
                DecodeBc1Block(data.Slice(sourceOffset, 8), block);
                CopyBlock(block, width, height, bx * 4, by * 4, rgba);
            }
        }
    }

    /// <summary>解码 BC2（DXT3）数据。</summary>
    public static void DecodeBc2(ReadOnlySpan<byte> data, int width, int height, Span<byte> rgba)
    {
        Span<byte> block = stackalloc byte[16 * PixelBytes];
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int sourceOffset = (by * blocksX + bx) * 16;
                DecodeBc2Block(data.Slice(sourceOffset, 16), block);
                CopyBlock(block, width, height, bx * 4, by * 4, rgba);
            }
        }
    }

    /// <summary>解码 BC3（DXT5）数据。</summary>
    public static void DecodeBc3(ReadOnlySpan<byte> data, int width, int height, Span<byte> rgba)
    {
        Span<byte> block = stackalloc byte[16 * PixelBytes];
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int sourceOffset = (by * blocksX + bx) * 16;
                DecodeBc3Block(data.Slice(sourceOffset, 16), block);
                CopyBlock(block, width, height, bx * 4, by * 4, rgba);
            }
        }
    }

    /// <summary>解码 BC4（ATI1）单通道数据，输出灰度 RGBA。</summary>
    public static void DecodeBc4(ReadOnlySpan<byte> data, int width, int height, Span<byte> rgba)
    {
        Span<byte> block = stackalloc byte[16 * PixelBytes];
        Span<byte> channel = stackalloc byte[16];
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int sourceOffset = (by * blocksX + bx) * 8;
                DecodeAlphaBlock(data.Slice(sourceOffset, 8), channel);
                for (int i = 0; i < 16; i++)
                {
                    int offset = i * PixelBytes;
                    block[offset] = channel[i];
                    block[offset + 1] = channel[i];
                    block[offset + 2] = channel[i];
                    block[offset + 3] = 255;
                }

                CopyBlock(block, width, height, bx * 4, by * 4, rgba);
            }
        }
    }

    /// <summary>解码 BC5（ATI2）双通道数据，输出 RG 法线贴图。</summary>
    public static void DecodeBc5(ReadOnlySpan<byte> data, int width, int height, Span<byte> rgba)
    {
        Span<byte> block = stackalloc byte[16 * PixelBytes];
        Span<byte> red = stackalloc byte[16];
        Span<byte> green = stackalloc byte[16];
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int sourceOffset = (by * blocksX + bx) * 16;
                DecodeAlphaBlock(data.Slice(sourceOffset, 8), red);
                DecodeAlphaBlock(data.Slice(sourceOffset + 8, 8), green);
                for (int i = 0; i < 16; i++)
                {
                    int offset = i * PixelBytes;
                    block[offset] = red[i];
                    block[offset + 1] = green[i];
                    block[offset + 2] = 0;
                    block[offset + 3] = 255;
                }

                CopyBlock(block, width, height, bx * 4, by * 4, rgba);
            }
        }
    }

    private static void CopyBlock(ReadOnlySpan<byte> block, int width, int height, int x, int y, Span<byte> rgba)
    {
        for (int row = 0; row < 4; row++)
        {
            int py = y + row;
            if (py >= height)
            {
                return;
            }

            for (int col = 0; col < 4; col++)
            {
                int px = x + col;
                if (px >= width)
                {
                    continue;
                }

                int source = (row * 4 + col) * PixelBytes;
                int target = (py * width + px) * PixelBytes;
                rgba[target] = block[source];
                rgba[target + 1] = block[source + 1];
                rgba[target + 2] = block[source + 2];
                rgba[target + 3] = block[source + 3];
            }
        }
    }

    private static void DecodeBc1Block(ReadOnlySpan<byte> source, Span<byte> block)
    {
        ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(source);
        ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(source[2..]);
        uint indices = BinaryPrimitives.ReadUInt32LittleEndian(source[4..]);

        Span<byte> palette = stackalloc byte[4 * PixelBytes];
        Rgb565(c0, palette[..3]);
        palette[3] = 255;
        Rgb565(c1, palette.Slice(PixelBytes, 3));
        palette[7] = 255;

        if (c0 > c1)
        {
            for (int c = 0; c < 3; c++)
            {
                palette[2 * PixelBytes + c] = (byte)((2 * palette[c] + palette[PixelBytes + c] + 1) / 3);
                palette[3 * PixelBytes + c] = (byte)((palette[c] + 2 * palette[PixelBytes + c] + 1) / 3);
            }

            palette[2 * PixelBytes + 3] = 255;
            palette[3 * PixelBytes + 3] = 255;
        }
        else
        {
            for (int c = 0; c < 3; c++)
            {
                palette[2 * PixelBytes + c] = (byte)((palette[c] + palette[PixelBytes + c]) / 2);
                palette[3 * PixelBytes + c] = 0;
            }

            palette[2 * PixelBytes + 3] = 255;
            palette[3 * PixelBytes + 3] = 0;
        }

        FillBlockFromPalette(palette, indices, block);
    }

    private static void DecodeBc2Block(ReadOnlySpan<byte> source, Span<byte> block)
    {
        DecodeBc1Block4Color(source.Slice(8, 8), block);

        for (int i = 0; i < 16; i++)
        {
            int packed = source[i >> 1];
            int alpha4 = (i & 1) == 0 ? packed & 0xF : (packed >> 4) & 0xF;
            block[i * PixelBytes + 3] = (byte)(alpha4 * 17);
        }
    }

    private static void DecodeBc3Block(ReadOnlySpan<byte> source, Span<byte> block)
    {
        Span<byte> alpha = stackalloc byte[16];
        DecodeAlphaBlock(source[..8], alpha);
        DecodeBc1Block4Color(source.Slice(8, 8), block);
        for (int i = 0; i < 16; i++)
        {
            block[i * PixelBytes + 3] = alpha[i];
        }
    }

    private static void DecodeBc1Block4Color(ReadOnlySpan<byte> source, Span<byte> block)
    {
        ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(source);
        ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(source[2..]);
        uint indices = BinaryPrimitives.ReadUInt32LittleEndian(source[4..]);

        Span<byte> palette = stackalloc byte[4 * PixelBytes];
        Rgb565(c0, palette[..3]);
        Rgb565(c1, palette.Slice(PixelBytes, 3));
        for (int c = 0; c < 3; c++)
        {
            palette[2 * PixelBytes + c] = (byte)((2 * palette[c] + palette[PixelBytes + c] + 1) / 3);
            palette[3 * PixelBytes + c] = (byte)((palette[c] + 2 * palette[PixelBytes + c] + 1) / 3);
        }

        palette[3] = 255;
        palette[7] = 255;
        palette[2 * PixelBytes + 3] = 255;
        palette[3 * PixelBytes + 3] = 255;

        FillBlockFromPalette(palette, indices, block);
    }

    private static void FillBlockFromPalette(ReadOnlySpan<byte> palette, uint indices, Span<byte> block)
    {
        for (int i = 0; i < 16; i++)
        {
            int selector = (int)((indices >> (i * 2)) & 0x3);
            int source = selector * PixelBytes;
            int target = i * PixelBytes;
            block[target] = palette[source];
            block[target + 1] = palette[source + 1];
            block[target + 2] = palette[source + 2];
            block[target + 3] = palette[source + 3];
        }
    }

    private static void DecodeAlphaBlock(ReadOnlySpan<byte> source, Span<byte> output)
    {
        int a0 = source[0];
        int a1 = source[1];

        Span<int> palette = stackalloc int[8];
        palette[0] = a0;
        palette[1] = a1;
        if (a0 > a1)
        {
            for (int i = 1; i <= 6; i++)
            {
                palette[i + 1] = ((7 - i) * a0 + i * a1 + 3) / 7;
            }
        }
        else
        {
            for (int i = 1; i <= 4; i++)
            {
                palette[i + 1] = ((5 - i) * a0 + i * a1 + 2) / 5;
            }

            palette[6] = 0;
            palette[7] = 255;
        }

        ulong bits = 0;
        for (int i = 0; i < 6; i++)
        {
            bits |= (ulong)source[2 + i] << (8 * i);
        }

        for (int i = 0; i < 16; i++)
        {
            int selector = (int)((bits >> (i * 3)) & 0x7);
            output[i] = (byte)palette[selector];
        }
    }

    private static void Rgb565(ushort value, Span<byte> target)
    {
        int r = (value >> 11) & 0x1F;
        int g = (value >> 5) & 0x3F;
        int b = value & 0x1F;
        target[0] = (byte)((r << 3) | (r >> 2));
        target[1] = (byte)((g << 2) | (g >> 4));
        target[2] = (byte)((b << 3) | (b >> 2));
    }
}
