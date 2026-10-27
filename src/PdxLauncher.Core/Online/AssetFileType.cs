namespace PdxLauncher.Core.Online;

/// <summary>
/// 素材文件类型。服务器返回的 Content-Type 不可信（实测返回 multipart/form-data），
/// 因此类型一律由文件头 magic bytes 判定。
/// </summary>
public enum AssetFileType
{
    /// <summary>无法识别（下载会被拒绝）。</summary>
    Unknown = 0,

    /// <summary>PNG，magic 89 50 4E 47。</summary>
    Png = 1,

    /// <summary>JPEG，magic FF D8 FF。</summary>
    Jpeg = 2,

    /// <summary>SVG（文本，头部含 &lt;svg）。</summary>
    Svg = 3,

    /// <summary>TTF，magic 00 01 00 00。</summary>
    Ttf = 4,

    /// <summary>WOFF，magic wOFF。</summary>
    Woff = 5,

    /// <summary>WOFF2，magic wOF2。</summary>
    Woff2 = 6,

    /// <summary>GIF，magic GIF8（官方公告图常用 GIF）。</summary>
    Gif = 7,
}
