namespace PdxLauncher.Checks;

using System.Buffers.Binary;
using System.IO;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 图片按显示尺寸解码的两块纯逻辑自检：目标宽度换算（<see cref="ImageDecodeSize"/>）
/// 与文件头宽度解析（<see cref="ImagePixelSize"/>）。
/// <para>
/// 这两块错了不会崩，只会在真机上表现为「图糊了」或「内存没降下来」，编译器看不见，
/// 所以口径必须由这里钉住：只降不升、屏幕缩放参与换算、上限 2048。
/// </para>
/// </summary>
public static class ImageDecodeSizeChecks
{
    /// <summary>运行全部解码尺寸相关检查。</summary>
    public static int Run(IFileSystem fs)
    {
        CheckCompute();
        CheckParsePng();
        CheckParseGif();
        CheckParseBmp();
        CheckParseWebp();
        CheckParseJpeg();
        CheckParseUnknown();
        CheckReadWidthFromFile(fs);
        return 0;
    }

    private static void CheckCompute()
    {
        ChecksContext.Equal("解码尺寸: 超采样系数", 1.25, ImageDecodeSize.Oversample);

        // 48px 格子 + 1900px 原图：按 60 解码（48 × 1.0 × 1.25），而不是整张 1900。
        ChecksContext.Equal("解码尺寸: 48px@100%", 60, ImageDecodeSize.Compute(48, 1.0, 1900));

        // 150% 缩放要按物理像素来，否则高分屏上会糊。
        ChecksContext.Equal("解码尺寸: 48px@150%", 90, ImageDecodeSize.Compute(48, 1.5, 1900));

        // 源图比目标还小：只降不升，按源图宽度。
        ChecksContext.Equal("解码尺寸: 小图不放大", 40, ImageDecodeSize.Compute(100, 1.0, 40));

        // 拿不到源图宽度就按目标宽度解，交给原图解码。
        ChecksContext.Equal("解码尺寸: 宽度未知", 125, ImageDecodeSize.Compute(100, 1.0, 0));

        // 上限：再大的显示尺寸也按 2048 解。
        ChecksContext.Equal("解码尺寸: 上限 2048", 2048, ImageDecodeSize.Compute(5000, 1.0, 9000));

        // 下限：极小的目标宽度也至少解 16px。
        ChecksContext.Equal("解码尺寸: 下限 16", 16, ImageDecodeSize.Compute(1, 1.0, 9000));

        // 非法输入一律返回 0（不设 DecodePixelWidth）。
        ChecksContext.Equal("解码尺寸: 0 宽度", 0, ImageDecodeSize.Compute(0, 1.0, 100));
        ChecksContext.Equal("解码尺寸: NaN 宽度", 0, ImageDecodeSize.Compute(double.NaN, 1.0, 100));
        ChecksContext.Equal("解码尺寸: 负缩放按 100%", 125, ImageDecodeSize.Compute(100, -1, 0));
    }

    private static void CheckParsePng()
    {
        var header = new byte[24];
        header[0] = 0x89;
        header[1] = 0x50;
        header[2] = 0x4E;
        header[3] = 0x47;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16, 4), 1920);
        ChecksContext.Equal("文件头: PNG 宽度", 1920, ImagePixelSize.ParseWidth(header));
    }

    private static void CheckParseGif()
    {
        var header = new byte[10];
        header[0] = (byte)'G';
        header[1] = (byte)'I';
        header[2] = (byte)'F';
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), 320);
        ChecksContext.Equal("文件头: GIF 宽度", 320, ImagePixelSize.ParseWidth(header));
    }

    private static void CheckParseBmp()
    {
        var header = new byte[26];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(18, 4), 800);
        ChecksContext.Equal("文件头: BMP 宽度", 800, ImagePixelSize.ParseWidth(header));
    }

    private static void CheckParseWebp()
    {
        var header = new byte[30];
        header[0] = (byte)'R';
        header[1] = (byte)'I';
        header[2] = (byte)'F';
        header[3] = (byte)'F';
        header[8] = (byte)'W';
        header[9] = (byte)'E';
        header[10] = (byte)'B';
        header[11] = (byte)'P';
        header[12] = (byte)'V';
        header[13] = (byte)'P';
        header[14] = (byte)'8';
        header[15] = (byte)'X';

        // VP8X 里存的是「宽 - 1」的 24 位小端。
        int widthMinusOne = 639;
        header[24] = (byte)(widthMinusOne & 0xFF);
        header[25] = (byte)((widthMinusOne >> 8) & 0xFF);
        header[26] = (byte)((widthMinusOne >> 16) & 0xFF);
        ChecksContext.Equal("文件头: WebP 宽度", 640, ImagePixelSize.ParseWidth(header));
    }

    private static void CheckParseJpeg()
    {
        var header = new byte[]
        {
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x10, 0x01, 0x40,
            0x00,
        };

        // SOF0 里高度在前、宽度在后：宽 320 在 index+7..+9。
        ChecksContext.Equal("文件头: JPEG 宽度", 320, ImagePixelSize.ParseWidth(header));
    }

    private static void CheckParseUnknown()
    {
        ChecksContext.Equal("文件头: 空数据", 0, ImagePixelSize.ParseWidth(ReadOnlySpan<byte>.Empty));
        ChecksContext.Equal("文件头: 不认识的格式", 0, ImagePixelSize.ParseWidth(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    private static void CheckReadWidthFromFile(IFileSystem fs)
    {
        string path = Path.Combine(Path.GetTempPath(), "pdxnext-width-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var header = new byte[24];
            header[0] = 0x89;
            header[1] = 0x50;
            header[2] = 0x4E;
            header[3] = 0x47;
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16, 4), 1280);
            fs.WriteAllBytesAtomic(path, header);

            ChecksContext.Equal("文件头: 从文件读出宽度", 1280, ImagePixelSize.ReadWidth(path));
            ChecksContext.Equal("文件头: 文件不存在返回 0", 0, ImagePixelSize.ReadWidth(path + ".missing"));
        }
        finally
        {
            if (fs.FileExists(path))
            {
                fs.Delete(path);
            }
        }
    }
}
