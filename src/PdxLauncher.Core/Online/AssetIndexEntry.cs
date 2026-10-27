namespace PdxLauncher.Core.Online;

/// <summary>主题缓存索引里单个素材的条目（URL 里的 hash 段即版本号）。</summary>
public sealed class AssetIndexEntry
{
    /// <summary>上次成功下载时使用的远程 URL；URL 变化说明素材版本变了。</summary>
    public string? Url { get; set; }

    /// <summary>缓存目录下的文件名（含推断扩展名，如 ck3-background.png）。</summary>
    public string? FileName { get; set; }
}
