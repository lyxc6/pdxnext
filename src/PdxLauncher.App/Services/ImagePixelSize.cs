namespace PdxLauncher.App.Services;

using System.Buffers.Binary;
using System.IO;

/// <summary>
/// 只读图片文件头，拿源图的真实宽度（像素）。拿不到就返回 0，调用方按「不降采样」处理。
/// <para>
/// 存在的理由：<c>BitmapImage.DecodePixelWidth</c> 会把小图放大到目标宽度再解码，
/// 一张 451px 宽的新闻图按 1280 解码会从 0.4MB 涨到 3.3MB。先读真实宽度，
/// 只降不升，才不会为了「省内存」反而多占内存。
/// </para>
/// <para>
/// 只读文件头（最多 16KB），不做完整解码；格式认 PNG / JPEG / GIF / BMP / WebP(VP8X)，
/// 其余格式一律返回 0，交给原图解码。
/// </para>
/// </summary>
public static class ImagePixelSize
{
    /// <summary>读文件头的上限：JPEG 的 SOF 段通常在开头几 KB 内。</summary>
    private const int HeaderBytes = 16 * 1024;

    /// <summary>读取并解析图片宽度；文件不存在、格式不认识或读失败时返回 0。</summary>
    public static int ReadWidth(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            var buffer = new byte[HeaderBytes];
            int read = stream.ReadAtLeast(buffer, HeaderBytes, throwOnEndOfStream: false);
            return ParseWidth(buffer.AsSpan(0, read));
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>从文件头字节里解析宽度；认不出返回 0。纯函数，便于自检。</summary>
    public static int ParseWidth(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 24
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
        {
            // PNG：8 字节签名 + 4 字节长度 + "IHDR"，宽度是 IHDR 的第一个字段。
            return BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
        }

        if (header.Length >= 10
            && header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F')
        {
            // GIF：6 字节签名 + 2 字节宽 + 2 字节高（小端）。
            return BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6, 2));
        }

        if (header.Length >= 26 && header[0] == (byte)'B' && header[1] == (byte)'M')
        {
            // BMP：BITMAPINFOHEADER 的宽度在偏移 18（小端 int32）。
            return BinaryPrimitives.ReadInt32LittleEndian(header.Slice(18, 4));
        }

        if (header.Length >= 30
            && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
            && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
        {
            return ReadWebpWidth(header);
        }

        if (header.Length >= 4 && header[0] == 0xFF && header[1] == 0xD8)
        {
            return ReadJpegWidth(header);
        }

        return 0;
    }

    private static int ReadJpegWidth(ReadOnlySpan<byte> header)
    {
        int index = 2;
        while (index + 9 < header.Length)
        {
            if (header[index] != 0xFF)
            {
                index++;
                continue;
            }

            byte marker = header[index + 1];
            if (marker == 0xFF)
            {
                index++;
                continue;
            }

            // 无长度的标记：SOI/EOI/TEM 与 RSTn。
            if (marker == 0xD8 || marker == 0xD9 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                index += 2;
                continue;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(index + 2, 2));
            if (length < 2)
            {
                return 0;
            }

            bool isStartOfFrame = marker >= 0xC0 && marker <= 0xCF
                && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isStartOfFrame)
            {
                // SOF：长度(2) + 精度(1) + 高(2) + 宽(2)。
                return BinaryPrimitives.ReadUInt16BigEndian(header.Slice(index + 7, 2));
            }

            index += 2 + length;
        }

        return 0;
    }

    private static int ReadWebpWidth(ReadOnlySpan<byte> header)
    {
        // 只认扩展格式（VP8X）：24 位小端存的是「宽 - 1」。
        // 有损/无损的简单帧头解析收益不大，认不出就返回 0，按原图解码。
        if (header[12] == (byte)'V' && header[13] == (byte)'P' && header[14] == (byte)'8' && header[15] == (byte)'X')
        {
            return 1 + (header[24] | (header[25] << 8) | (header[26] << 16));
        }

        return 0;
    }
}
