namespace PdxLauncher.Core.Playsets;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// <see cref="IPlaySetRepository"/> 的 SQLite 实现（winsqlite3.dll）。
/// <para>读取一律以 SQLITE_OPEN_READONLY 打开，绝不因为读而锁库；写入一律先备份再 BEGIN IMMEDIATE 事务提交，失败回滚。</para>
/// <para>所有 SQL 参数都走 sqlite3_bind_text 参数化绑定，绝不拼接字符串。</para>
/// </summary>
public sealed class PlaySetRepository : IPlaySetRepository
{
    /// <summary>写前备份文件的后缀（备份到 &lt;launcher-v2.sqlite&gt; + 此后缀，覆盖上一份）。</summary>
    public const string BackupSuffix = ".pdxnext.bak";

    /// <summary>SQLite 数据库文件名。</summary>
    public const string DatabaseFileName = "launcher-v2.sqlite";

    // 同一路径的写操作必须串行化；按规范化路径共享信号量，避免多个实例并发写同一个库。
    private static readonly Dictionary<string, SemaphoreSlim> WriteGates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object GateLock = new();

    /// <summary>写成功的公共结果，避免每次分配。</summary>
    private static readonly AppResult<bool> OkBool = AppResult<bool>.Ok(true);

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly IClock _clock;
    private readonly ModThumbnailResolver _thumbnails;

