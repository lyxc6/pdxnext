namespace PdxLauncher.Core.Playsets;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Steam;

/// <summary>
/// mod 缩略图定位：把「数据库里记录的期望路径」翻译成「磁盘上真实存在的图片文件」。
/// <para>
/// 上一代 P 社游戏（Stellaris 等）的 launcher-v2.sqlite 里 dirPath 是绝对路径，玩家搬迁 Steam 库后
/// 这些路径会全部失效（实测 313 行只有 2 行仍然有效），因此不能只看 dirPath，
/// 必须按「创意工坊 id + 各 Steam 库根」重新定位。
/// </para>
/// <para>
/// 解析顺序：dirPath → archivePath 所在目录 → Steam 库重定位 → 目录内 picture 优先的图片
/// → 联网缓存 <c>mod-cache</c> → sqlite 缓存路径。
/// 任何情况下都不抛异常：调用方 <c>PlaySetRepository.ReadAsync</c> 会把异常吞成整页空列表。
/// </para>
/// </summary>
public sealed class ModThumbnailResolver
{
    private const string ThumbnailPattern = "thumbnail*";
    private const string PreferredThumbnailFileName = "thumbnail.png";
    private const string SteamAppsSegment = "steamapps";
    private const string WorkshopSegment = "workshop";
    private const string ContentSegment = "content";

    /// <summary>steamapps 之后至少要跟这么多段（workshop / content / appid）才当作工坊目录结构。</summary>
    private const int MinimumWorkshopTailSegments = 3;

