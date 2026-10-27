namespace PdxLauncher.Core.Playsets.Json;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;

/// <summary>
/// EU5（<see cref="LoadFileFormat.PlaysetsJsonV1"/>）的播放集仓储：直接维护文档目录下的 <c>playsets.json</c>。
/// <para>
/// 与 sqlite 版的映射规则（差异都是 EU5 自己造成的）：
/// </para>
/// <list type="bullet">
/// <item>播放集没有 id：<see cref="PlaySet.Id"/> 由 (gameId, name) 哈希得到，改名等于换 id（官方也是 name 即身份）。</item>
/// <item>mod 只有绝对目录路径：<see cref="PlaySetMod.ModId"/> 与 <see cref="PlaySetMod.DirPath"/> 由扫描结果补全，
/// 写回时优先用磁盘上真实存在的目录（Steam 库搬家后旧路径会失效）。</item>
/// <item>DLC 挂在播放集上（<c>DLC[] { paradoxAppId, isEnabled }</c>），id 是 pops_id 字符串。</item>
/// </list>
/// <para>
/// 写入策略：写前把旧文件备份到我们自己的 data 目录，然后原子替换；解析失败时只记 Warn，绝不抛给 UI。
/// </para>
/// </summary>
public sealed class PlaysetsJsonRepository : IPlaySetRepository
{
    private const string KeyName = "name";
    private const string KeyIsActive = "isActive";
    private const string KeyOrderedListMods = "orderedListMods";
    private const string KeyDlc = "DLC";
    private const string KeyPath = "path";
    private const string KeyIsEnabled = "isEnabled";
    private const string KeyParadoxAppId = "paradoxAppId";

    private const string BackupFolderName = "playsets-backup";

    private static readonly char[] PathSeparators = ['/', '\\'];

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly IModCatalog _mods;
    private readonly IDlcCatalog _dlcs;
    private readonly string _backupRoot;

    /// <summary>
    /// 创建仓储。
    /// </summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <param name="logger">日志。</param>
    /// <param name="mods">mod 目录扫描：用来把 json 里的路径补成完整条目。</param>
    /// <param name="dlcs">DLC 目录扫描：用来把 pops_id 与官方写下的显示名条目对上。</param>
    /// <param name="backupRoot">备份根目录（我们自己的 data 目录，绝不写进游戏目录）。</param>
    public PlaysetsJsonRepository(
        IFileSystem fileSystem,
        IAppLogger logger,
        IModCatalog mods,
        IDlcCatalog dlcs,
        string? backupRoot = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(dlcs);

        _fileSystem = fileSystem;
        _logger = logger;
        _mods = mods;
        _dlcs = dlcs;
        _backupRoot = backupRoot ?? Path.Combine(PdxDataRoot.Directory, BackupFolderName);
    }

    /// <summary>游戏文档目录下的 playsets.json 绝对路径。</summary>
    public static string GetFilePath(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return Path.Combine(game.GameDataDir, PlaysetsJsonDocument.FileName);
    }

    /// <summary>
    /// 播放集 Id：由 (gameId, name) 派生的确定性 Guid。
    /// UI 的选中态、播放集页路由都靠它稳定；改名会让 Id 变化（这是设计，不是 bug）。
    /// </summary>
    public static Guid GetPlaySetId(string gameId, string name)
    {
        var key = "pdxnext:playset:" + (gameId ?? string.Empty) + ":" + (name ?? string.Empty);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>mod 目录路径规范化：正斜杠 + 末尾带 “/”（与官方写出来的形状一致）。</summary>
    public static string NormalizeModPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/');
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlaySet>> GetPlaySetsAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var document = LoadDocument(game);
        return document.PlaySets
            .Select(ps => new PlaySet(
                GetPlaySetId(game.GameId, PlaysetsJsonDocument.GetString(ps, KeyName)),
                PlaysetsJsonDocument.GetString(ps, KeyName),
                PlaysetsJsonDocument.GetBool(ps, KeyIsActive, fallback: false)))
            .ToArray();
    }

