namespace PdxLauncher.Core.Dlc;

using System.Globalization;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Parsing;
using PdxLauncher.Core.Playsets;

/// <summary>
/// <see cref="IDlcCatalog"/> 实现：扫描游戏安装目录下的 DLC 描述文件。
/// <para>V0（CK3/Stellaris）：递归扫 &lt;DlcPath&gt;/**/*.dlc，用 Clausewitz 解析。</para>
/// <para>V1/V1_1（V3）：递归扫 &lt;DlcPath&gt;/**/*.dlc.json，用 JSON 解析；找不到 .dlc.json 时回退扫 .dlc。</para>
/// <para>容错：坏文件、缺必填键一律跳过并记 Warn，绝不抛异常。</para>
/// </summary>
public sealed class DlcCatalog : IDlcCatalog, IScanCacheInvalidator
{
    private const string ClausewitzPattern = "*.dlc";
    private const string JsonPattern = "*.dlc.json";
    private const string DlcMetadataDirectoryName = "dlc_metadata";
    private const string DlcMetadataPattern = "*.txt";
    /// <summary>
    /// 同目录缩略图的候选文件名（按优先级）。
    /// 旧游戏的 DLC 都是 <c>thumbnail.png</c>；EU5 的 DLC 只有 <c>thumbnail.dds</c>，
    /// 由 App 侧转成 PNG 后再交给 WinUI 渲染。
    /// </summary>
    private static readonly string[] ThumbnailFileNames =
    [
        "thumbnail.png",
        "thumbnail.jpg",
        "thumbnail.jpeg",
        "thumbnail.webp",
        "thumbnail.bmp",
        "thumbnail.dds",
    ];

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly DiscoveryCache<IReadOnlyList<DlcInfo>> _cache;

    /// <summary>创建 DLC 目录。</summary>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="logger">日志。</param>
    /// <param name="clock">时钟，用于扫描结果缓存的 TTL；为 null 时用系统时钟。</param>
    public DlcCatalog(IFileSystem fileSystem, IAppLogger logger, IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _logger = logger;
        _cache = new DiscoveryCache<IReadOnlyList<DlcInfo>>(clock ?? new SystemClock());
    }

    /// <summary>
    /// 官方 Jr.getDlcId 语义：pops_id 优先，空则退化到相对 dlcPath 的 POSIX 路径。
    /// <para>真机 Stellaris 有 4 个 .dlc 的 pops_id 是空字符串（Galactic Paragons / Astral Planes /
    /// Rick The Cube Species Portrait / Stargazer Species Portrait），此时 <see cref="DlcInfo.PopsId"/>
    /// 为空，UI 与启用状态映射必须改用本方法返回的有效 id，否则这 4 个会撞成同一个 key。</para>
    /// </summary>
    /// <param name="dlc">DLC 条目。</param>
    /// <returns>有效 id：<see cref="DlcInfo.PopsId"/> 非空时原样返回，否则返回 <see cref="DlcInfo.FilePathRelativeToDlcPath"/>。</returns>
    public static string GetEffectiveDlcId(DlcInfo dlc)
    {
        ArgumentNullException.ThrowIfNull(dlc);
        return string.IsNullOrEmpty(dlc.PopsId) ? dlc.FilePathRelativeToDlcPath : dlc.PopsId;
    }

    /// <summary>
    /// LFV1/1.1 的 <c>content_load.json</c> 里 <c>disabledDLC[].paradoxAppId</c> 只能表达有 pops_id 的 DLC：
    /// 官方 app.asar 的 X2.mapPopsId 会对 pops_id 为空的条目做 <c>.filter(fe.identity)</c> 把它丢掉。
    /// <para>因此 pops_id 为空的 DLC 在 LFV1/1.1 下无法被禁用 —— 这是官方行为，不要伪造假 id 写进 load 文件。</para>
    /// </summary>
    /// <param name="dlc">DLC 条目。</param>
    /// <returns>有 pops_id 时 true；pops_id 为空时 false（该 DLC 无法出现在 disabledDLC 里）。</returns>
    public static bool CanBeDisabledInContentLoad(DlcInfo dlc)
    {
        ArgumentNullException.ThrowIfNull(dlc);
        return !string.IsNullOrEmpty(dlc.PopsId);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 结果按「gameId + 扫描目录 + 格式」缓存 60 秒：一次切换游戏时主页、DLC 页、mod 页会各调一遍，
    /// 不缓存就会把同一棵目录树遍历 2-3 次（日志里同一次切换出现多条「DLC 扫描完成」）。
    /// 用户显式刷新走 <see cref="InvalidateCache"/>。
    /// </remarks>
    public Task<IReadOnlyList<DlcInfo>> DiscoverAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        var key = CacheKeyFor(game);
        return _cache.GetOrCreateAsync(
            key,
            () => Task.Run(() => Discover(game, cancellationToken), cancellationToken),
            cancellationToken);
    }

