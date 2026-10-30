namespace PdxLauncher.Core.Online;

/// <summary>按文件头 magic bytes 判定素材类型（文档 §7 冻结的实测列表）。</summary>
public static class AssetFileTypeDetector
{
    /// <summary>最小合法素材字节数：小于 256 B 视为坏文件。
    /// 实测官方 cache 里 ck3-buttonBackgroundImage 只有 3,818 B、stellaris 的 4,495 B，
    /// cities_skylines-buttonBackgroundImage 更是只有 361 B（IHDR 8×50、单个 IDAT、IEND 完整），
    /// 都是合法 PNG；阈值必须低于 361 B，否则按钮背景图会被误判成坏文件，
    /// 进而让该游戏的主题刷新永远停在「部分失败」并每 10 分钟重试一次。</summary>
    public const int MinimumFileBytes = 256;

    /// <summary>最大合法素材字节数：40 MB（背景图实测 1.8 MB，留足余量）。</summary>
    public const int MaximumFileBytes = 40 * 1024 * 1024;

    /// <summary>判定所需的最大头部字节数（SVG 的 &lt;svg 可能出现在 XML 声明之后）。</summary>
    public const int HeadBytes = 4096;

    /// <summary>判定类型；头部不足或无法识别时返回 <see cref="AssetFileType.Unknown"/>。</summary>
    public static AssetFileType Detect(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 4)
        {
            if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
            {
                return AssetFileType.Png;
            }

            if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            {
                return AssetFileType.Jpeg;
            }

            if (head[0] == (byte)'G' && head[1] == (byte)'I' && head[2] == (byte)'F' && head[3] == (byte)'8')
            {
                return AssetFileType.Gif;
            }

            // 0x00010000 = TrueType；OTTO = CFF 轮廓的 OpenType；'true' = 老 TrueType。
            // 官方主题里 cities_skylines 与 eu4 的 buttonFont / appTaskbarIcon 就是 OTTO，
            // 只认 0x00010000 会让这两款游戏的字体永远下载不下来。
            if (head[0] == 0x00 && head[1] == 0x01 && head[2] == 0x00 && head[3] == 0x00)
            {
                return AssetFileType.Ttf;
            }

            if (head[0] == (byte)'O' && head[1] == (byte)'T' && head[2] == (byte)'T' && head[3] == (byte)'O')
            {
                return AssetFileType.Ttf;
            }

            if (head[0] == (byte)'t' && head[1] == (byte)'r' && head[2] == (byte)'u' && head[3] == (byte)'e')
            {
                return AssetFileType.Ttf;
            }

            // 00 00 01 00 = Windows ICO。官方主题里 eu4 的 appTaskbarIcon 就是 370 KB 的 6 帧 ICO，
            // 不认它会让 eu4 的主题刷新永远停在「部分失败」并每 10 分钟重试一次。
            if (head[0] == 0x00 && head[1] == 0x00 && head[2] == 0x01 && head[3] == 0x00)
            {
                return AssetFileType.Ico;
            }

            if (head[0] == 0x77 && head[1] == 0x4F && head[2] == 0x46 && head[3] == 0x46)
            {
                return AssetFileType.Woff;
            }

            if (head[0] == 0x77 && head[1] == 0x4F && head[2] == 0x46 && head[3] == 0x32)
            {
                return AssetFileType.Woff2;
            }
        }

        return ContainsSvgTag(head) ? AssetFileType.Svg : AssetFileType.Unknown;
    }

    /// <summary>类型对应的本地扩展名（缓存文件名以此结尾）。</summary>
    public static string GetExtension(AssetFileType type) => type switch
    {
        AssetFileType.Png => ".png",
        AssetFileType.Jpeg => ".jpg",
        AssetFileType.Svg => ".svg",
        AssetFileType.Ttf => ".ttf",
        AssetFileType.Woff => ".woff",
        AssetFileType.Woff2 => ".woff2",
        AssetFileType.Gif => ".gif",
        AssetFileType.Ico => ".ico",
        _ => string.Empty,
    };

    private static bool ContainsSvgTag(ReadOnlySpan<byte> head)
    {
        for (var i = 0; i + 3 < head.Length; i++)
        {
            if (head[i] != (byte)'<')
            {
                continue;
            }

            if (IsAsciiLetter(head[i + 1], (byte)'s')
                && IsAsciiLetter(head[i + 2], (byte)'v')
                && IsAsciiLetter(head[i + 3], (byte)'g'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>字节是否等于指定小写字母（大小写不敏感）。</summary>
    private static bool IsAsciiLetter(byte value, byte lower)
    {
        if (value == lower)
        {
            return true;
        }

        return value >= (byte)'A' && value <= (byte)'Z' && (byte)(value + 32) == lower;
    }
}