    /// <inheritdoc />
    public Task<PlaySet?> GetActivePlaySetAsync(GameInstallation game, CancellationToken cancellationToken) =>
        GetPlaySetsAsync(game, cancellationToken)
            .ContinueWith(
                task => task.Result.FirstOrDefault(static ps => ps.IsActive),
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlaySetMod>> GetPlaySetModsAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var document = LoadDocument(game);
        var playSet = FindPlaySet(document, game, playSetId);
        if (playSet is null)
        {
            return Array.Empty<PlaySetMod>();
        }

        var catalog = await _mods.DiscoverAllAsync(game, cancellationToken).ConfigureAwait(false);
        var byPath = BuildPathIndex(catalog);
        var byDirectory = BuildDirectoryIndex(catalog);

        var result = new List<PlaySetMod>();
        var position = 0;
        foreach (var node in PlaysetsJsonDocument.EnsureArray(playSet, KeyOrderedListMods))
        {
            if (node is not JsonObject entry)
            {
                continue;
            }

            var path = PlaysetsJsonDocument.GetString(entry, KeyPath);
            var key = NormalizeLookupKey(path);
            var enabled = PlaysetsJsonDocument.GetBool(entry, KeyIsEnabled, fallback: true);

            var known = FindKnownMod(byPath, byDirectory, key);
            if (known is not null)
            {
                // DirPath 用磁盘上的真实路径：json 里的陈旧路径（库搬家前）不该带进播放集，
                // 否则启用/启动参数会把游戏指向不存在的目录。
                result.Add(known with { Enabled = enabled, Position = position });
            }
            else
            {
                // 磁盘上已经不存在的 mod（Steam 取消订阅/库搬家）：保留一条占位，避免把用户配置悄悄丢掉。
                var directoryName = GetDirectoryNameFromModPath(path);
                result.Add(new PlaySetMod(
                    ModId: CreatePlaceholderModId(game.GameId, key),
                    Name: directoryName,
                    DirPath: path,
                    ArchivePath: null,
                    GameRegistryId: string.Empty,
                    ThumbnailPath: null,
                    RequiredVersion: string.Empty,
                    Status: "missing",
                    Enabled: enabled,
                    Position: position,
                    SteamId: directoryName));
            }

            position++;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModLibraryEntry>> GetModLibraryAsync(
        GameInstallation game,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var catalog = await _mods.DiscoverAllAsync(game, cancellationToken).ConfigureAwait(false);
        var document = LoadDocument(game);

        // 路径 → 引用它的播放集 Id（json 里只有路径，没有 mods 表，所以每次现算）。
        // 同时按目录名（工坊 id）记一份，兜住 json 里库搬家前的陈旧路径。
        var references = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
        void AddReference(string key, Guid playSetId)
        {
            if (key.Length == 0)
            {
                return;
            }

            if (!references.TryGetValue(key, out var list))
            {
                list = [];
                references[key] = list;
            }

            if (!list.Contains(playSetId))
            {
                list.Add(playSetId);
            }
        }

        foreach (var playSet in document.PlaySets)
        {
            var playSetId = GetPlaySetId(game.GameId, PlaysetsJsonDocument.GetString(playSet, KeyName));
            foreach (var node in PlaysetsJsonDocument.EnsureArray(playSet, KeyOrderedListMods))
            {
                if (node is not JsonObject entry)
                {
                    continue;
                }

                var key = NormalizeLookupKey(PlaysetsJsonDocument.GetString(entry, KeyPath));
                AddReference(key, playSetId);
                AddReference(GetDirectoryNameFromModPath(key), playSetId);
            }
        }

        return catalog
            .Select(mod =>
            {
                var key = NormalizeLookupKey(mod.DirPath ?? mod.ArchivePath);
                var playSetIds = references.TryGetValue(key, out var list)
                    ? list
                    : references.TryGetValue(GetDirectoryNameFromModPath(key), out var byDirectory)
                        ? byDirectory
                        : [];
                return new ModLibraryEntry(
                    mod.ModId,
                    mod.Name,
                    mod.GameRegistryId,
                    mod.DirPath,
                    mod.ArchivePath,
                    mod.ThumbnailPath,
                    mod.RequiredVersion,
                    mod.Status,
                    Source: "local",
                    SizeBytes: null,
                    SubscribedOn: null,
                    playSetIds,
                    IsUnregistered: false,
                    mod.SteamId,
                    ThumbnailUrl: null);
            })
            .ToArray();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySetDlc>> GetPlaySetDlcsAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var document = LoadDocument(game);
        var playSet = FindPlaySet(document, game, playSetId);
        if (playSet is null)
        {
            return Task.FromResult<IReadOnlyList<PlaySetDlc>>(Array.Empty<PlaySetDlc>());
        }

        var result = new List<PlaySetDlc>();
        foreach (var node in PlaysetsJsonDocument.EnsureArray(playSet, KeyDlc))
        {
            if (node is not JsonObject entry)
            {
                continue;
            }

            var id = PlaysetsJsonDocument.GetString(entry, KeyParadoxAppId);
            if (id.Length == 0)
            {
                continue;
            }

            result.Add(new PlaySetDlc(id, PlaysetsJsonDocument.GetBool(entry, KeyIsEnabled, fallback: true)));
        }

        return Task.FromResult<IReadOnlyList<PlaySetDlc>>(result);
    }

    /// <inheritdoc />
    public Task SavePlaySetModsAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetMod> mods,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(mods);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () =>
            {
                var document = LoadDocument(game);
                var playSet = RequirePlaySet(document, game, playSetId);

                var array = new JsonArray();
                foreach (var mod in mods.OrderBy(static m => m.Position))
                {
                    var path = ResolveModPath(game, mod);
                    if (path.Length == 0)
                    {
                        continue;
                    }

                    array.Add(new JsonObject
                    {
                        [KeyPath] = path,
                        [KeyIsEnabled] = mod.Enabled,
                    });
                }

                playSet[KeyOrderedListMods] = array;
                SaveDocument(game, document);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task SavePlaySetDlcsAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetDlc> dlcs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(dlcs);

        var catalog = await _dlcs.DiscoverAsync(game, cancellationToken).ConfigureAwait(false);

        var document = LoadDocument(game);
        var playSet = RequirePlaySet(document, game, playSetId);

        // pops_id → 启用状态
        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var dlc in dlcs)
        {
            if (!string.IsNullOrWhiteSpace(dlc.DlcId))
            {
                states[dlc.DlcId] = dlc.Enabled;
            }
        }

        // 官方启动器历史上还会写「显示名」条目（如 "Sacred Sites"），它们与 pops_id 条目是同一个 DLC。
        // 这里按 DLC 目录里的 localizable_name 建立别名，写回时同步成同一状态，避免 UI 关掉 DLC 后游戏仍认为它是开的。
        var aliasToPopsId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dlc in catalog)
        {
            if (string.IsNullOrWhiteSpace(dlc.PopsId) || string.IsNullOrWhiteSpace(dlc.DisplayName))
            {
                continue;
            }

            if (!aliasToPopsId.ContainsKey(dlc.DisplayName))
            {
                aliasToPopsId[dlc.DisplayName] = dlc.PopsId;
            }
        }

        var known = new HashSet<string>(states.Keys, StringComparer.OrdinalIgnoreCase);
        var existing = PlaysetsJsonDocument.EnsureArray(playSet, KeyDlc);

        // 保留「我们这次没有覆盖到」的条目（例如 "Shared DLC Data" 这类游戏自己的条目），状态原样带过去。
        var extra = new List<(string Id, bool Enabled)>();
        foreach (var node in existing)
        {
            if (node is not JsonObject entry)
            {
                continue;
            }

            var id = PlaysetsJsonDocument.GetString(entry, KeyParadoxAppId);
            if (id.Length == 0 || known.Contains(id))
            {
                continue;
            }

            var enabled = PlaysetsJsonDocument.GetBool(entry, KeyIsEnabled, fallback: true);
            if (aliasToPopsId.TryGetValue(id, out var popsId) && states.TryGetValue(popsId, out var aliasState))
            {
                enabled = aliasState;
            }

            extra.Add((id, enabled));
        }

        var array = new JsonArray();
        foreach (var (id, enabled) in states.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            array.Add(new JsonObject
            {
                [KeyParadoxAppId] = id,
                [KeyIsEnabled] = enabled,
            });
        }

        foreach (var (id, enabled) in extra)
        {
            array.Add(new JsonObject
            {
                [KeyParadoxAppId] = id,
                [KeyIsEnabled] = enabled,
            });
        }

        playSet[KeyDlc] = array;
        SaveDocument(game, document);
    }