    /// <summary>让某款游戏的 DLC 扫描缓存失效（用户点刷新、或刚装/删了 DLC 时调用）。</summary>
    /// <param name="game">游戏安装信息。</param>
    public void InvalidateCache(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _cache.Invalidate(CacheKeyFor(game));
    }

    /// <summary>清空全部 DLC 扫描缓存（设置页「清缓存」用）。</summary>
    public void ClearCache() => _cache.Clear();

    private static string CacheKeyFor(GameInstallation game) =>
        game.GameId + "|" + (game.DlcPath ?? string.Empty) + "|" + game.Format;

    private IReadOnlyList<DlcInfo> Discover(GameInstallation game, CancellationToken cancellationToken)
    {
        var dlcPath = game.DlcPath;
        if (string.IsNullOrWhiteSpace(dlcPath) || !_fileSystem.DirectoryExists(dlcPath))
        {
            _logger.Warn("DLC 目录不存在，跳过扫描：" + (dlcPath ?? "<null>"));
            return Array.Empty<DlcInfo>();
        }

        var useJson = game.Format
            is LoadFileFormat.ContentLoadV1
            or LoadFileFormat.ContentLoadV1_1
            or LoadFileFormat.PlaysetsJsonV1;
        var result = new List<DlcInfo>();

        // CS2（PlaysetConfigV1）没有 DLC 清单：安装根没有 .dlc / .dlc.json（.packages\ 为空），
        // playset_config.json 里也没有 dlcs 字段，DLC 归属完全在 Steam 授权侧。
        // 直接返回空，不去遍历安装根（否则白扫几万个文件），也绝不伪造数据。
        if (game.Format == LoadFileFormat.PlaysetConfigV1)
        {
            _logger.Info("CS2 没有 DLC 描述文件，DLC 清单为空（归属由 Steam 授权决定）：" + game.GameId);
            return Array.Empty<DlcInfo>();
        }

        if (useJson)
        {
            var localization = game.Format == LoadFileFormat.PlaysetsJsonV1
                ? DlcLocalization.Load(_fileSystem, dlcPath, _logger)
                : new Dictionary<string, string>();
            CollectJson(game, dlcPath, localization, result, cancellationToken);
            if (result.Count == 0)
            {
                // V3 的 dlc 目录里其实同时存在 .dlc 与 .dlc.json；若 JSON 全坏，用 .dlc 兜底。
                _logger.Warn("V1/V1_1 未解析到任何 .dlc.json，回退扫描 .dlc：" + dlcPath);
                CollectClausewitz(game, dlcPath, result, cancellationToken);
            }
        }
        else
        {
            CollectClausewitz(game, dlcPath, result, cancellationToken);
        }

        // VIC3 1.15 起，新 DLC 只写 game/dlc_metadata/*.txt（不再补 .dlc.json），把磁盘上没有的条目补进来。
        // 解析失败只降级为原有的 .dlc.json/.dlc 结果，绝不影响主扫描。
        CollectDlcMetadata(dlcPath, result, cancellationToken);

        result.Sort(static (left, right) => string.CompareOrdinal(left.FilePathRelativeToDlcPath, right.FilePathRelativeToDlcPath));
        _logger.Info("DLC 扫描完成：" + game.GameId + "，共 " + result.Count.ToString(CultureInfo.InvariantCulture) + " 个（目录 " + dlcPath + "）。");
        return result;
    }