    /// <summary>创建仓库。</summary>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="logger">日志。</param>
    /// <param name="clock">时钟。</param>
    /// <param name="thumbnails">缩略图解析器；为 null 时自行创建（Steam 库列表延迟读取）。</param>
    public PlaySetRepository(
        IFileSystem fileSystem,
        IAppLogger logger,
        IClock clock,
        ModThumbnailResolver? thumbnails = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);
        _fileSystem = fileSystem;
        _logger = logger;
        _clock = clock;
        _thumbnails = thumbnails ?? new ModThumbnailResolver(fileSystem, logger);
    }

    /// <summary>计算数据库文件的绝对路径：&lt;gameDataDir&gt;\launcher-v2.sqlite。</summary>
    public static string GetDatabasePath(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return Path.Combine(game.GameDataDir, DatabaseFileName);
    }

    /// <summary>计算写前备份文件的路径。</summary>
    public static string GetBackupPath(string databasePath) => databasePath + BackupSuffix;

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySet>> GetPlaySetsAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        return ReadAsync(
            game,
            "读取播放集列表",
            (connection, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                var rows = connection.Query("SELECT id, name, isActive FROM playsets ORDER BY createdOn ASC");
                var result = new List<PlaySet>(rows.Count);
                foreach (var row in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    var id = ParseGuid(row, "id", "playsets");
                    if (id is null)
                    {
                        continue;
                    }

                    result.Add(new PlaySet(id.Value, ReadString(row, "name"), ReadBoolean(row, "isActive")));
                }

                return (IReadOnlyList<PlaySet>)result;
            },
            (IReadOnlyList<PlaySet>)Array.Empty<PlaySet>(),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<PlaySet?> GetActivePlaySetAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        return ReadAsync(
            game,
            "读取激活播放集",
            (connection, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                var rows = connection.Query(
                    "SELECT id, name, isActive FROM playsets WHERE isActive = 1 ORDER BY createdOn DESC LIMIT 1");
                if (rows.Count == 0)
                {
                    return (PlaySet?)null;
                }

                var id = ParseGuid(rows[0], "id", "playsets");
                return id is null
                    ? null
                    : new PlaySet(id.Value, ReadString(rows[0], "name"), ReadBoolean(rows[0], "isActive"));
            },
            (PlaySet?)null,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySetMod>> GetPlaySetModsAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        return ReadAsync(
            game,
            "读取播放集 mod 行",
            (connection, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                var rows = connection.Query(
                    "SELECT pm.position, pm.enabled, m.id, m.displayName, m.dirPath, m.archivePath, " +
                    "m.gameRegistryId, m.thumbnailPath, m.requiredVersion, m.status FROM playsets_mods pm " +
                    "JOIN mods m ON m.id = pm.modId WHERE pm.playSetId = ? ORDER BY pm.position ASC",
                    playSetId.ToString("D"));

                var result = new List<PlaySetMod>(rows.Count);
                foreach (var row in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    var modId = ParseGuid(row, "id", "mods");
                    if (modId is null)
                    {
                        continue;
                    }

                    result.Add(new PlaySetMod(
                        modId.Value,
                        ReadString(row, "displayName"),
                        ReadString(row, "dirPath"),
                        ReadNullableString(row, "archivePath"),
                        ReadString(row, "gameRegistryId"),
                        ResolveModThumbnail(game, row, modId.Value),
                        ReadString(row, "requiredVersion"),
                        ReadString(row, "status"),
                        ReadBoolean(row, "enabled"),
                        ReadInt32(row, "position")));
                }

                return (IReadOnlyList<PlaySetMod>)result;
            },
            (IReadOnlyList<PlaySetMod>)Array.Empty<PlaySetMod>(),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ModLibraryEntry>> GetModLibraryAsync(
        GameInstallation game,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        return ReadAsync(
            game,
            "读取 Mod 库",
            (connection, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                // 用 SELECT *：不同游戏/版本的 mods 表列不完全一致（部分库没有 name/size/subscribedDate），
                // 缺列时按 null 处理，不让整个 Mod 库因为一个缺列变成空白。
                var rows = connection.Query("SELECT * FROM mods ORDER BY displayName COLLATE NOCASE ASC");
                if (rows.Count == 0)
                {
                    return (IReadOnlyList<ModLibraryEntry>)Array.Empty<ModLibraryEntry>();
                }

                // 归属关系必须单独查一次：mods 表本身不记录「属于哪些播放集」。
                var memberships = ReadPlaySetMemberships(connection, ct);
                var result = new List<ModLibraryEntry>(rows.Count);
                foreach (var row in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    var modId = ParseGuid(row, "id", "mods");
                    if (modId is null)
                    {
                        continue;
                    }

                    var playSetIds = memberships.TryGetValue(modId.Value, out var known)
                        ? known
                        : (IReadOnlyList<Guid>)Array.Empty<Guid>();

                    result.Add(new ModLibraryEntry(
                        modId.Value,
                        ReadModDisplayName(row),
                        ReadString(row, "gameRegistryId"),
                        ReadNullableString(row, "dirPath"),
                        ReadNullableString(row, "archivePath"),
                        ResolveModThumbnail(game, row, modId.Value),
                        ReadString(row, "requiredVersion"),
                        ReadString(row, "status"),
                        ReadString(row, "source"),
                        ReadNullableInt64(row, "size"),
                        ReadNullableTimestamp(row, "subscribedDate"),
                        playSetIds,
                        // steamId 是较新的列：老库缺失时 ReadNullableString 返回 null，
                        // 与「这个 mod 没有创意工坊地址」是同一种展示结果。
                        SteamId: ReadNullableString(row, "steamId"),
                        ThumbnailUrl: ReadNullableString(row, "thumbnailUrl")));
                }

                // SQLite 的 COLLATE NOCASE 只折叠 ASCII，中文/符号排序和 .NET 不一致；
                // 统一在托管层按 OrdinalIgnoreCase 排序，保证仓库与 UI 看到的顺序完全相同。
                result.Sort(static (left, right) =>
                {
                    var byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
                    return byName != 0 ? byName : left.ModId.CompareTo(right.ModId);
                });

                return (IReadOnlyList<ModLibraryEntry>)result;
            },
            (IReadOnlyList<ModLibraryEntry>)Array.Empty<ModLibraryEntry>(),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySetDlc>> GetPlaySetDlcsAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        return ReadAsync(
            game,
            "读取播放集 DLC 行",
            (connection, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                // Stellaris 的库里没有 playsets_dlcs 表，必须先探测再查，否则会抛 no such table。
                if (!connection.TableExists("playsets_dlcs"))
                {
                    return (IReadOnlyList<PlaySetDlc>)Array.Empty<PlaySetDlc>();
                }

                var rows = connection.Query(
                    "SELECT dlcId, enabled FROM playsets_dlcs WHERE playsetId = ? ORDER BY dlcId ASC",
                    playSetId.ToString("D"));

                var result = new List<PlaySetDlc>(rows.Count);
                foreach (var row in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    var dlcId = ReadString(row, "dlcId");
                    if (string.IsNullOrEmpty(dlcId))
                    {
                        continue;
                    }

                    result.Add(new PlaySetDlc(dlcId, ReadBoolean(row, "enabled")));
                }

                return (IReadOnlyList<PlaySetDlc>)result;
            },
            (IReadOnlyList<PlaySetDlc>)Array.Empty<PlaySetDlc>(),
            cancellationToken);
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
            game,
            "保存播放集 mod 行",
            (connection, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                connection.Execute("DELETE FROM playsets_mods WHERE playsetId = ?", playSetId.ToString("D"));
                var position = 0;
                foreach (var mod in mods)
                {
                    ct.ThrowIfCancellationRequested();
                    ArgumentNullException.ThrowIfNull(mod);
                    connection.Execute(
                        "INSERT INTO playsets_mods (playsetId, modId, enabled, position) VALUES (?, ?, ?, ?)",
                        playSetId.ToString("D"),
                        mod.ModId.ToString("D"),
                        mod.Enabled ? 1 : 0,
                        mod.Position >= 0 ? mod.Position : position);
                    position++;
                }

                return OkBool;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<AppResult<bool>> SavePlaySetDlcsAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetDlc> dlcs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(dlcs);
        return WriteAsync(
            game,
            "保存播放集 DLC 行",
            (connection, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                if (!connection.TableExists("playsets_dlcs"))
                {
                    // 该游戏（Stellaris）官方就没有 DLC 开关表，静默跳过是正确行为。
                    _logger.Warn("游戏 " + game.GameId + " 的 launcher-v2.sqlite 没有 playsets_dlcs 表，已跳过 DLC 保存。");
                    return OkBool;
                }

                connection.Execute("DELETE FROM playsets_dlcs WHERE playsetId = ?", playSetId.ToString("D"));
                foreach (var dlc in dlcs)
                {
                    ct.ThrowIfCancellationRequested();
                    ArgumentNullException.ThrowIfNull(dlc);
                    if (string.IsNullOrEmpty(dlc.DlcId))
                    {
                        continue;
                    }

                    connection.Execute(
                        "INSERT INTO playsets_dlcs (playsetId, dlcId, enabled) VALUES (?, ?, ?)",
                        playSetId.ToString("D"),
                        dlc.DlcId,
                        dlc.Enabled ? 1 : 0);
                }

                return OkBool;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<AppResult<Guid>> CreatePlaySetAsync(
        GameInstallation game,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        var playSetName = string.IsNullOrWhiteSpace(name) ? "New Playset" : name.Trim();
        var id = Guid.NewGuid();
        var createdOn = _clock.UtcNow.ToUnixTimeMilliseconds();
        return WriteAsync<Guid>(
            game,
            "创建播放集",
            (connection, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                connection.Execute(
                    "INSERT INTO playsets (id, name, isActive, createdOn) VALUES (?, ?, 0, ?)",
                    id.ToString("D"),
                    playSetName,
                    createdOn);
                return AppResult<Guid>.Ok(id);
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
        var id = playSetId.ToString("D");
        return WriteAsync(
            game,
            "设置激活播放集",
            (connection, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                connection.Execute("UPDATE playsets SET isActive = 0 WHERE isActive = 1");
                var changed = connection.Execute("UPDATE playsets SET isActive = 1 WHERE id = ?", id);
                return changed == 0
                    ? AppResult<bool>.Fail(ErrorKind.NotFound, "playSet-not-found:" + id)
                    : OkBool;
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

        var trimmed = newName.Trim();
        var updatedOn = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = playSetId.ToString("D");
        return WriteAsync(
            game,
            "重命名播放集",
            (connection, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                var changed = connection.Execute(
                    "UPDATE playsets SET name = ?, updatedOn = ? WHERE id = ?",
                    trimmed,
                    updatedOn,
                    id);
                return changed == 0
                    ? AppResult<bool>.Fail(ErrorKind.NotFound, "playSet-not-found:" + id)
                    : OkBool;
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
        var id = playSetId.ToString("D");
        return WriteAsync(
            game,
            "删除播放集",
            (connection, ct) =>
            {
                ct.ThrowIfCancellationRequested();

                // 活动播放集不允许删除。
                var active = connection.ExecuteScalar("SELECT isActive FROM playsets WHERE id = ?", id);
                if (active is null)
                {
                    return AppResult<bool>.Fail(ErrorKind.NotFound, "playSet-not-found:" + id);
                }

                if (ToBoolean(active))
                {
                    _logger.Warn("播放集 " + id + " 处于激活状态，拒绝删除。");
                    return AppResult<bool>.Fail(ErrorKind.Conflict, "playSet-is-active:" + id);
                }

                connection.Execute("DELETE FROM playsets_mods WHERE playsetId = ?", id);
                if (connection.TableExists("playsets_dlcs"))
                {
                    connection.Execute("DELETE FROM playsets_dlcs WHERE playsetId = ?", id);
                }

                return connection.Execute("DELETE FROM playsets WHERE id = ?", id) > 0
                    ? OkBool
                    : AppResult<bool>.Fail(ErrorKind.NotFound, "playSet-not-found:" + id);
            },
            cancellationToken);
    }

    private Task<T> ReadAsync<T>(
        GameInstallation game,
        string action,
        Func<SqliteConnection, CancellationToken, T> body,
        T fallback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var databasePath = GetDatabasePath(game);
                if (!_fileSystem.FileExists(databasePath))
                {
                    _logger.Warn(action + "：数据库不存在，按空结果处理：" + databasePath);
                    return fallback;
                }

                try
                {
                    // 读取必须只读打开：绝不因为读而锁库，影响官方启动器写入。
                    using var connection = SqliteConnection.OpenReadOnly(databasePath);
                    return body(connection, cancellationToken);
                }
                catch (SqliteException exception)
                {
                    // 解析失败不抛给 UI：记 Warn 后返回默认值。
                    _logger.Warn(action + " 失败（" + databasePath + "）：" + exception.Message);
                    return fallback;
                }
                catch (IOException exception)
                {
                    _logger.Warn(action + " 读取失败（" + databasePath + "）：" + exception.Message);
                    return fallback;
                }
                catch (UnauthorizedAccessException exception)
                {
                    _logger.Warn(action + " 无权限（" + databasePath + "）：" + exception.Message);
                    return fallback;
                }
            },
            cancellationToken);
    }

    /// <summary>无返回值的写操作：成功后统一返回 <c>Ok(true)</c>。</summary>
    private Task<AppResult<bool>> WriteAsync(
        GameInstallation game,
        string action,
        Action<SqliteConnection, CancellationToken> body,
        CancellationToken cancellationToken)
        => WriteAsync(
            game,
            action,
            (connection, ct) =>
            {
                body(connection, ct);
                return OkBool;
            },
            cancellationToken);

    /// <summary>
    /// 写库统一入口。<b>不再有 fallback</b>：数据库缺失、备份失败、SQLite 异常一律返回失败结果，
    /// 绝不把「没写成功」伪装成成功值（这是旧版 P0-1 数据静默丢失的根因）。
    /// </summary>
    private async Task<AppResult<T>> WriteAsync<T>(
        GameInstallation game,
        string action,
        Func<SqliteConnection, CancellationToken, AppResult<T>> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var databasePath = GetDatabasePath(game);
        var gate = GetWriteGate(databasePath);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_fileSystem.FileExists(databasePath))
                    {
                        _logger.Warn(action + "：数据库不存在：" + databasePath);
                        return AppResult<T>.Fail(ErrorKind.NotFound, "database-not-found:" + databasePath);
                    }

                    var backup = BackupDatabase(databasePath, action);
                    if (!backup.IsSuccess)
                    {
                        // 备份失败就中止：备份的意义是能回退，备份不成就没有回退点。
                        return AppResult<T>.Fail(ErrorKind.Io, backup.ErrorMessage!);
                    }

                    try
                    {
                        using var connection = SqliteConnection.OpenReadWrite(databasePath);
                        connection.BeginImmediate();
                        try
                        {
                            var result = body(connection, cancellationToken);
                            if (!result.IsSuccess)
                            {
                                connection.Rollback();
                                return result;
                            }

                            connection.Commit();
                            return result;
                        }
                        catch
                        {
                            connection.Rollback();
                            throw;
                        }
                    }
                    catch (SqliteException exception)
                    {
                        _logger.Error(action + " 失败（" + databasePath + "）", exception);
                        return AppResult<T>.Fail(ErrorKind.Io, action + ":" + exception.Message);
                    }
                    catch (IOException exception)
                    {
                        _logger.Error(action + " 失败（" + databasePath + "）", exception);
                        return AppResult<T>.Fail(ErrorKind.Io, action + ":" + exception.Message);
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        _logger.Error(action + " 无权限（" + databasePath + "）", exception);
                        return AppResult<T>.Fail(ErrorKind.Io, action + ":" + exception.Message);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>写前备份。<b>失败即中止写入</b>（P0-2）：返回失败结果，调用方不再继续。</summary>
    private AppResult<bool> BackupDatabase(string databasePath, string action)
    {
        try
        {
            var backupPath = GetBackupPath(databasePath);
            _fileSystem.Copy(databasePath, backupPath, overwrite: true);
            _logger.Info(action + "：已备份数据库到 " + backupPath);
            return OkBool;
        }
        catch (IOException exception)
        {
            _logger.Error(action + "：备份数据库失败，已中止写入（" + databasePath + "）", exception);
            return AppResult<bool>.Fail(ErrorKind.Io, "backup:" + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Error(action + "：备份数据库无权限，已中止写入（" + databasePath + "）", exception);
            return AppResult<bool>.Fail(ErrorKind.Io, "backup:" + exception.Message);
        }
    }

    private static SemaphoreSlim GetWriteGate(string databasePath)
    {
        var key = Path.GetFullPath(databasePath);
        lock (GateLock)
        {
            if (!WriteGates.TryGetValue(key, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                WriteGates[key] = gate;
            }

            return gate;
        }
    }

    /// <summary>
    /// 解析一行 mod 的缩略图：交给 <see cref="ModThumbnailResolver"/>，
    /// 由它处理「库搬迁后 dirPath 失效」与「只认 thumbnail.png」两个历史问题。
    /// </summary>
    private string? ResolveModThumbnail(GameInstallation game, IReadOnlyDictionary<string, object?> row, Guid modId)
        => _thumbnails.Resolve(
            game,
            dirPath: ReadNullableString(row, "dirPath"),
            archivePath: ReadNullableString(row, "archivePath"),
            // steamId / gameRegistryId 都可能缺列（固定列 SQL 不含 steamId）：
            // ReadNullableString 对缺列安全返回 null，gameRegistryId（mod/ugc_<id>.mod）足以反解创意工坊 id。
            steamId: ReadNullableString(row, "steamId"),
            gameRegistryId: ReadNullableString(row, "gameRegistryId"),
            cachedThumbnailPath: ReadNullableString(row, "thumbnailPath"),
            modId: modId);

    /// <summary>读取 modId → 播放集 Id 列表（按播放集创建时间升序）。</summary>
    private static Dictionary<Guid, IReadOnlyList<Guid>> ReadPlaySetMemberships(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, IReadOnlyList<Guid>>();
        if (!connection.TableExists("playsets_mods"))
        {
            return result;
        }

        var rows = connection.Query(
            "SELECT pm.modId, pm.playsetId FROM playsets_mods pm " +
            "JOIN playsets p ON p.id = pm.playsetId ORDER BY p.createdOn ASC");

        var builder = new Dictionary<Guid, List<Guid>>();
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(ReadNullableString(row, "modId"), out var modId))
            {
                continue;
            }

            if (!Guid.TryParse(ReadNullableString(row, "playsetId"), out var playSetId))
            {
                continue;
            }

            if (!builder.TryGetValue(modId, out var playSetIds))
            {
                playSetIds = new List<Guid>();
                builder[modId] = playSetIds;
            }

            if (!playSetIds.Contains(playSetId))
            {
                playSetIds.Add(playSetId);
            }
        }

        foreach (var pair in builder)
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    /// <summary>展示名回退链：displayName → name → 目录名 → Unknown Mod（与 ModCatalog 保持一致）。</summary>
    private static string ReadModDisplayName(IReadOnlyDictionary<string, object?> row)
    {
        var displayName = ReadNullableString(row, "displayName");
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        var name = ReadNullableString(row, "name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        var dirPath = ReadNullableString(row, "dirPath");
        if (!string.IsNullOrEmpty(dirPath))
        {
            var directoryName = Path.GetFileName(dirPath.TrimEnd('\\', '/'));
            if (!string.IsNullOrEmpty(directoryName))
            {
                return directoryName;
            }
        }

        return "Unknown Mod";
    }

    private Guid? ParseGuid(IReadOnlyDictionary<string, object?> row, string column, string table)
    {
        var text = ReadNullableString(row, column);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (Guid.TryParse(text, out var id))
        {
            return id;
        }

        _logger.Warn("表 " + table + " 的 " + column + " 不是合法 GUID，已跳过该行：" + text);
        return null;
    }

    internal static string ReadString(IReadOnlyDictionary<string, object?> row, string column)
        => ReadNullableString(row, column) ?? string.Empty;

    internal static string? ReadNullableString(IReadOnlyDictionary<string, object?> row, string column)
    {
        if (!row.TryGetValue(column, out var value) || value is null || value is DBNull)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }

    internal static bool ReadBoolean(IReadOnlyDictionary<string, object?> row, string column)
    {
        if (!row.TryGetValue(column, out var value))
        {
            return false;
        }

        return ToBoolean(value);
    }

    internal static bool ToBoolean(object? value)
    {
        return value switch
        {
            null => false,
            DBNull => false,
            bool flag => flag,
            string text => text is "1" or "true" or "TRUE" or "True" || (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed != 0),
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0,
        };
    }

    internal static long? ReadNullableInt64(IReadOnlyDictionary<string, object?> row, string column)
    {
        if (!row.TryGetValue(column, out var value) || value is null || value is DBNull)
        {
            return null;
        }

        return value switch
        {
            long number => number,
            int number => number,
            double number => (long)Math.Round(number),
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>解析 SUBSCRIBEDDATE 一类的 SQLite 时间列：优先 Unix 秒/毫秒，退化为 ISO 文本。</summary>
    internal static DateTimeOffset? ReadNullableTimestamp(IReadOnlyDictionary<string, object?> row, string column)
    {
        if (!row.TryGetValue(column, out var value) || value is null || value is DBNull)
        {
            return null;
        }

        switch (value)
        {
            case long seconds:
                return FromUnixTimestamp(seconds);
            case int seconds:
                return FromUnixTimestamp(seconds);
            case double seconds:
                return FromUnixTimestamp((long)seconds);
            case string text when string.IsNullOrWhiteSpace(text) is false:
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return FromUnixTimestamp(parsed);
                }

                return DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedDate)
                    ? parsedDate
                    : null;
            default:
                return null;
        }
    }

    private static DateTimeOffset? FromUnixTimestamp(long value)
    {
        if (value <= 0)
        {
            return null;
        }

        // 官方启动器多数库里是秒，个别版本会写毫秒，按量级自动判别。
        return value > 100_000_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds(value)
            : DateTimeOffset.FromUnixTimeSeconds(value);
    }

    internal static int ReadInt32(IReadOnlyDictionary<string, object?> row, string column)
    {
        if (!row.TryGetValue(column, out var value) || value is null || value is DBNull)
        {
            return 0;
        }

        return value switch
        {
            long number => checked((int)number),
            int number => number,
            double number => (int)number,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => Convert.ToInt32(value, CultureInfo.InvariantCulture),
        };
    }
}
