namespace PdxLauncher.Core.Playsets;

/// <summary>路径处理的共享小工具：POSIX 相对路径、大小写不敏感的比较键。</summary>
internal static class PathHelper
{
    /// <summary>
    /// 把 <paramref name="fullPath"/> 转成相对 <paramref name="root"/> 的 POSIX 风格路径（'/' 分隔）。
    /// 不在 root 之下时回退成文件名，绝不返回空字符串。
    /// </summary>
    internal static string ToPosixRelative(string root, string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return string.Empty;
        }

        var relative = fullPath;
        if (!string.IsNullOrEmpty(root))
        {
            try
            {
                var normalizedRoot = Path.GetFullPath(root);
                var normalizedPath = Path.GetFullPath(fullPath);
                if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    relative = normalizedPath[normalizedRoot.Length..];
                }
            }
            catch (ArgumentException)
            {
                relative = fullPath;
            }
            catch (NotSupportedException)
            {
                relative = fullPath;
            }
            catch (PathTooLongException)
            {
                relative = fullPath;
            }
        }

        relative = relative.Replace('\\', '/').TrimStart('/');
        return relative;
    }

    /// <summary>大小写不敏感的绝对路径比较键；非法路径原样返回。</summary>
    internal static string NormalizeKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim().Replace('/', Path.DirectorySeparatorChar))
                .TrimEnd(Path.DirectorySeparatorChar)
                .ToUpperInvariant();
        }
        catch (ArgumentException)
        {
            return path.Trim().ToUpperInvariant();
        }
        catch (NotSupportedException)
        {
            return path.Trim().ToUpperInvariant();
        }
    }

    /// <summary>把相对路径按 baseDirectory 绝对化（已是绝对路径时原样返回）。</summary>
    internal static string? ResolveMaybeRelative(string baseDirectory, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        return string.IsNullOrEmpty(baseDirectory) ? trimmed : Path.Combine(baseDirectory, trimmed);
    }
}
