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

    /// <summary>
    /// 测量「库内一份文档 + 一个磁盘目录」的合计占用。
    /// 缓存索引搬进自有库之后，一类缓存往往由「库里一行 JSON + 目录里的图片」两部分组成；
    /// 分开量再相加，才能让设置页的占用数与清理结果一致。
    /// </summary>
    public static CacheUsage Measure(IFileSystem fileSystem, IJsonDocumentStore? documents, string? key, string? folderPath)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var usage = Measure(fileSystem, folderPath ?? string.Empty);
        if (documents is null || string.IsNullOrWhiteSpace(key))
        {
            return usage;
        }

        var bytes = documents.MeasureBytes(key);
        if (bytes <= 0)
        {
            return usage;
        }

        // 库里的文档算一个文件；图片目录里的文件数照旧。
        return new CacheUsage(usage.Bytes + bytes, usage.Files + 1);
    }

    /// <summary>删掉库内文档，再删磁盘目录；两者都不存在也算成功。</summary>
    public static void Clear(IFileSystem fileSystem, IJsonDocumentStore? documents, string? key, string? folderPath)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (documents is not null && !string.IsNullOrWhiteSpace(key))
        {
            documents.Delete(key);
        }

        Clear(fileSystem, folderPath ?? string.Empty);
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
