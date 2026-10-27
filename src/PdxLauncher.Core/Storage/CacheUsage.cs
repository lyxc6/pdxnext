namespace PdxLauncher.Core.Storage;

/// <summary>一个缓存区当前的磁盘占用：字节数与文件数。缺失的路径是 <see cref="Empty"/>。</summary>
public sealed record CacheUsage(long Bytes, int Files)
{
    /// <summary>路径不存在时的占用。</summary>
    public static CacheUsage Empty { get; } = new(0, 0);
}
