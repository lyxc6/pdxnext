namespace PdxLauncher.Core.Playsets.PlaysetConfig;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;

/// <summary>
/// CS2（<see cref="LoadFileFormat.PlaysetConfigV1"/>）的播放集仓储：
/// 直接维护 <c>{gameDataDir}\.cache\Mods\playset_config.json</c>。
/// <para>
/// 映射规则（与 sqlite 版的差异全部由 CS2 自己造成）：
/// </para>
/// <list type="bullet">
/// <item>播放集自带真 GUID（<c>id</c>），直接用，不像 EU5 那样从名字哈希。</item>
/// <item>mod 项只有 <c>source + sourceId</c>：<see cref="PlaySetMod.Position"/> 等于 <c>mods[]</c> 下标
/// （<b>数组顺序就是加载顺序</b>，CS2 没有 position 字段）。</item>
/// <item>mod 的版本由目录名承载：<c>pdx_mods\&lt;sourceId&gt;_&lt;version&gt;\</c>，
/// 同一个 mod 可能同时存在多个版本目录，挑选规则见 <see cref="PdxModsDirectory"/>。</item>
/// <item>激活的播放集写顶层 <c>activePlaysetId</c>（官方为空时整键删除）。</item>
/// <item>CS2 的 playset 里没有 DLC 字段，安装根也没有 DLC 清单 —— DLC 相关方法一律空操作，不伪造数据。</item>
/// </list>
/// <para>
/// 写入策略：先抢 <c>playset_config_lock.json</c>（官方锁有效时拒绝写），把旧文件备份到我们自己的 data 目录，
/// 再用 <see cref="JsonNode"/> 就地改、紧凑无 BOM 原子写，最后删掉自己的锁。
/// 解析失败时只记 Warn，绝不把半截内容写回去。
/// </para>
/// </summary>
public sealed class PlaysetConfigRepository : IPlaySetRepository
{
    private const string BackupFolderName = "playsets-backup";

    /// <summary>备份保留份数上限。</summary>
    public const int MaxBackups = 20;

    private static readonly AppResult<bool> OkBool = AppResult<bool>.Ok(true);

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly IClock _clock;
    private readonly PdxModsDirectory _directory;
    private readonly string _backupRoot;

    /// <summary>创建仓储。</summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <param name="logger">日志。</param>
    /// <param name="clock">时间抽象（自检注入固定时间）。</param>
    /// <param name="backupRoot">备份根目录（我们自己的 data 目录，绝不写进游戏目录）。</param>
    public PlaysetConfigRepository(
        IFileSystem fileSystem,
        IAppLogger logger,
        IClock clock,
        string? backupRoot = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);

        _fileSystem = fileSystem;
        _logger = logger;
        _clock = clock;
        _directory = new PdxModsDirectory(fileSystem, logger);
        _backupRoot = backupRoot ?? Path.Combine(PdxDataRoot.Directory, BackupFolderName);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySet>> GetPlaySetsAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var (document, ok) = LoadDocument(game);
        if (!ok)
        {
            return Task.FromResult<IReadOnlyList<PlaySet>>(Array.Empty<PlaySet>());
        }

        var activeId = document.ActivePlaySetId;
        var result = new List<PlaySet>(document.PlaySets.Count);
        foreach (var playSet in document.PlaySets)
        {
            var id = PlaysetConfigDocument.GetPlaySetId(playSet);
            var name = PlaysetConfigDocument.GetName(playSet);
            result.Add(new PlaySet(
                id,
                name.Length > 0 ? name : id.ToString("D"),
                activeId is { } active && active == id));
        }