    /// <summary>
    /// 能被渲染的图片扩展名；其它（.pdn / .psd / .xcf）一律跳过。
    /// <c>.dds</c> 也在这里：WinUI 不能直接解 DDS，但 App 侧会先转成 PNG 再用。
    /// </summary>
    private static readonly string[] ImageExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".dds" };

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly Lazy<IReadOnlyList<string>> _steamLibraries;
    private readonly ModThumbnailCache? _cache;

    /// <summary>创建解析器，Steam 库列表在首次需要时才从注册表 / libraryfolders.vdf 读取。</summary>
    public ModThumbnailResolver(IFileSystem fileSystem, IAppLogger logger)
        : this(fileSystem, logger, steamLibraries: null, cache: null)
    {
    }

    /// <summary>
    /// 创建解析器，并显式指定 Steam 库根列表。
    /// <paramref name="steamLibraries"/> 非 null 时完全不做注册表 / VDF 探测（测试用）。
    /// </summary>
    public ModThumbnailResolver(IFileSystem fileSystem, IAppLogger logger, Func<IReadOnlyList<string>>? steamLibraries)
        : this(fileSystem, logger, steamLibraries, cache: null)
    {
    }

    /// <summary>
    /// 创建解析器，并显式指定 Steam 库根列表与缩略图磁盘缓存。
    /// <paramref name="cache"/> 非 null 时，本地目录全 miss 后会再查联网缓存目录。
    /// </summary>
    public ModThumbnailResolver(
        IFileSystem fileSystem,
        IAppLogger logger,
        Func<IReadOnlyList<string>>? steamLibraries,
        ModThumbnailCache? cache)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _logger = logger;
        _cache = cache;

        // 库列表只求值一次：一次 mod 扫描会调用 Resolve 三百多次，不能每次都读注册表和 VDF。
        _steamLibraries = new Lazy<IReadOnlyList<string>>(
            () => steamLibraries is null
                ? new SteamLibraryLocator(fileSystem, logger).FindLibraries()
                : steamLibraries() ?? Array.Empty<string>(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// 解析 mod 缩略图。命中返回绝对路径，找不到返回 null；任何路径异常都吞掉并记 Warn。
    /// </summary>
    /// <param name="game">mod 所属游戏（重定位需要 SteamAppId）。</param>
    /// <param name="dirPath">数据库或描述文件里的 mod 目录，可能是已失效的绝对路径。</param>
    /// <param name="archivePath">归档形态 mod 的压缩包路径（缩略图取同目录）。</param>
    /// <param name="picture">.mod 的 picture 键或 metadata.json 的 thumbnail 键。</param>
    /// <param name="steamId">mods.steamId。</param>
    /// <param name="gameRegistryId">注册 ID（如 mod/ugc_2218867072.mod），steamId 缺失时靠它反解。</param>
    /// <param name="cachedThumbnailPath">sqlite mods.thumbnailPath，最低优先级。</param>
    /// <param name="modId">mods.id：用于查联网下载的磁盘缓存；null 表示不查缓存。</param>
    public string? Resolve(
        GameInstallation game,
        string? dirPath = null,
        string? archivePath = null,
        string? picture = null,
        string? steamId = null,
        string? gameRegistryId = null,
        string? cachedThumbnailPath = null,
        Guid? modId = null)
    {
        ArgumentNullException.ThrowIfNull(game);

        try
        {
            foreach (var directory in CandidateDirectories(game, dirPath, archivePath, steamId, gameRegistryId))
            {
                var resolved = ResolveInDirectory(directory, picture);
                if (resolved is not null)
                {
                    return resolved;
                }
            }

            // 本地 mod 目录全 miss 时，再看联网补全留下的缓存图（按 mod id 定位）。
            if (modId is not null && _cache is not null)
            {
                var cachedThumbnail = _cache.TryGetCachedPath(modId.Value);
                if (cachedThumbnail is not null)
                {
                    return cachedThumbnail;
                }
            }

            // sqlite 的 thumbnailPath 常指向 .launcher-cache 里已被清理的文件，必须验存在再用。
            var cached = cachedThumbnailPath?.Trim();
            if (!string.IsNullOrWhiteSpace(cached) && _fileSystem.FileExists(cached))
            {
                return cached;
            }

            return null;
        }
        catch (Exception exception) when (exception is ArgumentException
                                              or NotSupportedException
                                              or PathTooLongException
                                              or IOException
                                              or UnauthorizedAccessException)
        {
            _logger.Warn("mod 缩略图解析失败，按无图处理：" + (dirPath ?? "<null>") + "：" + exception.Message);
            return null;
        }
    }

    private IReadOnlyList<string> CandidateDirectories(
        GameInstallation game,
        string? dirPath,
        string? archivePath,
        string? steamId,
        string? gameRegistryId)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Consider(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var trimmed = path.Trim();
            if (!_fileSystem.DirectoryExists(trimmed))
            {
                return;
            }

            if (seen.Add(trimmed))
            {
                candidates.Add(trimmed);
            }
        }

        Consider(dirPath);
        if (!string.IsNullOrWhiteSpace(archivePath))
        {
            Consider(_fileSystem.GetDirectoryName(archivePath.Trim()));
        }

        // 旧 dirPath 里的 workspace 结构（...steamappsworkshopcontent<appid><modId>）最可信：
        // appid 与 modId 都取自原值，把它原样挂到每个 Steam 库根上即可。
        var workshopTail = ExtractWorkshopTail(dirPath);
        var workshopId = SteamWorkshop.GetWorkshopId(steamId, gameRegistryId);
        var appId = game.SteamAppId?.ToString(CultureInfo.InvariantCulture);

        foreach (var library in _steamLibraries.Value)
        {
            if (workshopTail is not null)
            {
                Consider(Path.Combine(library, SteamAppsSegment, workshopTail));
            }

            if (appId is not null && workshopId is not null)
            {
                Consider(Path.Combine(library, SteamAppsSegment, WorkshopSegment, ContentSegment, appId, workshopId));
            }
        }

        // InstallRoot 形如 <library>steamappscommon<game>，再上两级就是 <library>steamapps：
        // 用它兜底 SteamLibraryLocator 漏检的主库。
        var steamAppsRoot = TryGetSteamAppsRoot(game.InstallRoot);
        if (steamAppsRoot is not null)
        {
            if (workshopTail is not null)
            {
                Consider(Path.Combine(steamAppsRoot, workshopTail));
            }

            if (appId is not null && workshopId is not null)
            {
                Consider(Path.Combine(steamAppsRoot, WorkshopSegment, ContentSegment, appId, workshopId));
            }
        }

        return candidates;
    }

    private string? ResolveInDirectory(string directory, string? picture)
    {
        // 描述文件里显式声明的图片名优先：它可能是任意名字（含中文、空格、单引号、子目录、.png.png 双扩展名）。
        if (!string.IsNullOrWhiteSpace(picture))
        {
            var normalized = picture.Trim()
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            var declared = Path.Combine(directory, normalized);
            if (IsImageFile(declared) && _fileSystem.FileExists(declared))
            {
                return declared;
            }

            // picture 里带子目录但实际文件平铺在 mod 根目录时，退化到只用文件名再试一次。
            var fileName = Path.GetFileName(normalized);
            if (!string.IsNullOrWhiteSpace(fileName) && !string.Equals(fileName, normalized, StringComparison.Ordinal))
            {
                var flattened = Path.Combine(directory, fileName);
                if (IsImageFile(flattened) && _fileSystem.FileExists(flattened))
                {
                    return flattened;
                }
            }
        }

        return PickThumbnailFile(directory);
    }

    /// <summary>
    /// 在目录里挑 thumbnail* 图片：精确 thumbnail.png 最优先，其余按扩展名优先级、最后按文件名。
    /// 必须枚举而不是硬拼文件名：真机存在 thumbnail.jpg / Thumbnail.png / thumbnail_new.png / thumbanail.png，
    /// 也有 "empty thumbnail.png" 这种同目录干扰项，以及 thumbnail.pdn 之类不能渲染的文件。
    /// </summary>
    /// <param name="directory">mod 目录（不存在或不可读时返回 null）。</param>
    /// <returns>挑中的图片绝对路径；没有可渲染图片时返回 null。</returns>
    public string? PickThumbnailFile(string directory)
    {
        var files = _fileSystem.EnumerateFiles(directory, ThumbnailPattern, recursive: false)
            .Where(IsImageFile)
            .ToList();
        if (files.Count == 0)
        {
            return null;
        }

        files.Sort(CompareThumbnailFiles);
        return files[0];
    }

    private static int CompareThumbnailFiles(string left, string right)
    {
        var rank = RankThumbnailFile(left).CompareTo(RankThumbnailFile(right));
        return rank != 0
            ? rank
            : StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(left), Path.GetFileName(right));
    }

    private static int RankThumbnailFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.Equals(Path.GetFileName(path), PreferredThumbnailFileName, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var tier = string.Equals(Path.GetFileNameWithoutExtension(path), "thumbnail", StringComparison.OrdinalIgnoreCase)
            ? 10
            : 20;
        return tier + RankImageExtension(extension);
    }

    private static int RankImageExtension(string extension)
    {
        for (var index = 0; index < ImageExtensions.Length; index++)
        {
            if (string.Equals(ImageExtensions[index], extension, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return ImageExtensions.Length;
    }

    private static bool IsImageFile(string path)
    {
        foreach (var extension in ImageExtensions)
        {
            if (string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ExtractWorkshopTail(string? dirPath)
    {
        if (string.IsNullOrWhiteSpace(dirPath))
        {
            return null;
        }

        var segments = dirPath.Trim().Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            if (!string.Equals(segments[index], SteamAppsSegment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var count = segments.Length - index - 1;
            if (count < MinimumWorkshopTailSegments)
            {
                return null;
            }

            return string.Join(Path.DirectorySeparatorChar, segments, index + 1, count);
        }

        return null;
    }

    private string? TryGetSteamAppsRoot(string? installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Path.Combine(installRoot.Trim(), "..", ".."));
        }
        catch (Exception exception) when (exception is ArgumentException
                                              or NotSupportedException
                                              or PathTooLongException
                                              or IOException
                                              or UnauthorizedAccessException)
        {
            _logger.Warn("mod 缩略图：游戏安装路径不合法，跳过 steamapps 推导：" + installRoot + "：" + exception.Message);
            return null;
        }
    }
}
