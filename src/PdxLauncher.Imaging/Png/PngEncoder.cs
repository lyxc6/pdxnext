using System.Buffers.Binary;
using System.IO.Compression;

namespace PdxLauncher.Imaging.Png;

/// <summary>把 RGBA32 像素写成最小 PNG（8 位真彩 + alpha，无交错）。</summary>
public static class PngEncoder
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>把像素缓冲编码为 PNG 字节流。</summary>
    /// <param name="pixels">RGBA32 像素缓冲。</param>
    /// <returns>完整 PNG 文件字节。</returns>
    public static byte[] Encode(PixelBuffer pixels)
    {
        return Encode(pixels.Width, pixels.Height, pixels.Rgba32);
    }

    /// <summary>把 RGBA32 原始字节编码为 PNG 字节流。</summary>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    /// <param name="rgba">长度必须是 width*height*4。</param>
    /// <returns>完整 PNG 文件字节。</returns>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "PNG 尺寸必须为正数。");
        }

        int expected = width * height * 4;
        if (rgba.Length < expected)
        {
            throw new ArgumentException($"像素数据不足：需要 {expected} 字节，实际 {rgba.Length} 字节。", nameof(rgba));
        }

        using MemoryStream output = new();
        output.Write(Signature);

        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type: truecolor + alpha
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace
        WriteChunk(output, "IHDR", ihdr);

        int stride = width * 4;
        byte[] scanlines = new byte[(stride + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int target = y * (stride + 1);
            scanlines[target] = 0; // filter: None
            rgba.Slice(y * stride, stride).CopyTo(scanlines.AsSpan(target + 1, stride));
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(scanlines);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> payload)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
        output.Write(length);

        Span<byte> typeBytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
        {
            typeBytes[i] = (byte)type[i];
        }

        output.Write(typeBytes);
        output.Write(payload);

        uint crc = Crc32(typeBytes, payload);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload)
    {
        uint crc = 0xFFFFFFFF;
        crc = Update(crc, type);
        crc = Update(crc, payload);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
