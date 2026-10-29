namespace PdxLauncher.Core.Storage;

/// <summary>一次遗留缓存清理的结果：删掉多少文件 / 字节 / 整个目录。</summary>
/// <param name="Files">删掉的文件数。</param>
/// <param name="Bytes">删掉的字节数（按删除前的大小累加）。</param>
/// <param name="Directories">整目录删除的个数。</param>
public sealed record LegacyCacheCleanupResult(int Files, long Bytes, int Directories)
{
    /// <summary>什么都没删。</summary>
    public static LegacyCacheCleanupResult Nothing { get; } = new(0, 0, 0);

    /// <summary>是否有实际删除动作。</summary>
    public bool HasWork => Files > 0 || Directories > 0;
}
