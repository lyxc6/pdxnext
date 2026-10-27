namespace PdxLauncher.Checks;

using System.Buffers.Binary;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Imaging;
using PdxLauncher.Imaging.Dds;

/// <summary>DDS 解码库与 mod 缩略图挑选顺序的自检。</summary>
public static class DdsChecks
{
    private const uint DdpfFourCc = 0x4;
    private const uint DdpfRgb = 0x40;
    private const uint DdpfAlphaPixels = 0x1;
    private const uint FourCcDxt1 = 0x31545844;
    private const uint FourCcDxt5 = 0x35545844;
    private const uint FourCcDx10 = 0x30315844;

    /// <summary>运行全部 DDS 相关检查。</summary>
    public static int Run(IFileSystem fs)
    {
        CheckHeaderParsing();
        CheckRejections();
        CheckUncompressed();
        CheckBc1();
        CheckBc3();
        CheckBc7Mode6();
        CheckBc7Mode0();
        CheckDownscale();
        CheckPngEncoder();
        CheckRealDlcThumbnails();
        CheckModThumbnailOrder(fs);
        return 0;
    }

    private static void CheckHeaderParsing()
    {
        // 64x32 BC1：16x8 个块，每块 8 字节 = 1024 字节。
        byte[] bc1 = Concat(Header(64, 32, DdpfFourCc, FourCcDxt1, 0, 0, 0, 0, 0), new byte[1024]);
        bool ok = DdsImage.TryReadInfo(bc1, out DdsImageInfo info);
        ChecksContext.Check("BC1 头可解析", ok);
        ChecksContext.Equal("BC1 宽度", 64, info.Width);
        ChecksContext.Equal("BC1 高度", 32, info.Height);
        ChecksContext.Equal("BC1 格式", DdsFormat.Bc1, info.Format);
        ChecksContext.Equal("BC1 mip0 长度", 1024, info.DataLength);
        ChecksContext.Equal("BC1 数据偏移", 128, info.DataOffset);

        ChecksContext.Check("非 DDS 魔数被拒绝", !DdsImage.TryReadInfo(new byte[128], out _));

        // 128 字节头 + 不足的数据。
        byte[] truncated = Header(64, 32, DdpfFourCc, FourCcDxt1, 0, 0, 0, 0, 0);
        ChecksContext.Check("数据不足被拒绝", !DdsImage.TryReadInfo(truncated, out _));

        // DX10 + BC7：8x8 需要 4 个块 * 16 = 64 字节。
        byte[] bc7 = Concat(
            Header(8, 8, DdpfFourCc, FourCcDx10, 0, 0, 0, 0, 0),
            Dx10(98, 3, 0, 1),
            new byte[64]);
        ChecksContext.Check("DX10 BC7 头可解析", DdsImage.TryReadInfo(bc7, out DdsImageInfo bc7Info));
        ChecksContext.Equal("DX10 BC7 格式", DdsFormat.Bc7, bc7Info.Format);
        ChecksContext.Equal("DX10 BC7 偏移", 148, bc7Info.DataOffset);
        ChecksContext.Equal("DX10 BC7 长度", 64, bc7Info.DataLength);
    }

    private static void CheckRejections()
    {
        ChecksContext.Check(
            "BC6H 被拒绝",
            !DdsImage.TryReadInfo(Concat(Header(8, 8, DdpfFourCc, FourCcDx10, 0, 0, 0, 0, 0), Dx10(95, 3, 0, 1), new byte[64]), out _));

        ChecksContext.Check(
            "立方体贴图被拒绝",
            !DdsImage.TryReadInfo(Concat(Header(8, 8, DdpfFourCc, FourCcDx10, 0, 0, 0, 0, 0), Dx10(98, 3, 4, 1), new byte[64]), out _));

        ChecksContext.Check(
            "纹理数组被拒绝",
            !DdsImage.TryReadInfo(Concat(Header(8, 8, DdpfFourCc, FourCcDx10, 0, 0, 0, 0, 0), Dx10(98, 3, 0, 2), new byte[64]), out _));

        ChecksContext.Check(
            "3D 纹理被拒绝",
            !DdsImage.TryReadInfo(
                Concat(Header(8, 8, DdpfFourCc, FourCcDxt1, 0, 0, 0, 0, 0, flags: 0x801007, depth: 4), new byte[64]),
                out _));

        ChecksContext.Check(
            "未知 DXGI 格式被拒绝",
            !DdsImage.TryReadInfo(Concat(Header(8, 8, DdpfFourCc, FourCcDx10, 0, 0, 0, 0, 0), Dx10(200, 3, 0, 1), new byte[64]), out _));

        DdsDecodeResult data = DdsImage.Decode(Header(64, 32, DdpfFourCc, FourCcDxt1, 0, 0, 0, 0, 0));
        ChecksContext.Check("解码失败不抛异常且带中文原因", !data.IsSuccess && data.Error.Length > 0, data.Error);
    }

