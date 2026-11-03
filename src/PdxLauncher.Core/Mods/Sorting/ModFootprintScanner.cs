namespace PdxLauncher.Core.Mods.Sorting;

using System.IO.Compression;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 文件足迹扫描器：枚举 mod 的（归一化）相对路径，对照原版清单区分「覆盖原版」与「新增」。
/// 路径归一化：'\\'→'/'、去 './' 与首部 '/'、折叠 replace/ 段（events/replace/foo.txt → events/foo.txt）。
/// </summary>
public static class ModFootprintScanner
{
    /// <summary>内容顶层目录（判定 mod 内容根用；不带斜杠的目录名）。</summary>
    private static readonly string[] ContentTopDirs =
        ["common", "events", "history", "gui", "gfx", "localization", "map_data", "data_binding", "music", "fonts", "sound", "dlc", "tweakergui_assets", "content_source", "notifications", "tests", "tools"];

    /// <summary>非内容文件名前缀/后缀（说明文档、缩略图、注册残留）。</summary>
    private static readonly string[] JunkFilePrefixes = ["readme", "license", "changelog", "credits", "thumbnail", "description"];

    /// <summary>原版清单枚举的进度/取消检查粒度（文件数）。</summary>
    private const int ProgressStride = 4096;

    /// <summary>单个 mod 条目枚举的取消检查粒度（条目数）。</summary>
    private const int ScanStride = 256;

    /// <summary>构建原版内容清单（内容根下的全部归一化相对路径集合）。</summary>
    /// <param name="progress">枚举进度回调（每 4096 个文件报一次，原版库通常 5 万级）。</param>
    /// <param name="cancellationToken">取消令牌；枚举过程中按 4096 个文件粒度检查。</param>
    public static IReadOnlySet<string> BuildVanillaIndex(
        IFileSystem fs,
        string contentRoot,
        IProgress<AutoSortProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(contentRoot);

        var index = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!fs.DirectoryExists(contentRoot))
        {
            return index;
        }

        var rootFullPath = fs.GetFullPath(contentRoot);
        var scanned = 0;
        foreach (var file in fs.EnumerateFiles(contentRoot, "*", true))
        {
            if (++scanned % ProgressStride == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new AutoSortProgress(AutoSortPhase.VanillaIndex, scanned, 0));
            }