    /// <inheritdoc />
    public Task<Guid> CreatePlaySetAsync(GameInstallation game, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () =>
            {
                var document = LoadDocument(game);
                if (document.FindPlaySet(name) is not null)
                {
                    throw new InvalidOperationException("播放集已存在：" + name);
                }

                document.AddPlaySet(name, isActive: false);
                SaveDocument(game, document);
                return GetPlaySetId(game.GameId, name);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetActivePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () =>
            {
                var document = LoadDocument(game);
                var target = RequirePlaySet(document, game, playSetId);
                foreach (var playSet in document.PlaySets)
                {
                    playSet[KeyIsActive] = ReferenceEquals(playSet, target);
                }

                SaveDocument(game, document);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> RenamePlaySetAsync(
        GameInstallation game,
        Guid playSetId,
        string newName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () =>
            {
                var document = LoadDocument(game);
                var playSet = FindPlaySet(document, game, playSetId);
                if (playSet is null || document.FindPlaySet(newName) is not null)
                {
                    return false;
                }

                playSet[KeyName] = newName;
                SaveDocument(game, document);
                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeletePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () =>
            {
                var document = LoadDocument(game);
                var playSet = FindPlaySet(document, game, playSetId);
                if (playSet is null)
                {
                    return false;
                }

                // 活动播放集不允许删除：游戏启动时必须有一个活动播放集。
                if (PlaysetsJsonDocument.GetBool(playSet, KeyIsActive, fallback: false))
                {
                    return false;
                }

                if (!document.RemovePlaySet(playSet))
                {
                    return false;
                }

                SaveDocument(game, document);
                return true;
            },
            cancellationToken);
    }

    /// <summary>路径 → 已扫描条目，键是规范化后的路径（大小写不敏感）。</summary>
    private static Dictionary<string, PlaySetMod> BuildPathIndex(IReadOnlyList<PlaySetMod> catalog)
    {
        var index = new Dictionary<string, PlaySetMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in catalog)
        {
            var key = NormalizeLookupKey(mod.DirPath ?? mod.ArchivePath);
            if (key.Length > 0 && !index.ContainsKey(key))
            {
                index[key] = mod;
            }
        }

        return index;
    }

    /// <summary>
    /// 目录名（工坊 id）→ 已扫描条目。用来兜住「Steam 库搬家后 playsets.json 里还留着旧绝对路径」
    /// 的情况：真机 EU5 的 json 里写的是 <c>E:/SteamLibrary/...</c>，而 mod 实际在 F: 库。
    /// </summary>
    private static Dictionary<string, PlaySetMod> BuildDirectoryIndex(IReadOnlyList<PlaySetMod> catalog)
    {
        var index = new Dictionary<string, PlaySetMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in catalog)
        {
            var directory = GetDirectoryNameFromModPath(mod.DirPath ?? mod.ArchivePath ?? string.Empty);
            if (directory.Length > 0 && !index.ContainsKey(directory))
            {
                index[directory] = mod;
            }
        }

        return index;
    }

    /// <summary>
    /// 按路径查已扫描的 mod；路径对不上时退一步按目录名（工坊 id）再查一次。
    /// 库搬家后 json 里的绝对路径会失效，但工坊 id 目录名不变。
    /// </summary>
    private static PlaySetMod? FindKnownMod(
        Dictionary<string, PlaySetMod> byPath,
        Dictionary<string, PlaySetMod> byDirectory,
        string pathKey)
    {
        if (pathKey.Length == 0)
        {
            return null;
        }

        if (byPath.TryGetValue(pathKey, out var known))
        {
            return known;
        }

        var directory = GetDirectoryNameFromModPath(pathKey);
        return directory.Length > 0 && byDirectory.TryGetValue(directory, out var renamed) ? renamed : null;
    }

    /// <summary>查表用的路径键：去掉末尾分隔符、统一正斜杠。</summary>
    private static string NormalizeLookupKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Replace('\\', '/').TrimEnd(PathSeparators);
    }

