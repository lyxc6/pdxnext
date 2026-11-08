namespace PdxLauncher.Core.Playsets;

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Parsing;

/// <summary>
/// <see cref="IModCatalog"/> 实现：把"磁盘上的 mod 描述文件"与"launcher-v2.sqlite 的 mods 表"合并成一份目录。
/// <para>CK3/Stellaris：扫 &lt;gameDataDir&gt;\mod\*.mod（Clausewitz 解析）。</para>
/// <para>V3：扫 workshop 目录 &lt;...&gt;\workshop\content\&lt;appid&gt;\*\\.metadata\\metadata.json，
/// 以及 &lt;gameDataDir&gt;\mod\*\\.metadata\\metadata.json（本地 mod）。</para>
/// <para>合并键：gameRegistryId（如 mod/ugc_2218867072.mod）→ dirPath（绝对化后大小写不敏感）→ steamId。</para>
/// </summary>
public sealed class ModCatalog : IModCatalog, IScanCacheInvalidator
{
    private const string ModDescriptorPattern = "*.mod";
    private const string MetadataFileName = "metadata.json";
    private const string MetadataDirectoryName = ".metadata";
    private const string WorkshopSegment = "workshop";
    private const string ContentSegment = "content";

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly ModThumbnailResolver _thumbnails;
    private readonly DiscoveryCache<IReadOnlyList<PlaySetMod>> _cache;

    /// <summary>创建 mod 目录。</summary>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="logger">日志。</param>
    /// <param name="thumbnails">缩略图解析器；为 null 时自行创建（Steam 库列表延迟读取）。</param>
    /// <param name="clock">时钟，用于扫描结果缓存的 TTL；为 null 时用系统时钟。</param>
    public ModCatalog(
        IFileSystem fileSystem,
        IAppLogger logger,
        ModThumbnailResolver? thumbnails = null,
        IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _logger = logger;
        _thumbnails = thumbnails ?? new ModThumbnailResolver(fileSystem, logger);
        _cache = new DiscoveryCache<IReadOnlyList<PlaySetMod>>(clock ?? new SystemClock());
    }