            var relative = ToRelativePath(rootFullPath, fs.GetFullPath(file));
            if (relative is not null)
            {
                index.Add(NormalizePath(relative, out _));
            }
        }

        progress?.Report(new AutoSortProgress(AutoSortPhase.VanillaIndex, scanned, 0));
        return index;
    }

    /// <summary>
    /// 扫描单个 mod 产出足迹。有压缩包优先用压缩包（与 ModLoadRules 一致）；
    /// 目录/压缩包都不可用时返回空足迹。
    /// </summary>
    public static ModFootprint Scan(
        IFileSystem fs,
        Guid modId,
        string name,
        string? dirPath,
        string? archivePath,
        IReadOnlySet<string> vanillaPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(vanillaPaths);
        cancellationToken.ThrowIfCancellationRequested();

        var paths = CollectRelativePaths(fs, dirPath, archivePath, cancellationToken);
        return Measure(modId, name, paths, vanillaPaths, cancellationToken);
    }

    /// <summary>收集 mod 内容的相对路径（已剔除垃圾，未折叠 replace/；返回「原相对路径」）。</summary>
    private static List<string> CollectRelativePaths(
        IFileSystem fs,
        string? dirPath,
        string? archivePath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(archivePath) && fs.FileExists(archivePath!))
        {
            return CollectFromZip(archivePath!, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(dirPath) && fs.DirectoryExists(dirPath!))
        {
            return CollectFromDirectory(fs, dirPath!, cancellationToken);
        }

        return [];
    }

    private static List<string> CollectFromDirectory(
        IFileSystem fs,
        string dirPath,
        CancellationToken cancellationToken)
    {
        var rootFullPath = fs.GetFullPath(dirPath);
        var relativePaths = new List<string>();
        var scanned = 0;
        foreach (var file in fs.EnumerateFiles(dirPath, "*", true))
        {
            if (++scanned % ScanStride == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var relative = ToRelativePath(rootFullPath, fs.GetFullPath(file));
            if (relative is not null)
            {
                relativePaths.Add(relative);
            }
        }

        return StripWrapper(relativePaths);
    }

    private static List<string> CollectFromZip(string archivePath, CancellationToken cancellationToken)
    {
        var relativePaths = new List<string>();
        try
        {
            using var zip = ZipFile.OpenRead(archivePath);
            var scanned = 0;
            foreach (var entry in zip.Entries)
            {
                if (++scanned % ScanStride == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                relativePaths.Add(entry.FullName.Replace('\\', '/'));
            }
        }
        catch (InvalidDataException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }

        return StripWrapper(relativePaths);
    }

    /// <summary>
    /// 垃圾剔除 + 内容根探测：
    /// 根下没有任何已知内容顶层目录、且只剩一个顶级目录时，把它当内容根剥掉（mod 常见「多包一层文件夹」）。
    /// </summary>
    private static List<string> StripWrapper(List<string> relativePaths)
    {
        var kept = new List<string>();
        var topDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in relativePaths)
        {
            if (IsJunkPath(path))
            {
                continue;
            }

            kept.Add(path);
            var slash = path.IndexOf('/');
            if (slash > 0)
            {
                topDirs.Add(path[..slash]);
            }
        }

        var hasKnownTopDir = topDirs.Any(dir => ContentTopDirs.Contains(dir, StringComparer.OrdinalIgnoreCase));
        if (!hasKnownTopDir && topDirs.Count == 1)
        {
            var prefix = topDirs.First() + "/";
            for (var i = 0; i < kept.Count; i++)
            {
                if (kept[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    kept[i] = kept[i][prefix.Length..];
                }
            }
        }

        return kept;
    }

    /// <summary>统计足迹：归一化路径 → 覆盖/新增集合与各类计数。</summary>
    private static ModFootprint Measure(
        Guid modId,
        string name,
        List<string> relativePaths,
        IReadOnlySet<string> vanillaPaths,
        CancellationToken cancellationToken)
    {
        var totalFiles = 0;
        var gameplayOverrides = 0;
        var locFiles = 0;
        var gfxFiles = 0;
        var replaceFiles = 0;
        var overridden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var measured = 0;
        foreach (var path in relativePaths)
        {
            if (++measured % ScanStride == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var normalized = NormalizePath(path, out var hadReplaceSegment);
            if (normalized.Length == 0)
            {
                continue;
            }

            totalFiles++;
            if (hadReplaceSegment)
            {
                replaceFiles++;
            }

            if (FootprintClassifier.IsLocPath(normalized))
            {
                locFiles++;
            }

            if (FootprintClassifier.IsGfxPath(normalized))
            {
                gfxFiles++;
            }

            if (vanillaPaths.Contains(normalized))
            {
                overridden.Add(normalized);
                if (FootprintClassifier.IsGameplayPath(normalized))
                {
                    gameplayOverrides++;
                }
            }
            else
            {
                added.Add(normalized);
            }
        }

        return new ModFootprint(
            modId,
            name,
            totalFiles,
            gameplayOverrides,
            locFiles,
            gfxFiles,
            replaceFiles,
            overridden,
            added);
    }

    /// <summary>
    /// 路径归一化：'\\'→'/'、去空段与 '.' 段、折叠 replace/ 段（见 CK3 的整文件覆盖语义）。
    /// </summary>
    /// <param name="hadReplaceSegment">是否折叠过 replace/ 段。</param>
    private static string NormalizePath(string relativePath, out bool hadReplaceSegment)
    {
        hadReplaceSegment = false;
        var segments = relativePath.Replace('\\', '/').Split('/');
        var kept = new List<string>(segments.Length);
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            var isFileName = i == segments.Length - 1;
            if (!isFileName && string.Equals(segment, "replace", StringComparison.OrdinalIgnoreCase))
            {
                hadReplaceSegment = true;
                continue;
            }

            kept.Add(segment);
        }

        return string.Join("/", kept);
    }

    /// <summary>垃圾判定：隐藏目录/开发残留目录下的任何文件，或说明文档类文件名。</summary>
    private static bool IsJunkPath(string relativePath)
    {
        var segments = relativePath.Replace('\\', '/').Split('/');
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (segment.StartsWith('.') || string.Equals(segment, "__MACOSX", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var fileName = segments[^1];
        if (fileName.EndsWith(".mod", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var prefix in JunkFilePrefixes)
        {
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>把绝对路径换算成相对内容根的相对路径；不在根下返回 null。</summary>
    private static string? ToRelativePath(string rootFullPath, string fileFullPath)
    {
        var root = rootFullPath.Replace('\\', '/').TrimEnd('/');
        var file = fileFullPath.Replace('\\', '/');
        if (!file.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return file[(root.Length + 1)..];
    }
}
