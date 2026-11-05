namespace PdxLauncher.Core.Online;

/// <summary>主题缓存索引里单个素材的条目（URL 里的 hash 段即版本号）。</summary>
public sealed class AssetIndexEntry
{
    /// <summary>上次成功下载时使用的远程 URL；URL 变化说明素材版本变了。</summary>
    public string? Url { get; set; }

    /// <summary>缓存目录下的文件名（含推断扩展名，如 ck3-background.png）。</summary>
    public string? FileName { get; set; }

    /// <summary>
    /// 上次 200 响应带回来的 ETag。URL 不变时用它发 If-None-Match：
    /// 304 只更新检查时间、绝不重写文件；服务端原地换图会变成 200。
    /// </summary>
    public string? ETag { get; set; }

    /// <summary>上次 200 响应带回来的 Last-Modified，没有 ETag 时用 If-Modified-Since。</summary>
    public string? LastModified { get; set; }
}