    private static void CheckUncompressed()
    {
        // 2x2 BGRA8（R 掩码 0x00FF0000 → 内存字节序是 B,G,R,A）：像素依次为 红、绿、蓝、白。
        byte[] payload =
        [
            0, 0, 255, 255,
            0, 255, 0, 255,
            255, 0, 0, 255,
            255, 255, 255, 255,
        ];
        byte[] file = Concat(Header(2, 2, DdpfRgb | DdpfAlphaPixels, 0, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000), payload);
        DdsDecodeResult result = DdsImage.Decode(file);
        ChecksContext.Check("BGRA8 解码成功", result.IsSuccess, result.Error);
        if (!result.IsSuccess)
        {
            return;
        }

        PixelBuffer pixels = result.Pixels;
        ChecksContext.Equal("BGRA8 像素 0 为红色", 255, pixels.Rgba32[0]);
        ChecksContext.Equal("BGRA8 像素 0 绿通道", 0, pixels.Rgba32[1]);
        ChecksContext.Equal("BGRA8 像素 0 蓝通道", 0, pixels.Rgba32[2]);
        ChecksContext.Equal("BGRA8 像素 1 绿通道", 255, pixels.Rgba32[5]);

        // 24 位 BGR。
        byte[] bgr = Concat(Header(1, 1, DdpfRgb, 0, 24, 0, 0, 0, 0), [10, 20, 30]);
        DdsDecodeResult bgrResult = DdsImage.Decode(bgr);
        ChecksContext.Check("BGR8 解码成功", bgrResult.IsSuccess, bgrResult.Error);
        if (bgrResult.IsSuccess)
        {
            ChecksContext.Equal("BGR8 转成 RGBA 的 R", 30, bgrResult.Pixels.Rgba32[0]);
            ChecksContext.Equal("BGR8 转成 RGBA 的 G", 20, bgrResult.Pixels.Rgba32[1]);
            ChecksContext.Equal("BGR8 转成 RGBA 的 B", 10, bgrResult.Pixels.Rgba32[2]);
            ChecksContext.Equal("BGR8 转成 RGBA 的 A", 255, bgrResult.Pixels.Rgba32[3]);
        }

        PixelBuffer noisyAlpha = new(2, 1, [10, 20, 30, 0, 40, 50, 60, 200]);
        PixelBuffer forced = DdsImage.ForceOpaque(noisyAlpha);
        ChecksContext.Equal("ForceOpaque 第一像素不透明", (byte)255, forced.Rgba32[3]);
        ChecksContext.Equal("ForceOpaque 第二像素不透明", (byte)255, forced.Rgba32[7]);
        ChecksContext.Equal("ForceOpaque 保留 RGB", (byte)60, forced.Rgba32[6]);
        ChecksContext.Equal("ForceOpaque 不改原缓冲", (byte)0, noisyAlpha.Rgba32[3]);
    }

