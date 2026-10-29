namespace PdxLauncher.Core.Storage;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 一次性清理「改过路径 / 改过命名之后留在磁盘上的旧缓存」。
/// <para>
/// 这些目录与文件<b>当前代码已经完全不引用</b>，但它们仍占着几十 MB，且没有任何入口能删掉：
/// 设置页的六张缓存卡片只指向现行路径，「清空全部缓存」也只遍历这六行。所以启动时跑一遍。
/// </para>
/// <para>
/// 清的是三样东西：
/// <list type="bullet">
/// <item><c>data\workshop-covers\</c> —— 创意工坊页早期的封面根，2026-10-28 改成统一用
/// <see cref="Online.WorkshopInfoCache.CoverRoot"/> 之后就没再写过。</item>
/// <item><c>data\online-mod-covers\</c> —— 已整体回退的「Mod 库在线视图」的封面根。</item>
/// <item>工坊详情 <c>shots\</c> 下 <c>shot_NN</c> 形式的旧截图 —— 命名改成 URL 哈希之后
/// 这些文件再也没有代码能读到，属于永久死文件。</item>
/// </list>
/// </para>
/// <para>
/// 幂等：没有可删的东西时什么都不做、不写盘；删失败只记 Warn 并继续下一项，绝不抛给调用方。
/// </para>
/// </summary>
public sealed class LegacyCacheCleaner
{
    /// <summary>创意工坊信息缓存目录名（与 <c>AppServices.WorkshopCacheRoot</c> 同源，由构造传入）。</summary>
    private const string ShotsDirectoryName = "shots";

    /// <summary>旧命名截图的文件名主干前缀。</summary>
    private const string LegacyShotPrefix = "shot_";

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly string _dataRoot;
    private readonly string _workshopCacheRoot;

    /// <summary>构造。</summary>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="logger">日志。</param>
    /// <param name="dataRoot">数据根目录（<c>data\</c>）。</param>
    /// <param name="workshopCacheRoot">创意工坊信息缓存根（<c>data\workshop-info</c>）。</param>
    public LegacyCacheCleaner(
        IFileSystem fileSystem,
        IAppLogger logger,
        string dataRoot,
        string workshopCacheRoot)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(workshopCacheRoot);
        _fileSystem = fileSystem;
        _logger = logger;
        _dataRoot = dataRoot;
        _workshopCacheRoot = workshopCacheRoot;
    }

    /// <summary>已废弃的缓存目录名（位于数据根下），顺序即删除顺序。</summary>
    public static IReadOnlyList<string> LegacyDirectoryNames { get; } = ["workshop-covers", "online-mod-covers"];

    /// <summary>执行清理；返回实际删掉的体积。重复调用安全。</summary>
    public LegacyCacheCleanupResult Run()
    {
        var files = 0;
        var bytes = 0L;
        var directories = 0;

        foreach (var name in LegacyDirectoryNames)
        {
            var path = _fileSystem.Combine(_dataRoot, name);
            if (!_fileSystem.DirectoryExists(path))
            {
                continue;
            }

            var usage = CacheStorage.Measure(_fileSystem, path);
            if (!TryDelete(path, "遗留缓存目录 " + name))
            {
                continue;
            }

            files += usage.Files;
            bytes += usage.Bytes;
            directories++;
        }

        var shotsRoot = _fileSystem.Combine(_workshopCacheRoot, ShotsDirectoryName);
        foreach (var directory in _fileSystem.EnumerateDirectories(shotsRoot))
        {
            foreach (var file in _fileSystem.EnumerateFiles(directory, "*", recursive: false))
            {
                if (!IsLegacyShotName(_fileSystem.GetFileName(file)))
                {
                    continue;
                }

                var size = _fileSystem.GetFileSize(file);
                if (!TryDelete(file, "旧命名截图 " + _fileSystem.GetFileName(file)))
                {
                    continue;
                }

                files++;
                bytes += size;
            }
        }

        return new LegacyCacheCleanupResult(files, bytes, directories);
    }

    /// <summary>
    /// 旧命名固定是 <c>shot_&lt;两位序号&gt;.&lt;扩展名&gt;</c>；新命名是 <c>shot_&lt;8 位小写 hex&gt;.&lt;扩展名&gt;</c>，
    /// 两者在 <c>shot_</c> 之后的长度不同，所以按「第 6、7 位是不是数字、点号是不是在 7」判定即可。
    /// </summary>
    /// <param name="fileName">文件名（含扩展名）。</param>
    private static bool IsLegacyShotName(string fileName)
    {
        if (!fileName.StartsWith(LegacyShotPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // shot_ + 两位序号 => '.' 必然落在下标 7，再往后至少还要有一位扩展名。
        return fileName.Length > 8
               && fileName[7] == '.'
               && char.IsAsciiDigit(fileName[5])
               && char.IsAsciiDigit(fileName[6]);
    }

    /// <summary>删一个文件或目录；失败只记 Warn，返回是否真的删掉了。</summary>
    private bool TryDelete(string path, string label)
    {
        try
        {
            _fileSystem.Delete(path);
            return !_fileSystem.FileExists(path) && !_fileSystem.DirectoryExists(path);
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or ArgumentException
                                              or NotSupportedException
                                              or PathTooLongException)
        {
            _logger.Warn("清理" + label + "失败：" + exception.Message);
            return false;
        }
    }
}
