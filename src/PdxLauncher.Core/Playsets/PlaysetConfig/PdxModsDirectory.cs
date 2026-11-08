namespace PdxLauncher.Core.Playsets.PlaysetConfig;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// CS2 已下载 mod 的目录扫描：把 <c>.cache\Mods\pdx_mods\&lt;modId&gt;_&lt;version&gt;\</c> 与
/// <c>pdx_mods_cache.json</c> 的元数据合并成可直接喂给 UI 的条目。
/// <para>
/// 真机实测：81~82 个目录里，28 个 modId 同时存在<b>两个版本</b>（如 <c>74151_16</c> 与 <c>74151_18</c>），
/// 所以「一个 mod 一个目录」的假设不成立，必须按下面的三级规则挑一个：
/// </para>
/// <list type="number">
/// <item>播放集条目里明确写了 <c>sourceVersion</c> → 用它。</item>
/// <item>否则用 <c>pdx_mods_cache.json</c> 的 <c>latestVersion</c>（真机 53 项里 version == latestVersion）。</item>
/// <item>否则取版本号最大的目录。</item>
/// </list>
/// </summary>
public sealed class PdxModsDirectory
{
    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;

    /// <summary>创建扫描器。</summary>
    public PdxModsDirectory(IFileSystem fileSystem, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// 扫描已安装的 mod 目录，返回 <c>sourceId → 版本列表（升序）</c>。目录不存在时返回空。
    /// 只认 <c>&lt;数字&gt;_&lt;数字&gt;</c> 形式的目录名，其它（如 <c>.downloading</c>）一律跳过。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ScanVersions(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var root = PlaysetConfigPaths.GetPdxModsRoot(game);
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (!_fileSystem.DirectoryExists(root))
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        }

        foreach (var directory in _fileSystem.EnumerateDirectories(root))
        {
            var name = _fileSystem.GetFileName(directory);
            if (PdxModsCache.ParseFolderName(name) is not { } parsed)
            {
                continue;
            }

            if (!result.TryGetValue(parsed.Id, out var versions))
            {
                versions = [];
                result[parsed.Id] = versions;
            }

            if (!versions.Contains(parsed.Version, StringComparer.Ordinal))
            {
                versions.Add(parsed.Version);
            }
        }

        var ordered = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (id, versions) in result)
        {
            versions.Sort(PdxModsCache.CompareVersions);
            ordered[id] = versions;
        }

        return ordered;
    }

    /// <summary>
    /// 在候选版本里挑一个：优先 <paramref name="preferredVersion"/>，否则 <paramref name="latestVersion"/>，
    /// 否则最大的版本号。没有候选返回 null。
    /// </summary>
    public static string? PickVersion(
        IReadOnlyList<string>? versions,
        string? preferredVersion,
        string? latestVersion)
    {
        if (versions is null || versions.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredVersion)
            && versions.Contains(preferredVersion, StringComparer.Ordinal))
        {
            return preferredVersion;
        }

        if (!string.IsNullOrWhiteSpace(latestVersion)
            && versions.Contains(latestVersion, StringComparer.Ordinal))
        {
            return latestVersion;
        }

        // versions 已升序排列，末位即最大。
        return versions[^1];
    }

    /// <summary>拼出某个版本目录的绝对路径。</summary>
    public static string GetModDirectory(GameInstallation game, string sourceId, string version)
    {
        ArgumentNullException.ThrowIfNull(game);
        return Path.Combine(
            PlaysetConfigPaths.GetPdxModsRoot(game),
            sourceId + "_" + version);
    }

    /// <summary>
    /// 构造一条播放集 mod 条目。
    /// </summary>
    /// <param name="game">游戏安装信息（用于派生确定性 ModId）。</param>
    /// <param name="sourceId">PDX Mods 的数字 id。</param>
    /// <param name="cache">已安装元数据缓存。</param>
    /// <param name="versions">该 mod 在磁盘上的版本目录列表。</param>
    /// <param name="preferredVersion">播放集条目里写的 <c>sourceVersion</c>（可能为 null）。</param>
    /// <param name="enabled">启用状态。</param>
    /// <param name="position">加载顺序（<c>mods[]</c> 的下标）。</param>
    public PlaySetMod Compose(
        GameInstallation game,
        string sourceId,
        PdxModsCache cache,
        IReadOnlyList<string>? versions,
        string? preferredVersion,
        bool enabled,
        int position)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(cache);

        var item = cache.Find(sourceId);
        var version = PickVersion(versions, preferredVersion, item?.LatestVersion);
        var directory = version is null
            ? string.Empty
            : GetModDirectory(game, sourceId, version);

        // 名字优先级：缓存里的 displayName → 作者写的 name → 目录名（modId_version）。
        var name = item is null
            ? string.Empty
            : item.DisplayName.Length > 0
                ? item.DisplayName
                : item.UserModVersion;

        if (name.Length == 0)
        {
            name = version is null ? sourceId : sourceId + "_" + version;
        }

        return new PlaySetMod(
            ModId: PdxModsCache.GetModId(game.GameId, sourceId),
            Name: name,
            DirPath: directory,
            ArchivePath: null,
            GameRegistryId: "pdx:" + sourceId,
            ThumbnailPath: item?.ThumbnailPath,
            RequiredVersion: item?.RequiredGameVersion ?? string.Empty,
            Status: version is null ? "missing" : item?.State ?? "ready_to_play",
            Enabled: enabled,
            Position: position,
            SteamId: null,
            // CS2 播放集条目里的 sourceId 就是 PDX Mods 的平台 id，分享 JSON 要用它。
            PdxId: sourceId);
    }

    /// <summary>
    /// 把整份缓存 + 磁盘目录展开成 Mod 库条目（不带播放集归属，归属由调用方补）。
    /// </summary>
    public IReadOnlyList<(PlaySetMod Mod, PdxModsCacheItem? Item)> ComposeLibrary(
        GameInstallation game,
        PdxModsCache cache)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(cache);

        var versions = ScanVersions(game);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(PlaySetMod, PdxModsCacheItem?)>(versions.Count + 8);
        var position = 0;

        foreach (var sourceId in versions.Keys.OrderBy(static id => id, StringComparer.Ordinal))
        {
            seen.Add(sourceId);
            var item = cache.Find(sourceId);
            result.Add((
                Compose(game, sourceId, cache, versions[sourceId], null, enabled: false, position++),
                item));
        }

        // 缓存里登记过、但磁盘上已经没有目录的（用户删了文件 / 只同步了订阅列表）：仍要列出来，
        // 否则 UI 里会「凭空少一个 mod」，用户以为丢了配置。
        foreach (var item in cache.Items)
        {
            if (seen.Contains(item.Id))
            {
                continue;
            }

            _logger.Info("pdx_mods_cache 中登记的 mod 在磁盘上找不到目录: " + item.Id + " (" + item.DisplayName + ")");
            result.Add((
                Compose(game, item.Id, cache, versions: null, preferredVersion: null, enabled: false, position++),
                item));
        }

        return result;
    }

    /// <summary>数字 id 排序键：先按数值再按文本，保证 74238 排在 74151 之后而不是 "74238" &lt; "7415"。</summary>
    public static int CompareIds(string left, string right)
    {
        var hasLeft = long.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a);
        var hasRight = long.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b);
        if (hasLeft && hasRight)
        {
            var byNumber = a.CompareTo(b);
            if (byNumber != 0)
            {
                return byNumber;
            }
        }

        return string.Compare(left, right, StringComparison.Ordinal);
    }
}