    private static void CheckBc1()
    {
        // 4x4 单块：c0 纯红、c1 纯蓝（c0 > c1 → 四色模式），索引全 0 → 全红。
        byte[] block = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(block, 0xF800);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(2), 0x001F);
        byte[] file = Concat(Header(4, 4, DdpfFourCc, FourCcDxt1, 0, 0, 0, 0, 0), block);
        DdsDecodeResult red = DdsImage.Decode(file);
        ChecksContext.Check("BC1 四色模式解码成功", red.IsSuccess, red.Error);
        if (red.IsSuccess)
        {
            ChecksContext.Equal("BC1 索引 0 为红 R", 255, red.Pixels.Rgba32[0]);
            ChecksContext.Equal("BC1 索引 0 为红 G", 0, red.Pixels.Rgba32[1]);
            ChecksContext.Equal("BC1 索引 0 为红 B", 0, red.Pixels.Rgba32[2]);
        }

        // 三色 + 透明模式：c0 <= c1，索引全 3 → alpha 0。
        byte[] transparent = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(transparent, 0x001F);
        BinaryPrimitives.WriteUInt16LittleEndian(transparent.AsSpan(2), 0xF800);
        BinaryPrimitives.WriteUInt32LittleEndian(transparent.AsSpan(4), 0xFFFFFFFF);
        DdsDecodeResult alpha = DdsImage.Decode(Concat(Header(4, 4, DdpfFourCc, FourCcDxt1, 0, 0, 0, 0, 0), transparent));
        ChecksContext.Check("BC1 透明模式解码成功", alpha.IsSuccess, alpha.Error);
        if (alpha.IsSuccess)
        {
            ChecksContext.Equal("BC1 索引 3 透明", 0, alpha.Pixels.Rgba32[3]);
        }
    }

    private static void CheckBc3()
    {
        // alpha 块：a0=200, a1=100，索引全 0 → 200；颜色块：c0 纯白、c1 纯白。
        byte[] block = new byte[16];
        block[0] = 200;
        block[1] = 100;
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(8), 0xFFFF);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(10), 0xFFFF);
        DdsDecodeResult result = DdsImage.Decode(Concat(Header(4, 4, DdpfFourCc, FourCcDxt5, 0, 0, 0, 0, 0), block));
        ChecksContext.Check("BC3 解码成功", result.IsSuccess, result.Error);
        if (!result.IsSuccess)
        {
            return;
        }

        ChecksContext.Equal("BC3 alpha 取 a0", 200, result.Pixels.Rgba32[3]);
        ChecksContext.Equal("BC3 颜色为白", 255, result.Pixels.Rgba32[0]);
    }

    private static void CheckBc7Mode6()
    {
        // mode 6：8 个 7 位端点 + 2 个 pbit + 16 个索引（首索引 3 位）。
        byte[] block = new byte[16];
        int bit = 0;
        PutBits(block, ref bit, 7, 0x40);
        foreach (int channel in new[] { 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40 })
        {
            PutBits(block, ref bit, 7, channel);
        }

        PutBits(block, ref bit, 1, 0);
        PutBits(block, ref bit, 1, 0);
        PutBits(block, ref bit, 3, 0);
        for (int i = 1; i < 16; i++)
        {
            PutBits(block, ref bit, 4, 0);
        }

        DdsDecodeResult result = DdsImage.Decode(Concat(Bc7Header(4, 4), block));
        ChecksContext.Check("BC7 mode6 解码成功", result.IsSuccess, result.Error);
        if (result.IsSuccess)
        {
            ChecksContext.Equal("BC7 mode6 灰度", 0x80, result.Pixels.Rgba32[0]);
            ChecksContext.Equal("BC7 mode6 alpha", 0x80, result.Pixels.Rgba32[3]);
        }
    }

    private static void CheckBc7Mode0()
    {
        // mode 0：3 子集、4 位端点、3 位权重；把三个子集的端点都设成同一颜色 → 整块同色。
        byte[] block = new byte[16];
        int bit = 0;
        PutBits(block, ref bit, 1, 1);
        PutBits(block, ref bit, 4, 0);   // partition
        for (int e = 0; e < 6; e++)
        {
            PutBits(block, ref bit, 4, 0xF); // R
            PutBits(block, ref bit, 4, 0x0); // G
            PutBits(block, ref bit, 4, 0x0); // B
        }

        for (int p = 0; p < 6; p++)
        {
            PutBits(block, ref bit, 1, 1);
        }

        for (int i = 0; i < 16; i++)
        {
            PutBits(block, ref bit, 3, 0);
        }

        DdsDecodeResult result = DdsImage.Decode(Concat(Bc7Header(4, 4), block));
        ChecksContext.Check("BC7 mode0 解码成功", result.IsSuccess, result.Error);
        if (result.IsSuccess)
        {
            ChecksContext.Equal("BC7 mode0 红色端点", 255, result.Pixels.Rgba32[0]);
            ChecksContext.Equal("BC7 mode0 不透明", 255, result.Pixels.Rgba32[3]);
        }
    }

    private static void CheckDownscale()
    {
        byte[] bc1 = Concat(
            Header(64, 32, DdpfFourCc, FourCcDxt1, 0, 0, 0, 0, 0),
            new byte[16 * 8 * 8]);
        DdsDecodeResult result = DdsImage.Decode(bc1, maxDimension: 16);
        ChecksContext.Check("缩小解码成功", result.IsSuccess, result.Error);
        if (result.IsSuccess)
        {
            ChecksContext.Equal("缩小后宽", 16, result.Pixels.Width);
            ChecksContext.Equal("缩小后高", 8, result.Pixels.Height);
            ChecksContext.Equal("缩小后缓冲长度", 16 * 8 * 4, result.Pixels.Rgba32.Length);
        }
    }

    private static void CheckPngEncoder()
    {
        byte[] rgba =
        [
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 255, 255, 255, 128,
        ];
        byte[] png = DdsImage.EncodePng(new PixelBuffer(2, 2, rgba));
        ChecksContext.Check(
            "PNG 魔数正确",
            png.Length > 45
            && png[0] == 0x89 && png[1] == 0x50 && png[2] == 0x4E && png[3] == 0x47);
        ChecksContext.Equal("PNG IHDR 宽度", 2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        ChecksContext.Equal("PNG IHDR 高度", 2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        ChecksContext.Equal("PNG 颜色类型 RGBA", (byte)6, png[25]);
        ChecksContext.Check("PNG 以 IEND 结尾", png[^8] == 0x49 && png[^7] == 0x45 && png[^6] == 0x4E && png[^5] == 0x44);
    }

    private static void CheckRealDlcThumbnails()
    {
        string[] roots =
        [
            @"F:\SteamLibrary\steamapps\common\Europa Universalis V\game\dlc",
            @"E:\SteamLibrary\steamapps\common\Europa Universalis V\game\dlc",
        ];

        string? directory = roots.FirstOrDefault(Directory.Exists);
        if (directory is null)
        {
            Console.WriteLine("  skip 本机没有 EU5 安装，跳过真实 DDS 文件解码");
            return;
        }

        List<string> files = Directory
            .EnumerateFiles(directory, "thumbnail.dds", SearchOption.AllDirectories)
            .Take(3)
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine("  skip EU5 安装目录里没有 thumbnail.dds");
            return;
        }

        foreach (string file in files)
        {
            DdsDecodeResult result = DdsImage.DecodeFile(file);
            string name = Path.GetFileName(Path.GetDirectoryName(file)) ?? file;
            ChecksContext.Check("真实 DDS 解码成功：" + name, result.IsSuccess, result.Error);
            if (!result.IsSuccess)
            {
                continue;
            }

            ChecksContext.Check("真实 DDS 是正方形：" + name, result.Pixels.Width == result.Pixels.Height, result.Pixels.Width + "x" + result.Pixels.Height);
            int min = 255;
            int max = 0;
            byte[] rgba = result.Pixels.Rgba32;
            for (int i = 0; i < rgba.Length; i += 4)
            {
                min = Math.Min(min, rgba[i]);
                max = Math.Max(max, rgba[i]);
            }

            ChecksContext.Check("真实 DDS 不是纯色：" + name, max - min > 8, "R " + min + ".." + max);

            // P 社这批缩略图的 alpha 通道是噪声（0..255 都有）：原始解码忠实保留，显示前用 ForceOpaque 兜底。
            PixelBuffer display = DdsImage.ForceOpaque(result.Pixels);
            ChecksContext.Equal("真实 DDS 兜底后不透明：" + name, (byte)255, display.Rgba32[3]);
            ChecksContext.Equal("真实 DDS 兜底不改颜色：" + name, rgba[0], display.Rgba32[0]);
        }
    }

    private static void CheckModThumbnailOrder(IFileSystem fs)
    {
        string root = Path.Combine(Path.GetTempPath(), "pdxnext-dds-" + Guid.NewGuid().ToString("N"));
        try
        {
            string modDir = Path.Combine(root, "mods", "sample");
            Directory.CreateDirectory(Path.Combine(modDir, ".metadata"));
            File.WriteAllText(Path.Combine(modDir, ".metadata", "metadata.json"), "{\"name\":\"示例\"}");
            File.WriteAllBytes(Path.Combine(modDir, "thumbnail.dds"), new byte[64]);

            var resolver = new ModThumbnailResolver(fs, new NullLogger(), () => Array.Empty<string>());
            string? onlyDds = resolver.PickThumbnailFile(modDir);
            ChecksContext.Check(
                "只有 dds 时选中 dds",
                onlyDds is not null && onlyDds.EndsWith("thumbnail.dds", StringComparison.OrdinalIgnoreCase),
                onlyDds);

            File.WriteAllBytes(Path.Combine(modDir, "thumbnail.png"), [0x89, 0x50, 0x4E, 0x47]);
            string? both = resolver.PickThumbnailFile(modDir);
            ChecksContext.Check(
                "png 优先于 dds",
                both is not null && both.EndsWith("thumbnail.png", StringComparison.OrdinalIgnoreCase),
                both);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static byte[] Bc7Header(int width, int height)
    {
        // 只拼接 128 字节头 + 20 字节 DX10 头；块数据由调用方追加，
        // 否则会先垫出一段零块，让解码器读到的是全零块（模式非法）。
        return Concat(Header(width, height, DdpfFourCc, FourCcDx10, 0, 0, 0, 0, 0), Dx10(98, 3, 0, 1));
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }

    private static byte[] Header(
        int width,
        int height,
        uint pfFlags,
        uint fourCc,
        int rgbBits,
        uint rMask,
        uint gMask,
        uint bMask,
        uint aMask,
        uint flags = 0x81007,
        int depth = 1)
    {
        byte[] header = new byte[128];
        WriteU32(header, 0, 0x20534444);
        WriteU32(header, 4, 124);
        WriteU32(header, 8, flags);
        WriteU32(header, 12, (uint)height);
        WriteU32(header, 16, (uint)width);
        WriteU32(header, 24, (uint)depth);
        WriteU32(header, 28, 1);
        WriteU32(header, 76, 32);
        WriteU32(header, 80, pfFlags);
        WriteU32(header, 84, fourCc);
        WriteU32(header, 88, (uint)rgbBits);
        WriteU32(header, 92, rMask);
        WriteU32(header, 96, gMask);
        WriteU32(header, 100, bMask);
        WriteU32(header, 104, aMask);
        return header;
    }

    private static byte[] Dx10(uint dxgiFormat, uint resourceDimension, uint miscFlag, uint arraySize)
    {
        byte[] header = new byte[20];
        WriteU32(header, 0, dxgiFormat);
        WriteU32(header, 4, resourceDimension);
        WriteU32(header, 8, miscFlag);
        WriteU32(header, 12, arraySize);
        return header;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        int total = parts.Sum(part => part.Length);
        byte[] result = new byte[total];
        int offset = 0;
        foreach (byte[] part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    private static void PutBits(byte[] buffer, ref int bit, int count, int value)
    {
        for (int i = 0; i < count; i++)
        {
            int target = bit + i;
            if (((value >> i) & 1) != 0)
            {
                buffer[target / 8] |= (byte)(1 << (target % 8));
            }
        }

        bit += count;
    }

    private static void WriteU32(byte[] buffer, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);
    }
}