    /// <inheritdoc />
    /// <remarks>
    /// 结果按「gameId + gameDataDir」缓存 60 秒：切一次游戏时 <c>ModsViewModel</c> 与
    /// <c>ModLibraryService</c> 会各扫一遍，不缓存就等于把 mod 目录遍历两次。
    /// 用户显式刷新走 <see cref="InvalidateCache"/>。
    /// </remarks>
    public Task<IReadOnlyList<PlaySetMod>> DiscoverAllAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        var key = CacheKeyFor(game);
        return _cache.GetOrCreateAsync(
            key,
            () => Task.Run(() => Discover(game, cancellationToken), cancellationToken),
            cancellationToken);
    }

    /// <summary>让某款游戏的 mod 扫描缓存失效（用户点刷新、或刚装/删了 mod 时调用）。</summary>
    /// <param name="game">游戏安装信息。</param>
    public void InvalidateCache(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _cache.Invalidate(CacheKeyFor(game));
    }

    /// <summary>清空全部 mod 扫描缓存（设置页「清缓存」用）。</summary>
    public void ClearCache() => _cache.Clear();

    private static string CacheKeyFor(GameInstallation game) =>
        game.GameId + "|" + (game.GameDataDir ?? string.Empty);

    private IReadOnlyList<PlaySetMod> Discover(GameInstallation game, CancellationToken cancellationToken)
    {
        var sqliteRows = ReadSqliteMods(game);
        var discovered = new List<ModRecord>();

        ScanModDescriptors(game, discovered, cancellationToken);
        ScanMetadataFiles(game, discovered, cancellationToken);

        // 合并：先按 sqlite 行匹配，未匹配的 sqlite 行也纳入结果（例如目录已删但仍登记在库里）。
        var usedSqlite = new HashSet<int>();
        var merged = new List<PlaySetMod>(discovered.Count + sqliteRows.Count);

        foreach (var record in discovered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matchIndex = FindSqliteMatch(sqliteRows, record);
            ModRecord? sqlite = null;
            if (matchIndex >= 0)
            {
                usedSqlite.Add(matchIndex);
                sqlite = sqliteRows[matchIndex];
            }

            merged.Add(Compose(game, record, sqlite, merged.Count));
        }

        for (var i = 0; i < sqliteRows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (usedSqlite.Contains(i))
            {
                continue;
            }

            merged.Add(Compose(game, record: null, sqliteRows[i], merged.Count));
        }

        merged.Sort(static (left, right) =>
        {
            var byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : left.ModId.CompareTo(right.ModId);
        });

        // 排序后 position 需要重排，保证调用方看到的位置是连续且稳定的。
        var normalized = new List<PlaySetMod>(merged.Count);
        for (var i = 0; i < merged.Count; i++)
        {
            normalized.Add(merged[i] with { Position = i });
        }

        _logger.Info(
            "mod 扫描完成：" + game.GameId + "，磁盘描述文件 " +
            discovered.Count.ToString(CultureInfo.InvariantCulture) + " 个，sqlite mods 表 " +
            sqliteRows.Count.ToString(CultureInfo.InvariantCulture) + " 行，合并后 " +
            normalized.Count.ToString(CultureInfo.InvariantCulture) + " 个。");
        return normalized;
    }

    // ---------------------------------------------------------------- 磁盘扫描

    private void ScanModDescriptors(GameInstallation game, List<ModRecord> discovered, CancellationToken cancellationToken)
    {
        var modDir = game.ModDir;
        if (string.IsNullOrWhiteSpace(modDir) || !_fileSystem.DirectoryExists(modDir))
        {
            return;
        }

        var files = _fileSystem.EnumerateFiles(modDir, ModDescriptorPattern, recursive: false).ToList();
        files.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = _fileSystem.ReadAllText(file);
                var values = ClausewitzParser.Parse(text);
                var registryId = "mod/" + _fileSystem.GetFileName(file);
                var dirPath = PathHelper.ResolveMaybeRelative(game.GameDataDir, Read(values, "path"));
                var archivePath = PathHelper.ResolveMaybeRelative(game.GameDataDir, Read(values, "archive"));

                discovered.Add(new ModRecord(
                    Name: Read(values, "name"),
                    DirPath: dirPath,
                    ArchivePath: archivePath,
                    GameRegistryId: registryId,
                    SteamId: Read(values, "remote_file_id"),
                    RequiredVersion: Read(values, "supported_version"),
                    ModVersion: Read(values, "version"),
                    ThumbnailPath: null,
                    Status: null,
                    Picture: Read(values, "picture"),
                    // .mod 里带 remote_file_id 的是工坊订阅；没带的是用户自己放的本地 mod。
                    Source: string.IsNullOrWhiteSpace(Read(values, "remote_file_id"))
                        ? ModOrigins.LocalSource
                        : ModOrigins.SteamSource));
            }
            catch (IOException exception)
            {
                _logger.Warn("读取 .mod 描述文件失败，已跳过：" + file + "：" + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.Warn("读取 .mod 描述文件无权限，已跳过：" + file + "：" + exception.Message);
            }
        }
    }

    private void ScanMetadataFiles(GameInstallation game, List<ModRecord> discovered, CancellationToken cancellationToken)
    {
        // 1) workshop：<...>/workshop/content/<appid>/<modId>/.metadata/metadata.json
        foreach (var workshopDirectory in EnumerateWorkshopDirectories(game))
        {
            if (!_fileSystem.DirectoryExists(workshopDirectory))
            {
                continue;
            }

            foreach (var modDirectory in _fileSystem.EnumerateDirectories(workshopDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddMetadataMod(game, modDirectory, discovered, isWorkshopRoot: true);
            }
        }

        // 2) 本地 mod：<gameDataDir>/mod/<dir>/.metadata/metadata.json
        var localModRoot = game.ModDir;
        if (!string.IsNullOrWhiteSpace(localModRoot) && _fileSystem.DirectoryExists(localModRoot))
        {
            foreach (var modDirectory in _fileSystem.EnumerateDirectories(localModRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddMetadataMod(game, modDirectory, discovered, isWorkshopRoot: false);
            }
        }
    }

    /// <summary>
    /// 把一个带 .metadata/metadata.json 的 mod 目录解析成 <see cref="ModRecord"/>。
    /// </summary>
    /// <param name="game">游戏安装信息。</param>
    /// <param name="modDirectory">mod 目录（workshop 的 content/&lt;appid&gt;/&lt;modId&gt; 或 &lt;gameDataDir&gt;\mod\&lt;dir&gt;）。</param>
    /// <param name="discovered">结果收集列表。</param>
    /// <param name="isWorkshopRoot">该目录是否位于 Steam 创意工坊的 content 目录下。</param>
    private void AddMetadataMod(
        GameInstallation game,
        string modDirectory,
        List<ModRecord> discovered,
        bool isWorkshopRoot)
    {
        var metadataPath = Path.Combine(modDirectory, MetadataDirectoryName, MetadataFileName);
        if (!_fileSystem.FileExists(metadataPath))
        {
            return;
        }

        try
        {
            var rawBytes = _fileSystem.ReadAllBytes(metadataPath);
            using var document = JsonDocument.Parse(JsonBytes.WithoutBom(rawBytes));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _logger.Warn("mod metadata.json 根节点不是对象，已跳过：" + metadataPath);
                return;
            }

            var name = ReadJsonProperty(root, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                // 真机实测有 2 个 mod 的 metadata.json name 为空，回退成目录名，避免出现无名条目。
                name = _fileSystem.GetFileName(modDirectory);
            }

            var steamId = ResolveSteamId(ReadJsonProperty(root, "id"), modDirectory, isWorkshopRoot, metadataPath);

            discovered.Add(new ModRecord(
                Name: name,
                DirPath: modDirectory,
                ArchivePath: null,
                GameRegistryId: string.Empty,
                SteamId: steamId,
                RequiredVersion: ReadJsonProperty(root, "supported_game_version"),
                // metadata.json 的 version 是作者自己写的 mod 版本；Vic3 大量 mod 没写
                // supported_game_version，但这一项常常有值（官方启动器把它抄进 mods.metadataVersion）。
                ModVersion: ReadJsonProperty(root, "version"),
                ThumbnailPath: null,
                Status: null,
                // 真机实测：EU5 的 metadata.json 用的是 "picture"（13/22 个有，9 个没有）；
                // "thumbnail" 只是历史写法，保留作兼容。封面路径相对 .metadata 目录。
                Picture: FirstNonEmptyOrNull(
                    ReadJsonProperty(root, "picture"),
                    ReadJsonProperty(root, "thumbnail"),
                    null)?.Trim(),
                // metadata.json 里没有来源字段：位置就是来源（工坊 content 目录 vs 本地 mod 目录）。
                Source: isWorkshopRoot ? ModOrigins.SteamSource : ModOrigins.LocalSource));
        }
        catch (JsonException exception)
        {
            _logger.Warn("mod metadata.json 解析失败，已跳过：" + metadataPath + "：" + exception.Message);
        }
        catch (IOException exception)
        {
            _logger.Warn("读取 mod metadata.json 失败，已跳过：" + metadataPath + "：" + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Warn("读取 mod metadata.json 无权限，已跳过：" + metadataPath + "：" + exception.Message);
        }
    }

    /// <summary>
    /// 解析 mod 的创意工坊 id（纯 ASCII 数字）。
    /// <para>
    /// metadata.json 的 <c>"id"</c> <b>不是</b>创意工坊 id：官方启动器把它写在 <c>mods.metadataId</c> 里，
    /// 真机有 "akagilnc@gmail.com-daoyu-cheat-113"、"tech.res"、"ecchi_redux" 这类 PDX 侧 id。
    /// 工坊 id 只有一个可靠来源：工坊目录名（workshop/content/&lt;appid&gt;/&lt;workshopId&gt;）；
    /// 其次才是本身恰好是纯数字的 metadata id（解压安装的工坊副本）。
    /// 本地 mod 目录（&lt;gameDataDir&gt;\mod\&lt;dir&gt;）不拿目录名编造工坊 id。
    /// </para>
    /// </summary>
    private string? ResolveSteamId(
        string? metadataId,
        string modDirectory,
        bool isWorkshopRoot,
        string metadataPath)
    {
        var fromMetadata = SteamWorkshop.IsAllDigits(metadataId?.Trim()) ? metadataId!.Trim() : null;
        if (!isWorkshopRoot)
        {
            // 本地 mod 只有「metadata id 本身是数字」时才可能是工坊 id。
            return fromMetadata;
        }

        var folderName = _fileSystem.GetFileName(modDirectory)?.Trim();
        if (SteamWorkshop.IsAllDigits(folderName))
        {
            return folderName;
        }

        if (fromMetadata is null)
        {
            // 目录名与 metadata id 都不是数字：这个 mod 拿不到创意工坊页，记一条 Warn 便于真机排障。
            _logger.Warn("工坊 mod 目录名与 metadata id 都不是数字，无法确定创意工坊 id：" + metadataPath);
        }

        return fromMetadata;
    }

    /// <summary>
    /// 推导 workshop 的 content 目录。优先用 InstallRoot 的固定相对结构
    /// （&lt;library&gt;\steamapps\common\&lt;dir&gt; → &lt;library&gt;\steamapps\workshop\content\&lt;appid&gt;），
    /// 再用 sqlite 里已登记的 dirPath 反推（防止库路径与安装路径不同源）。
    /// </summary>
    private IReadOnlyList<string> EnumerateWorkshopDirectories(GameInstallation game)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(game.InstallRoot))
        {
            try
            {
                var steamApps = Path.GetFullPath(Path.Combine(game.InstallRoot, "..", ".."));
                var contentRoot = Path.Combine(steamApps, WorkshopSegment, ContentSegment);
                if (game.SteamAppId is { } appId)
                {
                    candidates.Add(Path.Combine(contentRoot, appId.ToString(CultureInfo.InvariantCulture)));
                }
                else if (_fileSystem.DirectoryExists(contentRoot))
                {
                    foreach (var directory in _fileSystem.EnumerateDirectories(contentRoot))
                    {
                        candidates.Add(directory);
                    }
                }
            }
            catch (ArgumentException exception)
            {
                _logger.Warn("推导 workshop 目录失败：" + exception.Message);
            }
            catch (NotSupportedException exception)
            {
                _logger.Warn("推导 workshop 目录失败：" + exception.Message);
            }
        }

        // 从 sqlite 的 dirPath 反推：<content>/<appid>/<modId> 的父目录就是 <appid> 目录。
        foreach (var row in ReadSqliteMods(game))
        {
            var dirPath = row.DirPath;
            if (string.IsNullOrWhiteSpace(dirPath))
            {
                continue;
            }

            var parent = Path.GetDirectoryName(dirPath);
            if (string.IsNullOrEmpty(parent))
            {
                continue;
            }

            if (parent.Contains(WorkshopSegment + Path.DirectorySeparatorChar + ContentSegment, StringComparison.OrdinalIgnoreCase)
                || parent.Replace('/', Path.DirectorySeparatorChar).Contains(
                    WorkshopSegment + Path.DirectorySeparatorChar + ContentSegment,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!candidates.Contains(parent, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(parent);
                }
            }
        }

        return candidates;
    }

    // ---------------------------------------------------------------- sqlite

    private List<ModRecord> ReadSqliteMods(GameInstallation game)
    {
        var databasePath = PlaySetRepository.GetDatabasePath(game);
        if (!_fileSystem.FileExists(databasePath))
        {
            return new List<ModRecord>();
        }

        try
        {
            using var connection = SqliteConnection.OpenReadOnly(databasePath);
            // SELECT *：不同游戏/版本的 mods 表列不完全一致（旧库没有 metadataVersion），
            // 缺列由 ReadNullableString 当 null 处理，不让一个缺列把整份扫描变成空。
            var rows = connection.Query("SELECT * FROM mods");

            var result = new List<ModRecord>(rows.Count);
            foreach (var row in rows)
            {
                var id = PlaySetRepository.ReadNullableString(row, "id");
                result.Add(new ModRecord(
                    Name: PlaySetRepository.ReadString(row, "displayName"),
                    DirPath: PlaySetRepository.ReadNullableString(row, "dirPath"),
                    ArchivePath: PlaySetRepository.ReadNullableString(row, "archivePath"),
                    GameRegistryId: PlaySetRepository.ReadString(row, "gameRegistryId"),
                    SteamId: PlaySetRepository.ReadNullableString(row, "steamId"),
                    RequiredVersion: PlaySetRepository.ReadString(row, "requiredVersion"),
                    // 官方启动器把 metadata.json 的 version 抄进 metadataVersion；
                    // 更老的库里可能只有 version 列，依次兜底。
                    ModVersion: FirstNonEmptyOrNull(
                        PlaySetRepository.ReadNullableString(row, "metadataVersion"),
                        PlaySetRepository.ReadNullableString(row, "version"),
                        null),
                    ThumbnailPath: PlaySetRepository.ReadNullableString(row, "thumbnailPath"),
                    Status: PlaySetRepository.ReadNullableString(row, "status"),
                    Picture: null,
                    Source: PlaySetRepository.ReadNullableString(row, "source")) with
                {
                    ModId = string.IsNullOrEmpty(id) ? null : Guid.TryParse(id, out var parsed) ? parsed : null,
                });
            }

            return result;
        }
        catch (SqliteException exception)
        {
            _logger.Warn("读取 mods 表失败，仅使用磁盘扫描结果：" + exception.Message);
            return new List<ModRecord>();
        }
        catch (IOException exception)
        {
            _logger.Warn("读取 mods 表失败，仅使用磁盘扫描结果：" + exception.Message);
            return new List<ModRecord>();
        }
    }

    private static int FindSqliteMatch(List<ModRecord> sqliteRows, ModRecord record)
    {
        var dirKey = PathHelper.NormalizeKey(record.DirPath);
        var registryKey = NormalizeRegistry(record.GameRegistryId);
        var steamKey = NormalizeSteam(record.SteamId);

        for (var i = 0; i < sqliteRows.Count; i++)
        {
            var candidate = sqliteRows[i];
            if (dirKey.Length > 0 && dirKey == PathHelper.NormalizeKey(candidate.DirPath))
            {
                return i;
            }

            if (registryKey.Length > 0 && registryKey == NormalizeRegistry(candidate.GameRegistryId))
            {
                return i;
            }
        }

        if (steamKey.Length > 0)
        {
            for (var i = 0; i < sqliteRows.Count; i++)
            {
                if (steamKey == NormalizeSteam(sqliteRows[i].SteamId))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private PlaySetMod Compose(GameInstallation game, ModRecord? record, ModRecord? sqlite, int position)
    {
        var name = FirstNonEmptyOrNull(
            record?.Name,
            sqlite?.Name,
            record?.DirPath is null ? null : _fileSystem.GetFileName(record.DirPath)) ?? "Unknown Mod";
        var dirPath = FirstNonEmpty(record?.DirPath, sqlite?.DirPath, string.Empty);
        var archivePath = FirstNonEmpty(record?.ArchivePath, sqlite?.ArchivePath, null);
        var registryId = FirstNonEmpty(record?.GameRegistryId, sqlite?.GameRegistryId, string.Empty);
        var requiredVersion = FirstNonEmpty(record?.RequiredVersion, sqlite?.RequiredVersion, string.Empty);
        var modVersion = FirstNonEmpty(record?.ModVersion, sqlite?.ModVersion, string.Empty);
        var modId = sqlite?.ModId ?? CreateDeterministicModId(registryId, dirPath, archivePath, name);

        // .mod 的 remote_file_id 与 metadata.json 的 id 里都可能带创意工坊 id，但 metadata.json 的 id
        // 常是 PDX 侧 id 串（"tech.res"、"akagilnc@gmail.com-daoyu-cheat-113"）：只有纯数字才是工坊 id，
        // 必须让库里的数字 steamId 顶上去，否则已登记的 mod 在播放集页也会丢掉工坊按钮。
        var steamId = FirstNonEmptyOrNull(
            DigitsOrNull(record?.SteamId),
            DigitsOrNull(sqlite?.SteamId),
            FirstNonEmptyOrNull(record?.SteamId, sqlite?.SteamId, null));

        return new PlaySetMod(
            ModId: modId,
            Name: name,
            DirPath: dirPath,
            ArchivePath: archivePath,
            GameRegistryId: registryId,
            ThumbnailPath: ResolveThumbnail(game, record, sqlite, dirPath, archivePath, registryId, steamId, modId),
            RequiredVersion: requiredVersion,
            Status: ResolveStatus(record, sqlite, dirPath),
            // 目录扫描没有播放集上下文：Enabled 表示"该 mod 可用"，真实启用状态请用
            // IPlaySetRepository.GetPlaySetModsAsync 获取。
            Enabled: true,
            Position: position,
            SteamId: steamId,
            // 两边哪边有 source 用哪边；都没有（纯磁盘扫描）就按有没有工坊 id 判。
            Source: ModOrigins.Normalize(FirstNonEmptyOrNull(record?.Source, sqlite?.Source, null), steamId) == ModOrigin.Subscribed
                ? ModOrigins.SteamSource
                : ModOrigins.LocalSource,
            ModVersion: modVersion);
    }

    /// <summary>
    /// 缩略图解析：交给 <see cref="ModThumbnailResolver"/>，它负责 Steam 库重定位、picture 优先与多扩展名。
    /// </summary>
    private string? ResolveThumbnail(
        GameInstallation game,
        ModRecord? record,
        ModRecord? sqlite,
        string dirPath,
        string? archivePath,
        string registryId,
        string? steamId,
        Guid modId)
        => _thumbnails.Resolve(
            game,
            dirPath: dirPath,
            archivePath: archivePath,
            picture: FirstNonEmptyOrNull(record?.Picture, sqlite?.Picture, null),
            steamId: steamId,
            gameRegistryId: registryId,
            cachedThumbnailPath: sqlite?.ThumbnailPath,
            modId: modId);

    private string ResolveStatus(ModRecord? record, ModRecord? sqlite, string dirPath)
    {
        var status = sqlite?.Status;
        if (!string.IsNullOrWhiteSpace(status))
        {
            return status;
        }

        if (!string.IsNullOrWhiteSpace(dirPath))
        {
            return _fileSystem.DirectoryExists(dirPath) ? "ready_to_play" : "missing";
        }

        return "missing";
    }

    /// <summary>
    /// 不在 sqlite 里的 mod 也需要一个稳定 id：用注册 id / 目录 / 归档 / 名字的 MD5 生成，
    /// 保证同一 mod 每次扫描得到同一个 Guid（UI 选中状态、播放集保存都依赖它稳定）。
    /// </summary>
    private static Guid CreateDeterministicModId(string registryId, string dirPath, string? archivePath, string name)
    {
        var key = NormalizeRegistry(registryId) + "|" + PathHelper.NormalizeKey(dirPath) + "|" +
                  PathHelper.NormalizeKey(archivePath) + "|" + name.ToUpperInvariant();
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string NormalizeRegistry(string? registryId)
        => string.IsNullOrWhiteSpace(registryId)
            ? string.Empty
            : registryId.Trim().Replace('\\', '/').ToUpperInvariant();

    private static string NormalizeSteam(string? steamId) => string.IsNullOrWhiteSpace(steamId) ? string.Empty : steamId.Trim();

    /// <summary>
    /// 只有纯 ASCII 数字才是创意工坊 id。PDX 侧 id（"tech.res"、"…@126.com"）不参与工坊地址拼装，
    /// 但可能被其它调用方当普通字符串用，所以 <see cref="Compose"/> 里仍保留原始值兜底。
    /// </summary>
    private static string? DigitsOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return SteamWorkshop.IsAllDigits(trimmed) ? trimmed : null;
    }

    private static string FirstNonEmpty(string? first, string? second, string? third)
    {
        if (!string.IsNullOrWhiteSpace(first))
        {
            return first;
        }

        if (!string.IsNullOrWhiteSpace(second))
        {
            return second;
        }

        return string.IsNullOrWhiteSpace(third) ? string.Empty : third;
    }

    private static string? FirstNonEmptyOrNull(string? first, string? second, string? third)
    {
        if (!string.IsNullOrWhiteSpace(first))
        {
            return first;
        }

        if (!string.IsNullOrWhiteSpace(second))
        {
            return second;
        }

        return string.IsNullOrWhiteSpace(third) ? null : third;
    }

    private static string Read(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && value is not null ? value.Trim() : string.Empty;

    private static string? ReadJsonProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }

    /// <summary>一条 mod 记录（磁盘扫描结果或 sqlite 行），合并前的中转形态。</summary>
    private sealed record ModRecord(
        string Name,
        string? DirPath,
        string? ArchivePath,
        string GameRegistryId,
        string? SteamId,
        string? RequiredVersion,
        string? ThumbnailPath,
        string? Status,
        string? Picture,
        string? Source = null,
        string? ModVersion = null)
    {
        /// <summary>sqlite 行的主键；磁盘扫描得到的记录为 null。</summary>
        public Guid? ModId { get; init; }
    }
}