        return Task.FromResult<IReadOnlyList<PlaySet>>(result);
    }

    /// <inheritdoc />
    public async Task<PlaySet?> GetActivePlaySetAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        var all = await GetPlaySetsAsync(game, cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(static playSet => playSet.IsActive);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySetMod>> GetPlaySetModsAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var (document, ok) = LoadDocument(game);
        if (!ok)
        {
            return Task.FromResult<IReadOnlyList<PlaySetMod>>(Array.Empty<PlaySetMod>());
        }

        var playSet = document.FindPlaySet(playSetId);
        if (playSet is null)
        {
            return Task.FromResult<IReadOnlyList<PlaySetMod>>(Array.Empty<PlaySetMod>());
        }

        var cache = PdxModsCache.Load(_fileSystem, _logger, PlaysetConfigPaths.GetModsCachePath(game));
        var versions = _directory.ScanVersions(game);

        var result = new List<PlaySetMod>();
        var position = 0;
        foreach (var node in PlaysetConfigDocument.EnsureMods(playSet))
        {
            if (node is not JsonObject mod)
            {
                continue;
            }

            var sourceId = PlaysetConfigDocument.GetModSourceId(mod);
            if (sourceId.Length == 0)
            {
                continue;
            }

            // 只处理 pdx_mods 来源。CS2 目前只有这一种；万一以后出现 local 来源，
            // 这里按缺失态保留一条，不静默丢掉用户的条目。
            var source = PlaysetConfigDocument.GetModSource(mod);
            if (!string.Equals(source, PlaysetConfigDocument.ModSourcePdxMods, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("playset_config 里出现非常规 mod 来源 " + source + "（sourceId=" + sourceId + "），按缺失条目保留。");
                result.Add(new PlaySetMod(
                    ModId: PdxModsCache.GetModId(game.GameId, source + ":" + sourceId),
                    Name: sourceId,
                    DirPath: string.Empty,
                    ArchivePath: null,
                    GameRegistryId: source + ":" + sourceId,
                    ThumbnailPath: null,
                    RequiredVersion: string.Empty,
                    Status: "missing",
                    Enabled: PlaysetConfigDocument.GetModEnabled(mod),
                    Position: position++));
                continue;
            }

            result.Add(_directory.Compose(
                game,
                sourceId,
                cache,
                versions.TryGetValue(sourceId, out var known) ? known : null,
                PlaysetConfigDocument.GetModSourceVersion(mod),
                PlaysetConfigDocument.GetModEnabled(mod),
                position));
            position++;
        }

        return Task.FromResult<IReadOnlyList<PlaySetMod>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ModLibraryEntry>> GetModLibraryAsync(
        GameInstallation game,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var cache = PdxModsCache.Load(_fileSystem, _logger, PlaysetConfigPaths.GetModsCachePath(game));
        var (document, ok) = LoadDocument(game);

        // sourceId → 引用它的播放集 Id（CS2 的播放集里只有 sourceId，没有 mod 表，所以每次现算）。
        var references = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        if (ok)
        {
            foreach (var playSet in document.PlaySets)
            {
                var playSetId = PlaysetConfigDocument.GetPlaySetId(playSet);
                foreach (var node in PlaysetConfigDocument.EnsureMods(playSet))
                {
                    if (node is not JsonObject mod)
                    {
                        continue;
                    }

                    var sourceId = PlaysetConfigDocument.GetModSourceId(mod);
                    if (sourceId.Length == 0)
                    {
                        continue;
                    }

                    if (!references.TryGetValue(sourceId, out var list))
                    {
                        list = [];
                        references[sourceId] = list;
                    }

                    if (!list.Contains(playSetId))
                    {
                        list.Add(playSetId);
                    }
                }
            }
        }

        var result = new List<ModLibraryEntry>();
        foreach (var (mod, item) in _directory.ComposeLibrary(game, cache))
        {
            var sourceId = mod.GameRegistryId.StartsWith("pdx:", StringComparison.Ordinal)
                ? mod.GameRegistryId[4..]
                : mod.GameRegistryId;
            // PDX Mods 的 thumbnailPath 是 CDN 上的 https 地址，不是本地文件：
            // ThumbnailPath 必须留 null（UI 只按本地路径渲染），地址交给 ThumbnailUrl 走联网补全 + 缓存。
            result.Add(new ModLibraryEntry(
                mod.ModId,
                mod.Name,
                mod.GameRegistryId,
                mod.DirPath.Length > 0 ? mod.DirPath : null,
                mod.ArchivePath,
                ThumbnailPath: null,
                mod.RequiredVersion,
                mod.Status,
                Source: PlaysetConfigDocument.ModSourcePdxMods,
                item?.SizeBytes,
                item?.InstalledOn,
                references.TryGetValue(sourceId, out var list) ? list : [],
                IsUnregistered: false,
                SteamId: null,
                ThumbnailUrl: mod.ThumbnailPath));
        }

        return Task.FromResult<IReadOnlyList<ModLibraryEntry>>(result);
    }

    /// <inheritdoc />
    /// <remarks>CS2 的播放集里没有 DLC 条目，也读不到 DLC 清单，因此恒为空。</remarks>
    public Task<IReadOnlyList<PlaySetDlc>> GetPlaySetDlcsAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<PlaySetDlc>>(Array.Empty<PlaySetDlc>());
    }

    /// <inheritdoc />
    public Task<AppResult<bool>> SavePlaySetModsAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetMod> mods,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(mods);

        return WriteAsync(
            "保存播放集 mod",
            game,
            document =>
            {
                var playSet = document.FindPlaySet(playSetId);
                if (playSet is null)
                {
                    return AppResult<bool>.Fail(ErrorKind.NotFound, NotFoundCode(playSetId));
                }

                var cache = PdxModsCache.Load(_fileSystem, _logger, PlaysetConfigPaths.GetModsCachePath(game));
                var versions = _directory.ScanVersions(game);

                var array = new JsonArray();
                foreach (var mod in mods.OrderBy(static m => m.Position))
                {
                    var sourceId = ResolveSourceId(game, mod, cache);
                    if (sourceId is null)
                    {
                        // 认不出 sourceId 的条目（既不是 pdx:<id> 也派不出哈希）不能写进 CS2 的文件：
                        // 写进去等于让游戏去加载一个不存在的 mod，比丢掉它更糟。
                        _logger.Warn("跳过无法还原 sourceId 的 mod 条目: " + mod.Name + "（" + mod.GameRegistryId + "）");
                        continue;
                    }

                    var entry = new JsonObject
                    {
                        [KeySource] = PlaysetConfigDocument.ModSourcePdxMods,
                        [KeySourceId] = sourceId,
                    };

                    // 与官方一致：只有「这个 mod 存在多版本、且明确钉了某一个版本」时才写 sourceVersion。
                    // 真机 53 项里 49 项没有这个键，4 项有 —— 就是 keepLatest 与否的区别。
                    var pinnedVersion = ResolvePinnedVersion(game, mod, sourceId, cache, versions);
                    if (pinnedVersion is not null)
                    {
                        entry[KeySourceVersion] = pinnedVersion;
                    }

                    entry[KeyIsEnabled] = mod.Enabled;
                    array.Add(entry);
                }

                playSet[KeyMods] = array;
                PlaysetConfigDocument.Touch(playSet, _clock.UtcNow);
                return OkBool;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>CS2 没有播放集级 DLC 开关：直接返回成功，不往文件里写任何 DLC 结构。</remarks>
    public Task<AppResult<bool>> SavePlaySetDlcsAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetDlc> dlcs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OkBool);
    }

    /// <inheritdoc />
    public Task<AppResult<Guid>> CreatePlaySetAsync(
        GameInstallation game,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(AppResult<Guid>.Fail(ErrorKind.Invalid, "playSet-name-empty"));
        }

        var playSetName = name.Trim();
        var newId = Guid.NewGuid();
        return WriteAsync<Guid>(
            "创建播放集",
            game,
            document =>
            {
                if (document.FindPlaySet(playSetName) is not null)
                {
                    return AppResult<Guid>.Fail(ErrorKind.Conflict, "playSet-name-exists:" + playSetName);
                }

                document.AddPlaySet(newId, playSetName, _clock.UtcNow);
                return AppResult<Guid>.Ok(newId);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<AppResult<bool>> SetActivePlaySetAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        return WriteAsync(
            "设置激活播放集",
            game,
            document =>
            {
                if (document.FindPlaySet(playSetId) is null)
                {
                    return AppResult<bool>.Fail(ErrorKind.NotFound, NotFoundCode(playSetId));
                }

                document.SetActivePlaySetId(playSetId);
                return OkBool;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<AppResult<bool>> RenamePlaySetAsync(
        GameInstallation game,
        Guid playSetId,
        string newName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return Task.FromResult(AppResult<bool>.Fail(ErrorKind.Invalid, "playSet-name-empty"));
        }

        var name = newName.Trim();
        return WriteAsync(
            "重命名播放集",
            game,
            document =>
            {
                var playSet = document.FindPlaySet(playSetId);
                if (playSet is null)
                {
                    return AppResult<bool>.Fail(ErrorKind.NotFound, NotFoundCode(playSetId));
                }

                if (document.FindPlaySet(name) is not null)
                {
                    return AppResult<bool>.Fail(ErrorKind.Conflict, "playSet-name-exists:" + name);
                }

                PlaysetConfigDocument.SetName(playSet, name);
                PlaysetConfigDocument.Touch(playSet, _clock.UtcNow);
                return OkBool;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<AppResult<bool>> DeletePlaySetAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        return WriteAsync(
            "删除播放集",
            game,
            document =>
            {
                var playSet = document.FindPlaySet(playSetId);
                if (playSet is null)
                {
                    return AppResult<bool>.Fail(ErrorKind.NotFound, NotFoundCode(playSetId));
                }

                // 激活中的播放集不允许删除：游戏启动时必须能找到一个激活集。
                if (document.ActivePlaySetId is { } active && active == playSetId)
                {
                    return AppResult<bool>.Fail(ErrorKind.Conflict, "playSet-is-active:" + playSetId.ToString("D"));
                }

                if (!document.RemovePlaySet(playSet))
                {
                    return AppResult<bool>.Fail(ErrorKind.NotFound, NotFoundCode(playSetId));
                }

                return OkBool;
            },
            cancellationToken);
    }

    /// <summary>
    /// 把 UI 的 <see cref="PlaySetMod.ModId"/> 还原成 PDX Mods 的数字 sourceId。
    /// 优先用 <see cref="PlaySetMod.GameRegistryId"/>（<c>pdx:&lt;id&gt;</c>），
    /// 兜底用缓存与磁盘目录反查哈希值。
    /// </summary>
    private string? ResolveSourceId(GameInstallation game, PlaySetMod mod, PdxModsCache cache)
    {
        if (mod.GameRegistryId.StartsWith("pdx:", StringComparison.Ordinal))
        {
            var id = mod.GameRegistryId[4..];
            if (id.Length > 0)
            {
                return id;
            }
        }

        // 兜底：按确定性哈希反查（UI 重新加载过、GameRegistryId 丢了时也能救回来）。
        foreach (var item in cache.Items)
        {
            if (PdxModsCache.GetModId(game.GameId, item.Id) == mod.ModId)
            {
                return item.Id;
            }
        }

        foreach (var sourceId in _directory.ScanVersions(game).Keys)
        {
            if (PdxModsCache.GetModId(game.GameId, sourceId) == mod.ModId)
            {
                return sourceId;
            }
        }

        return null;
    }

    /// <summary>
    /// 决定写不写 <c>sourceVersion</c>：只有当这个 mod 在磁盘上确实存在多个版本、
    /// 且 <see cref="PlaySetMod.DirPath"/> 指向其中一个具体版本时才钉住它。
    /// 单版本 mod 不写（与官方 keepLatest 行为一致，也避免把版本号写成陈旧值）。
    /// </summary>
    private string? ResolvePinnedVersion(
        GameInstallation game,
        PlaySetMod mod,
        string sourceId,
        PdxModsCache cache,
        IReadOnlyDictionary<string, IReadOnlyList<string>> versions)
    {
        if (!versions.TryGetValue(sourceId, out var known) || known.Count <= 1)
        {
            return null;
        }

        var fromPath = PdxModsCache.ParseFolderName(
            string.IsNullOrWhiteSpace(mod.DirPath) ? null : Path.GetFileName(mod.DirPath));
        if (fromPath is { } parsed && string.Equals(parsed.Id, sourceId, StringComparison.Ordinal))
        {
            return parsed.Version;
        }

        // 路径拿不到版本时用缓存里的最新版本（与读取侧的挑选规则保持一致）。
        var latest = cache.Find(sourceId)?.LatestVersion;
        return string.IsNullOrWhiteSpace(latest) ? null : latest;
    }

    private (PlaysetConfigDocument Document, bool Ok) LoadDocument(GameInstallation game)
    {
        var path = PlaysetConfigPaths.GetConfigPath(game);
        if (!_fileSystem.FileExists(path))
        {
            return (PlaysetConfigDocument.CreateNew(), true);
        }

        try
        {
            return (PlaysetConfigDocument.Parse(_fileSystem.ReadAllText(path)), true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.Warn("playset_config.json 解析失败，按空播放集处理（不会再写坏它）: " + ex.Message);
            return (PlaysetConfigDocument.CreateNew(), false);
        }
    }

    private void SaveDocument(GameInstallation game, PlaysetConfigDocument document)
    {
        var path = PlaysetConfigPaths.GetConfigPath(game);
        var directory = _fileSystem.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _fileSystem.EnsureDirectory(directory);
        }

        if (_fileSystem.FileExists(path))
        {
            Backup(path, game.GameId);
        }

        _fileSystem.WriteAllTextAtomic(path, document.Serialize());
    }

    private void Backup(string path, string gameId)
    {
        try
        {
            var directory = Path.Combine(_backupRoot, gameId);
            _fileSystem.EnsureDirectory(directory);
            var stamp = _clock.UtcNow.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            _fileSystem.Copy(
                path,
                Path.Combine(directory, PlaysetConfigPaths.FileName + "." + stamp + ".json"),
                overwrite: true);
            PruneBackups(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 备份失败不应该挡住保存：只记 Warn。
            _logger.Warn("playset_config.json 备份失败（继续保存）: " + ex.Message);
        }
    }

    private void PruneBackups(string directory)
    {
        try
        {
            var files = _fileSystem
                .EnumerateFiles(directory, PlaysetConfigPaths.FileName + ".*.json", recursive: false)
                .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
                .Skip(MaxBackups)
                .ToArray();

            foreach (var file in files)
            {
                _fileSystem.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("清理 playset_config.json 旧备份失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 写操作的统一骨架：抢锁 → 读文档 → 改 → 备份 → 原子写 → 放锁。
    /// 异常一律收敛成 <see cref="ErrorKind.Io"/>，绝不抛给 UI。
    /// </summary>
    private async Task<AppResult<T>> WriteAsync<T>(
        string action,
        GameInstallation game,
        Func<PlaysetConfigDocument, AppResult<T>> write,
        CancellationToken cancellationToken)
    {
        var lockPath = PlaysetConfigPaths.GetLockPath(game);
        return await Task.Run(
            () =>
            {
                var now = _clock.UtcNow;
                AppResult<T> result;
                var acquired = PlaysetConfigLock.Acquire(_fileSystem, _logger, lockPath, now);
                if (!acquired.IsSuccess)
                {
                    return AppResult<T>.Fail(acquired.Kind, acquired.ErrorMessage ?? "playset-config-locked");
                }

                try
                {
                    var (document, ok) = LoadDocument(game);
                    if (!ok)
                    {
                        // 文件存在但读不懂：绝不覆盖（会把用户全部播放集抹掉）。
                        return AppResult<T>.Fail(ErrorKind.Io, "playset-config-unreadable");
                    }

                    result = write(document);
                    if (!result.IsSuccess)
                    {
                        return result;
                    }

                    SaveDocument(game, document);
                    _logger.Info("已写入 playset_config.json（" + action + "，" + game.GameId + "）");
                    return result;
                }
                finally
                {
                    PlaysetConfigLock.Release(_fileSystem, _logger, lockPath, _clock.UtcNow);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static string NotFoundCode(Guid playSetId) => "playSet-not-found:" + playSetId.ToString("D");

    private const string KeyMods = "mods";
    private const string KeySource = "source";
    private const string KeySourceId = "sourceId";
    private const string KeySourceVersion = "sourceVersion";
    private const string KeyIsEnabled = "isEnabled";
}
