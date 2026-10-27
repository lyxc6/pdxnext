namespace PdxLauncher.Core.Games;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 大小写不敏感的路径探测。Windows 上目录/文件名的大小写常与配置文件里写的不一致
/// （例如 Prison Architect 用 <c>Launcher\Launcher-assets</c>，而别的游戏用小写），
/// 而 <see cref="IFileSystem"/> 只能按精确路径判断存在性，所以这里用枚举来兜底。
/// </summary>
public static class CaseInsensitivePath
{
    /// <summary>在 <paramref name="directory"/> 下按名字（忽略大小写）找文件；找不到返回 null。</summary>
    public static string? FindFile(IFileSystem fileSystem, string directory, string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(fileName);
        if (!fileSystem.DirectoryExists(directory))
        {
            return null;
        }

        var direct = fileSystem.Combine(directory, fileName);
        if (fileSystem.FileExists(direct))
        {
            return direct;
        }

        foreach (var candidate in fileSystem.EnumerateFiles(directory, "*", recursive: false))
        {
            if (string.Equals(fileSystem.GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>在 <paramref name="parent"/> 下按名字（忽略大小写）找子目录；找不到返回 null。</summary>
    public static string? FindDirectory(IFileSystem fileSystem, string parent, string name)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(name);
        if (!fileSystem.DirectoryExists(parent))
        {
            return null;
        }

        var direct = fileSystem.Combine(parent, name);
        if (fileSystem.DirectoryExists(direct))
        {
            return direct;
        }

        foreach (var candidate in fileSystem.EnumerateDirectories(parent))
        {
            if (string.Equals(fileSystem.GetFileName(candidate), name, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 逐段解析路径，每段都允许大小写不匹配。整条路径不存在时返回 null。
    /// </summary>
    public static string? Resolve(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var full = fileSystem.GetFullPath(path);
        if (fileSystem.FileExists(full) || fileSystem.DirectoryExists(full))
        {
            return full;
        }

        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        var remainder = full.Substring(root.Length);
        var segments = remainder.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return fileSystem.DirectoryExists(root) ? root : null;
        }

        var current = root;
        for (var i = 0; i < segments.Length; i++)
        {
            var isLast = i == segments.Length - 1;
            var candidate = fileSystem.Combine(current, segments[i]);
            if (fileSystem.DirectoryExists(candidate))
            {
                current = candidate;
                continue;
            }

            if (isLast && fileSystem.FileExists(candidate))
            {
                return candidate;
            }

            var directoryMatch = FindDirectory(fileSystem, current, segments[i]);
            if (directoryMatch is not null)
            {
                current = directoryMatch;
                continue;
            }

            if (isLast)
            {
                var fileMatch = FindFile(fileSystem, current, segments[i]);
                if (fileMatch is not null)
                {
                    return fileMatch;
                }
            }

            return null;
        }

        return current;
    }
}