    private static string GetDirectoryNameFromModPath(string path)
    {
        var trimmed = path.Replace('\\', '/').TrimEnd(PathSeparators);
        var index = trimmed.LastIndexOf('/');
        return index >= 0 && index < trimmed.Length - 1 ? trimmed[(index + 1)..] : trimmed;
    }

    private static Guid CreatePlaceholderModId(string gameId, string pathKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("pdxnext:missingmod:" + gameId + ":" + pathKey));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// 写回用的 mod 路径：优先用磁盘上真实存在的目录（修掉 Steam 库搬家后的陈旧绝对路径），
    /// 不存在时退回 mod 记录里的路径。EU5 只认绝对路径，所以两条路径都必须能拼出绝对路径。
    /// </summary>
    private string ResolveModPath(GameInstallation game, PlaySetMod mod)
    {
        foreach (var candidate in new[] { mod.DirPath, mod.ArchivePath })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var full = Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(game.GameDataDir, candidate);

            if (_fileSystem.DirectoryExists(full) || _fileSystem.FileExists(full))
            {
                return NormalizeModPath(full);
            }
        }

        // 磁盘上找不到：仍然写回去（游戏会把它当缺失 mod 提示），至少不丢用户配置。
        var fallback = string.IsNullOrWhiteSpace(mod.DirPath) ? mod.ArchivePath : mod.DirPath;
        return NormalizeModPath(
            fallback is not null && !Path.IsPathRooted(fallback)
                ? Path.Combine(game.GameDataDir, fallback)
                : fallback);
    }

    private PlaysetsJsonDocument LoadDocument(GameInstallation game)
    {
        var path = GetFilePath(game);
        if (!_fileSystem.FileExists(path))
        {
            return PlaysetsJsonDocument.CreateNew();
        }

        try
        {
            return PlaysetsJsonDocument.Parse(_fileSystem.ReadAllText(path));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.Warn("playsets.json 解析失败，按空播放集处理（不会再写坏它）: " + ex.Message);
            return PlaysetsJsonDocument.CreateNew();
        }
    }

    private void SaveDocument(GameInstallation game, PlaysetsJsonDocument document)
    {
        var path = GetFilePath(game);
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
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            _fileSystem.Copy(path, Path.Combine(directory, PlaysetsJsonDocument.FileName + "." + stamp + ".bak"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 备份失败不应该挡住保存：只记 Warn。
            _logger.Warn("playsets.json 备份失败（继续保存）: " + ex.Message);
        }
    }

    private static JsonObject? FindPlaySet(PlaysetsJsonDocument document, GameInstallation game, Guid playSetId)
    {
        foreach (var playSet in document.PlaySets)
        {
            var name = PlaysetsJsonDocument.GetString(playSet, KeyName);
            if (GetPlaySetId(game.GameId, name) == playSetId)
            {
                return playSet;
            }
        }

        return null;
    }

    private static JsonObject RequirePlaySet(PlaysetsJsonDocument document, GameInstallation game, Guid playSetId) =>
        FindPlaySet(document, game, playSetId)
        ?? throw new InvalidOperationException("播放集不存在：" + playSetId);
}
