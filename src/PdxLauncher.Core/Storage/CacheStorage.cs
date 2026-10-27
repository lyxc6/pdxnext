namespace PdxLauncher.Core.Storage;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 缓存目录/文件的占用统计与整目录清理。只碰磁盘，不碰各服务的内存态——
/// 需要让服务忘记缓存时先调它的 ClearCache，再用这里兜底删文件。
/// </summary>
public static class CacheStorage
{
    /// <summary>测量一个文件或目录的占用；路径不存在返回 <see cref="CacheUsage.Empty"/>，单文件读失败按 0 字节计。</summary>
    public static CacheUsage Measure(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (string.IsNullOrWhiteSpace(path))
        {
            return CacheUsage.Empty;
        }

        if (fileSystem.FileExists(path))
        {
            return new CacheUsage(fileSystem.GetFileSize(path), 1);
        }

        if (!fileSystem.DirectoryExists(path))
        {
            return CacheUsage.Empty;
        }

        var bytes = 0L;
        var files = 0;
        foreach (var file in fileSystem.EnumerateFiles(path, "*", recursive: true))
        {
            bytes += fileSystem.GetFileSize(file);
            files++;
        }

        return new CacheUsage(bytes, files);
    }

    /// <summary>删除整个缓存文件或目录（不存在也算成功）；目录是递归删除。</summary>
    public static void Clear(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        fileSystem.Delete(path);
    }
}
