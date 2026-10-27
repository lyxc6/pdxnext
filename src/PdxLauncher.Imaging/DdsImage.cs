using PdxLauncher.Imaging.Dds;
using PdxLauncher.Imaging.Png;

namespace PdxLauncher.Imaging;

/// <summary>解码后的像素缓冲（RGBA32，行优先，无行间距）。</summary>
/// <param name="Width">像素宽度。</param>
/// <param name="Height">像素高度。</param>
/// <param name="Rgba32">长度必须是 width*height*4 的字节数组。</param>
public readonly record struct PixelBuffer(int Width, int Height, byte[] Rgba32);

/// <summary>DDS 解码结果；失败时只带错误文本，不抛异常。</summary>
public readonly struct DdsDecodeResult
{
    private DdsDecodeResult(bool isSuccess, PixelBuffer pixels, string error)
    {
        IsSuccess = isSuccess;
        Pixels = pixels;
        Error = error;
    }

    /// <summary>是否解码成功。</summary>
    public bool IsSuccess { get; }

    /// <summary>成功时的像素缓冲；失败时为 default。</summary>
    public PixelBuffer Pixels { get; }

    /// <summary>失败原因（中文）；成功时为空串。</summary>
    public string Error { get; }

    /// <summary>构造成功结果。</summary>
    /// <param name="pixels">解码出的像素。</param>
    public static DdsDecodeResult Success(PixelBuffer pixels) => new(true, pixels, string.Empty);

    /// <summary>构造失败结果。</summary>
    /// <param name="error">失败原因。</param>
    public static DdsDecodeResult Failure(string error) => new(false, default, error);
}

/// <summary>DDS 通用入口：读头、解码、转 PNG。</summary>
public static class DdsImage
{
    /// <summary>只读文件头，不分配像素缓冲。</summary>
    /// <param name="data">文件字节（至少 128 字节）。</param>
    /// <param name="info">解析结果。</param>
    /// <returns>解析成功返回 true。</returns>
    public static bool TryReadInfo(ReadOnlySpan<byte> data, out DdsImageInfo info)
    {
        return DdsHeader.TryParse(data, out info, out _);
    }

    /// <summary>解码 mip0。</summary>
    /// <param name="data">完整文件字节。</param>
    /// <param name="maxDimension">大于 0 时按最近邻把长边缩到该值；0 表示原尺寸。</param>
    /// <returns>解码结果。</returns>
    public static DdsDecodeResult Decode(ReadOnlySpan<byte> data, int maxDimension = 0)
    {
        return DdsDecoder.Decode(data, maxDimension);
    }

    /// <summary>读文件并解码。</summary>
    /// <param name="path">DDS 文件路径。</param>
    /// <param name="maxDimension">大于 0 时按最近邻把长边缩到该值；0 表示原尺寸。</param>
    /// <returns>解码结果；文件读不到或格式不支持时 IsSuccess 为 false。</returns>
    public static DdsDecodeResult DecodeFile(string path, int maxDimension = 0)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DdsDecodeResult.Failure("未指定 DDS 文件路径");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return DdsDecodeResult.Failure($"读取 DDS 失败：{ex.Message}");
        }

        return Decode(bytes, maxDimension);
    }

    /// <summary>把像素缓冲编码为 PNG。</summary>
    /// <param name="pixels">RGBA32 像素。</param>
    /// <returns>PNG 文件字节。</returns>
    public static byte[] EncodePng(PixelBuffer pixels)
    {
        return PngEncoder.Encode(pixels);
    }

    /// <summary>
    /// 把 alpha 通道整体改成不透明（255），返回新缓冲；RGB 不动。
    /// </summary>
    /// <remarks>
    /// Paradox 的 DLC 缩略图（如 EU5 的 thumbnail.dds）在头里声明了 alpha 掩码，
    /// 但 alpha 通道里装的是噪声（整张图 0..255 都有），直接合成会得到花斑/半透明的图，
    /// 而启动器里这些图就是当不透明封面用的。解码器本身保持忠实，
    /// 需要在「缩略图」语义下显示时调用这里。
    /// </remarks>
    /// <param name="pixels">解码结果。</param>
    /// <returns>alpha 全 255 的像素缓冲。</returns>
    public static PixelBuffer ForceOpaque(PixelBuffer pixels)
    {
        byte[] rgba = pixels.Rgba32;
        if (rgba is null || rgba.Length < 4)
        {
            return pixels;
        }

        byte[] opaque = (byte[])rgba.Clone();
        for (int i = 3; i < opaque.Length; i += 4)
        {
            opaque[i] = 255;
        }

        return new PixelBuffer(pixels.Width, pixels.Height, opaque);
    }

    /// <summary>把 DDS 文件转成 PNG 文件（先写临时文件再替换，避免留下半个文件）。</summary>
    /// <param name="sourcePath">DDS 源文件。</param>
    /// <param name="targetPath">PNG 目标文件。</param>
    /// <param name="maxDimension">大于 0 时按最近邻把长边缩到该值；0 表示原尺寸。</param>
    /// <returns>成功返回 true。</returns>
    public static bool TryConvertFileToPng(string sourcePath, string targetPath, int maxDimension = 0)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        DdsDecodeResult decoded = DecodeFile(sourcePath, maxDimension);
        if (!decoded.IsSuccess)
        {
            return false;
        }

        try
        {
            string? directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            byte[] png = EncodePng(decoded.Pixels);
            string temp = targetPath + ".tmp";
            File.WriteAllBytes(temp, png);
            File.Move(temp, targetPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