    private void CollectClausewitz(
        GameInstallation game,
        string dlcPath,
        List<DlcInfo> result,
        CancellationToken cancellationToken)
    {
        foreach (var file in EnumerateFilesSafe(dlcPath, ClausewitzPattern))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = _fileSystem.ReadAllText(file);
                var values = ClausewitzParser.Parse(text);
                var displayName = Read(values, "name");
                var popsId = Read(values, "pops_id");
                if (string.IsNullOrEmpty(displayName))
                {
                    _logger.Warn("DLC 缺少 name 键，已跳过：" + file);
                    continue;
                }

                // pops_id 可以为空（真机 Stellaris 有 4 个 .dlc 是 "pops_id = \"\""），
                // 官方 getDlcId = getPopsId() || getFilePath() || getUserContentId()，空 pops_id 时由调用方退化为文件路径。
                // 因此这里只要求 name 非空，pops_id 空就原样保留空串，不 Warn 不跳过。
                var rawBytes = _fileSystem.ReadAllBytes(file);
                // Clausewitz 格式（CK3/Stellaris）没有简介字段，留空由 UI 层联网补全。
                result.Add(new DlcInfo(
                    displayName,
                    popsId,
                    Read(values, "steam_id"),
                    PathHelper.ToPosixRelative(dlcPath, file),
                    ResolveThumbnail(dlcPath, file),
                    string.Empty,
                    rawBytes));
            }
            catch (IOException exception)
            {
                _logger.Warn("读取 DLC 文件失败，已跳过：" + file + "：" + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.Warn("读取 DLC 文件无权限，已跳过：" + file + "：" + exception.Message);
            }
        }
    }

    private void CollectJson(
        GameInstallation game,
        string dlcPath,
        IReadOnlyDictionary<string, string> localization,
        List<DlcInfo> result,
        CancellationToken cancellationToken)
    {
        foreach (var file in EnumerateFilesSafe(dlcPath, JsonPattern))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var rawBytes = _fileSystem.ReadAllBytes(file);
                using var document = JsonDocument.Parse(JsonBytes.WithoutBom(rawBytes));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    _logger.Warn("DLC JSON 根节点不是对象，已跳过：" + file);
                    continue;
                }

                var popsId = ReadNestedString(root, "id", "paradox");
                if (string.IsNullOrEmpty(popsId))
                {
                    // EU5 的 <Name>.dlc.json 是平铺结构：没有 id.paradox / displayName，
                    // 只有 name、localizable_name、pops_id、steam_id。
                    if (!TryCollectEu5(file, dlcPath, root, localization, rawBytes, result))
                    {
                        _logger.Warn("DLC JSON 缺少 id.paradox 且不是 EU5 结构，已跳过：" + file);
                    }

                    continue;
                }

                var displayName = ReadNestedString(root, "displayName", "en");
                if (string.IsNullOrEmpty(displayName))
                {
                    _logger.Warn("DLC JSON 缺少 displayName.en（必填），已跳过：" + file);
                    continue;
                }

                var steamId = ReadNestedString(root, "id", "steam") ?? string.Empty;
                var description = ReadNestedString(root, "description", "en") ?? string.Empty;
                result.Add(new DlcInfo(
                    displayName,
                    popsId,
                    steamId,
                    PathHelper.ToPosixRelative(dlcPath, file),
                    ResolveThumbnail(dlcPath, file),
                    description.Trim(),
                    rawBytes));
            }
            catch (JsonException exception)
            {
                _logger.Warn("DLC JSON 解析失败，已跳过：" + file + "：" + exception.Message);
            }
            catch (IOException exception)
            {
                _logger.Warn("读取 DLC JSON 失败，已跳过：" + file + "：" + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.Warn("读取 DLC JSON 无权限，已跳过：" + file + "：" + exception.Message);
            }
        }
    }

    /// <summary>
    /// EU5 的 DLC 描述文件：<c>&lt;DlcPath&gt;\D0xx_name\D0xx_name.dlc.json</c>，平铺字段
    /// <c>name</c> / <c>localizable_name</c> / <c>pops_id</c> / <c>steam_id</c> / <c>hidden</c>。
    /// 显示名优先取英文本地化，取不到时把 <c>d017_sacred_sites_pack</c> 这类内部名美化成 "Sacred Sites Pack"。
    /// 缩略图是 <c>thumbnail.dds</c>，由 App 侧转码后渲染。
    /// </summary>
    /// <returns>识别成功并已加入 <paramref name="result"/> 时 true；不是 EU5 结构时 false。</returns>
    private bool TryCollectEu5(
        string file,
        string dlcPath,
        JsonElement root,
        IReadOnlyDictionary<string, string> localization,
        byte[] rawBytes,
        List<DlcInfo> result)
    {
        var name = ReadStringProperty(root, "name");
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (root.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True)
        {
            // D000_shared 这类共享内容包是 hidden 的，官方启动器也不列出。
            return true;
        }

        var popsId = ReadStringProperty(root, "pops_id");
        if (string.IsNullOrEmpty(popsId))
        {
            popsId = name;
        }

        var localizableKey = ReadStringProperty(root, "localizable_name");
        var displayName = !string.IsNullOrEmpty(localizableKey) && localization.TryGetValue(localizableKey, out var localized)
            ? localized
            : HumanizeEu5Name(name);

        var steamId = root.TryGetProperty("steam_id", out var steamElement) && steamElement.TryGetInt64(out var steamNumber) && steamNumber > 0
            ? steamNumber.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

        result.Add(new DlcInfo(
            displayName,
            popsId,
            steamId,
            PathHelper.ToPosixRelative(dlcPath, file),
            ResolveThumbnail(dlcPath, file),
            string.Empty,
            rawBytes));
        return true;
    }

    /// <summary><c>d017_sacred_sites_pack</c> → <c>Sacred Sites Pack</c>（去掉 D 编号前缀后按词首字母大写）。</summary>
    internal static string HumanizeEu5Name(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length > 3 && (trimmed[0] is 'd' or 'D') && char.IsAsciiDigit(trimmed[1]))
        {
            var index = trimmed.IndexOf('_');
            if (index > 0 && index < trimmed.Length - 1)
            {
                trimmed = trimmed[(index + 1)..];
            }
        }

        var words = trimmed.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(
            ' ',
            words.Select(static word => word.Length == 0
                ? word
                : char.ToUpperInvariant(word[0]) + word[1..]));
    }

    /// <summary>
    /// VIC3 1.15 起，新 DLC 只写进 <c>&lt;DlcPath&gt;/dlc_metadata/*.txt</c>，不再补
    /// <c>game/dlc/&lt;id&gt;/&lt;id&gt;.dlc.json</c>；同目录 <c>dlc_metadata.md</c> 的官方文档约定
    /// 块名与 dlc 文件名一致、图标路径由块名推导：
    /// <code>
    /// dlc019 = { name = "State and Revolution" steam_id = "4314660" type = minor priority = 8 }
    /// </code>
    /// <para>
    /// 只补磁盘上没有的条目：<c>.dlc.json</c>/<c>.dlc</c> 优先，按 steam_id 或块名去重，
    /// 因此已有的 17 个 DLC 不会被重复列出。合成条目没有 RawBytes
    /// （<c>DlcSignatureService</c> 只哈希非空 RawBytes，带字节会改变 dlc_signature）。
    /// </para>
    /// </summary>
    private void CollectDlcMetadata(string dlcPath, List<DlcInfo> result, CancellationToken cancellationToken)
    {
        try
        {
            var directory = FindDlcMetadataDirectory(dlcPath);
            if (directory is null)
            {
                return;
            }

            // 图标按官方文档写在 <game>/gfx/interface/icons/dlc_icons 下；dlcPath 可能指向 game
            // （VIC3 真机）也可能指向 game/dlc，两种布局都取 metadata 目录的上一级（= game 根）。
            var gameRoot = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory)) ?? directory;

            var seenSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenPopsIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dlc in result)
            {
                if (!string.IsNullOrEmpty(dlc.SteamId))
                {
                    seenSteamIds.Add(dlc.SteamId);
                }

                if (!string.IsNullOrEmpty(dlc.PopsId))
                {
                    seenPopsIds.Add(dlc.PopsId);
                }
            }

            var added = 0;
            foreach (var file in EnumerateTopLevelFilesSafe(directory, DlcMetadataPattern))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text;
                try
                {
                    text = _fileSystem.ReadAllText(file);
                }
                catch (IOException exception)
                {
                    _logger.Warn("读取 DLC metadata 失败，已跳过：" + file + "：" + exception.Message);
                    continue;
                }
                catch (UnauthorizedAccessException exception)
                {
                    _logger.Warn("读取 DLC metadata 无权限，已跳过：" + file + "：" + exception.Message);
                    continue;
                }

                foreach (var block in ClausewitzParser.ParseBlocks(text))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = DlcLocalization.CleanMarkup(Read(block.Values, "name"));
                    if (string.IsNullOrEmpty(name))
                    {
                        _logger.Warn("DLC metadata 缺少 name，已跳过：" + file + " -> " + block.Key);
                        continue;
                    }

                    var steamId = Read(block.Values, "steam_id");
                    if (string.IsNullOrEmpty(steamId))
                    {
                        _logger.Warn("DLC metadata 缺少 steam_id，已跳过：" + file + " -> " + block.Key);
                        continue;
                    }

                    // 磁盘描述文件优先：steam_id 或块名（= pops_id）已经存在就跳过，避免重复列出。
                    if (seenSteamIds.Contains(steamId) || seenPopsIds.Contains(block.Key))
                    {
                        continue;
                    }

                    seenSteamIds.Add(steamId);
                    seenPopsIds.Add(block.Key);
                    result.Add(new DlcInfo(
                        name,
                        block.Key,
                        steamId,
                        "dlc_metadata/" + block.Key,
                        ResolveDlcMetadataIcon(gameRoot, block.Key),
                        string.Empty,
                        Array.Empty<byte>()));
                    added++;
                }
            }

            if (added > 0)
            {
                _logger.Info("DLC metadata 补充 " + added.ToString(CultureInfo.InvariantCulture) + " 个磁盘上没有的条目：" + directory);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 新数据源是增量：解析失败只降级为原有的 .dlc.json/.dlc 结果，绝不让整个扫描抛异常。
            _logger.Warn("解析 DLC metadata 失败，已降级为磁盘描述文件结果：" + exception.Message);
        }
    }

    /// <summary>
    /// 找 <c>dlc_metadata</c> 目录：VIC3 真机是 <c>&lt;DlcPath&gt;/dlc_metadata</c>（dlcPath 指向 game）；
    /// 同时兼容 dlcPath 指向 <c>game/dlc</c> 的布局（目录在上一级）。
    /// </summary>
    private string? FindDlcMetadataDirectory(string dlcPath)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(dlcPath));
        string?[] candidates =
        [
            Path.Combine(dlcPath, DlcMetadataDirectoryName),
            string.IsNullOrEmpty(parent) ? null : Path.Combine(parent, DlcMetadataDirectoryName),
        ];
        foreach (var candidate in candidates)
        {
            if (candidate is not null && _fileSystem.DirectoryExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 官方文档：icon 路径 = <c>&lt;game&gt;/gfx/interface/icons/dlc_icons/&lt;块名&gt;.dds</c>；
    /// banner 只给主题选择器用，不取。文件不存在就留空（与 .dlc 缺 thumbnail 时一致）。
    /// </summary>
    /// <param name="gameRoot">game 根目录（metadata 目录的上一级）。</param>
    /// <param name="id">块名，即 dlc 文件名。</param>
    private string ResolveDlcMetadataIcon(string gameRoot, string id)
    {
        var candidate = Path.Combine(gameRoot, "gfx", "interface", "icons", "dlc_icons", id + ".dds");
        return _fileSystem.FileExists(candidate) ? candidate : string.Empty;
    }

    private IEnumerable<string> EnumerateTopLevelFilesSafe(string directory, string pattern)
    {
        // metadata 目录只放 *.txt；不递归，避免把文档/子目录里的备份也算进来。
        var files = _fileSystem.EnumerateFiles(directory, pattern, recursive: false).ToList();
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    private static string ReadStringProperty(JsonElement root, string key)
        => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// 缩略图：与 .dlc/.dlc.json 同目录的 <c>thumbnail.*</c>（存在且格式可渲染才算）。
    /// Stellaris 的 .dlc 里另有 thumbnail 键，但真机实测同目录都存在 thumbnail.png，统一按同目录处理。
    /// </summary>
    private string ResolveThumbnail(string dlcPath, string descriptorPath)
    {
        var directory = Path.GetDirectoryName(descriptorPath);
        if (string.IsNullOrEmpty(directory))
        {
            return string.Empty;
        }

        foreach (var name in ThumbnailFileNames)
        {
            var candidate = Path.Combine(directory, name);
            if (_fileSystem.FileExists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private IEnumerable<string> EnumerateFilesSafe(string directory, string pattern)
    {
        // IFileSystem.EnumerateFiles 自身容错（目录不存在返回空），这里再排一次序保证结果稳定。
        var files = _fileSystem.EnumerateFiles(directory, pattern, recursive: true).ToList();
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    private static string Read(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && value is not null ? value.Trim() : string.Empty;

    private static string? ReadNestedString(JsonElement root, string parentKey, string childKey)
    {
        if (!root.TryGetProperty(parentKey, out var parent) || parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!parent.TryGetProperty(childKey, out var child) || child.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return child.GetString();
    }
}
