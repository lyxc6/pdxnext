namespace PdxLauncher.Core.Playsets;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Storage;

/// <summary>
/// 自有库（<c>data\pdxnext.sqlite</c>）版的播放集仓储。
/// </summary>
/// <remarks>
/// 它存在的理由是「用户没装官方启动器」：官方 <c>launcher-v2.sqlite</c> 不在时，播放集、Mod 库、
/// DLC 开关全都还能用，数据落在我们自己的库（带 <c>gameId</c>，一库多游戏）。
/// 官方库在的时候由 <see cref="LauncherDbProjector"/> 两边同步，读仍以官方库为准。
/// <para>错误码与 <see cref="PlaySetRepository"/> 保持一致，展示层不必分实现。</para>
/// </remarks>
public sealed class OwnDatabasePlaySetRepository : IPlaySetRepository
{
    private readonly PdxDatabase _database;
    private readonly IAppLogger _logger;
    private readonly IClock _clock;

    /// <summary>创建自有库仓储。</summary>
    public OwnDatabasePlaySetRepository(PdxDatabase database, IAppLogger logger, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);
        _database = database;
        _logger = logger;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySet>> GetPlaySetsAsync(
        GameInstallation game,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        return ReadAsync(
            game,
            "读取播放集列表",
            Array.Empty<PlaySet>(),
            connection =>
            {
                var rows = connection.Query(
                    "SELECT id, name, isActive FROM playsets WHERE gameId = ? ORDER BY loadOrder ASC, createdOn ASC",
                    game.GameId);

                var result = new List<PlaySet>(rows.Count);
                foreach (var row in rows)
                {
                    var id = ParseId(row, "id");
                    if (id == Guid.Empty)
                    {
                        continue;
                    }

                    result.Add(new PlaySet(id, PlaySetRepository.ReadString(row, "name"), PlaySetRepository.ReadBoolean(row, "isActive")));
                }

                return (IReadOnlyList<PlaySet>)result;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<PlaySet?> GetActivePlaySetAsync(
        GameInstallation game,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        return ReadAsync<PlaySet?>(
            game,
            "读取激活播放集",
            null,
            connection =>
            {
                var rows = connection.Query(
                    "SELECT id, name, isActive FROM playsets WHERE gameId = ? AND isActive = 1 ORDER BY loadOrder ASC LIMIT 1",
                    game.GameId);

                foreach (var row in rows)
                {
                    var id = ParseId(row, "id");
                    if (id != Guid.Empty)
                    {
                        return new PlaySet(id, PlaySetRepository.ReadString(row, "name"), true);
                    }
                }

                return null;
            },
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
            "读取播放集 mod",
            Array.Empty<PlaySetMod>(),
            connection =>
            {
                var rows = connection.Query(
                    "SELECT pm.position, pm.enabled, m.id, m.displayName, m.dirPath, m.archivePath, m.gameRegistryId, " +
                    "m.thumbnailPath, m.requiredVersion, m.status, m.steamId, m.source FROM playsets_mods pm " +
                    "JOIN mods m ON m.gameId = pm.gameId AND m.id = pm.modId " +
                    "WHERE pm.gameId = ? AND pm.playsetId = ? ORDER BY pm.position ASC",
                    game.GameId,
                    playSetId.ToString("D"));

                var result = new List<PlaySetMod>(rows.Count);
                foreach (var row in rows)
                {
                    var modId = ParseId(row, "id");
                    if (modId == Guid.Empty)
                    {
                        continue;
                    }

                    result.Add(new PlaySetMod(
                        modId,
                        PlaySetRepository.ReadString(row, "displayName"),
                        PlaySetRepository.ReadString(row, "dirPath"),
                        PlaySetRepository.ReadNullableString(row, "archivePath"),
                        PlaySetRepository.ReadString(row, "gameRegistryId"),
                        PlaySetRepository.ReadNullableString(row, "thumbnailPath"),
                        PlaySetRepository.ReadString(row, "requiredVersion"),
                        PlaySetRepository.ReadString(row, "status"),
                        PlaySetRepository.ReadBoolean(row, "enabled"),
                        PlaySetRepository.ReadInt32(row, "position"),
                        PlaySetRepository.ReadNullableString(row, "steamId"),
                        PlaySetRepository.ReadNullableString(row, "source")));
                }

                return (IReadOnlyList<PlaySetMod>)result;
            },
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
            Array.Empty<ModLibraryEntry>(),
            connection =>
            {
                var memberships = ReadMemberships(connection, game.GameId);
                var rows = connection.Query(
                    "SELECT id, displayName, gameRegistryId, dirPath, archivePath, thumbnailPath, thumbnailUrl, " +
                    "requiredVersion, status, source, size, subscribedDate, steamId FROM mods WHERE gameId = ?",
                    game.GameId);

                var result = new List<ModLibraryEntry>(rows.Count);
                foreach (var row in rows)
                {
                    var modId = ParseId(row, "id");
                    if (modId == Guid.Empty)
                    {
                        continue;
                    }

                    result.Add(new ModLibraryEntry(
                        modId,
                        PlaySetRepository.ReadString(row, "displayName"),
                        PlaySetRepository.ReadString(row, "gameRegistryId"),
                        PlaySetRepository.ReadNullableString(row, "dirPath"),
                        PlaySetRepository.ReadNullableString(row, "archivePath"),
                        PlaySetRepository.ReadNullableString(row, "thumbnailPath"),
                        PlaySetRepository.ReadString(row, "requiredVersion"),
                        PlaySetRepository.ReadString(row, "status"),
                        ReadSourceOrDefault(row),
                        PlaySetRepository.ReadNullableInt64(row, "size"),
                        PlaySetRepository.ReadNullableTimestamp(row, "subscribedDate"),
                        memberships.TryGetValue(modId, out var ids) ? ids : Array.Empty<Guid>(),
                        IsUnregistered: false,
                        SteamId: PlaySetRepository.ReadNullableString(row, "steamId"),
                        ThumbnailUrl: PlaySetRepository.ReadNullableString(row, "thumbnailUrl")));
                }

                // 官方库那版是按 displayName 排序的；这里同样按名字排，两边的列表顺序才一致。
                result.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
                return (IReadOnlyList<ModLibraryEntry>)result;
            },
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
            "读取播放集 DLC",
            Array.Empty<PlaySetDlc>(),
            connection =>
            {
                var rows = connection.Query(
                    "SELECT dlcId, enabled FROM playsets_dlcs WHERE gameId = ? AND playsetId = ?",
                    game.GameId,
                    playSetId.ToString("D"));

                var result = new List<PlaySetDlc>(rows.Count);
                foreach (var row in rows)
                {
                    var dlcId = PlaySetRepository.ReadString(row, "dlcId");
                    if (string.IsNullOrEmpty(dlcId))
                    {
                        continue;
                    }

                    result.Add(new PlaySetDlc(dlcId, PlaySetRepository.ReadBoolean(row, "enabled")));
                }

                return (IReadOnlyList<PlaySetDlc>)result;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public Task<AppResult<bool>> UpdateModPathsAsync(
        GameInstallation game,
        Guid modId,
        string? dirPath,
        string? archivePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (modId == Guid.Empty)
        {
            return Task.FromResult(AppResult<bool>.Fail(ErrorKind.Invalid, "mod-id-empty"));
        }

        var id = modId.ToString("D");
        var updatedAt = _clock.UtcNow.ToUnixTimeMilliseconds();
        return WriteAsync(
            game,
            "修正 mod 路径",
            connection =>
            {
                var sets = new List<string>(3);
                var parameters = new List<object?>(4);

                // 空入参表示「这一列保持原样」，所以两个路径都为空时直接返回。
                if (!string.IsNullOrWhiteSpace(dirPath))
                {
                    sets.Add("dirPath = ?");
                    parameters.Add(dirPath);
                }

                if (!string.IsNullOrWhiteSpace(archivePath))
                {
                    sets.Add("archivePath = ?");
                    parameters.Add(archivePath);
                }

                if (sets.Count == 0)
                {
                    return AppResult<bool>.Ok(false);
                }

                sets.Add("updatedAt = ?");
                parameters.Add(updatedAt);
                parameters.Add(game.GameId);
                parameters.Add(id);

                var affected = connection.Execute(
                    "UPDATE mods SET " + string.Join(", ", sets) + " WHERE gameId = ? AND id = ?",
                    parameters.ToArray());

                return affected > 0
                    ? AppResult<bool>.Ok(true)
                    : AppResult<bool>.Fail(ErrorKind.NotFound, "mod-not-found:" + id);
            },
            cancellationToken);
    }

    public Task<AppResult<bool>> SavePlaySetModsAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetMod> mods,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(mods);
        var id = playSetId.ToString("D");
        var updatedAt = _clock.UtcNow.ToUnixTimeMilliseconds();
        return WriteAsync(
            game,
            "保存播放集 mod",
            connection =>
            {
                if (!PlaySetExists(connection, game.GameId, id))
                {
                    return AppResult<bool>.Fail(ErrorKind.NotFound, "playSet-not-found:" + id);
                }

                // 先把 mod 行补上，再删旧关联：不然新 mod 在 mods 表里还没有行时 join 会读不出来。
                OwnModStore.UpsertRows(connection, game.GameId, mods, updatedAt);

                connection.Execute(
                    "DELETE FROM playsets_mods WHERE gameId = ? AND playsetId = ?",
                    game.GameId,
                    id);

                var position = 0;
                foreach (var mod in mods)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (mod is null || mod.ModId == Guid.Empty)
                    {
                        continue;
                    }

                    connection.Execute(
                        "INSERT INTO playsets_mods (gameId, playsetId, modId, enabled, position) VALUES (?, ?, ?, ?, ?)",
                        game.GameId,
                        id,
                        mod.ModId.ToString("D"),
                        mod.Enabled ? 1 : 0,
                        position);
                    position++;
                }

                TouchPlaySet(connection, game.GameId, id, updatedAt);
                return AppResult<bool>.Ok(true);
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
        var id = playSetId.ToString("D");
        var updatedAt = _clock.UtcNow.ToUnixTimeMilliseconds();
        return WriteAsync(
            game,
            "保存播放集 DLC",
            connection =>
            {
                if (!PlaySetExists(connection, game.GameId, id))
                {
                    return AppResult<bool>.Fail(ErrorKind.NotFound, "playSet-not-found:" + id);
                }

                connection.Execute(
                    "DELETE FROM playsets_dlcs WHERE gameId = ? AND playsetId = ?",
                    game.GameId,
                    id);

                foreach (var dlc in dlcs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (dlc is null || string.IsNullOrEmpty(dlc.DlcId))
                    {
                        continue;
                    }

                    connection.Execute(
                        "INSERT INTO playsets_dlcs (gameId, playsetId, dlcId, enabled) VALUES (?, ?, ?, ?)",
                        game.GameId,
                        id,
                        dlc.DlcId,
                        dlc.Enabled ? 1 : 0);
                }

                TouchPlaySet(connection, game.GameId, id, updatedAt);
                return AppResult<bool>.Ok(true);
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
        var newId = Guid.NewGuid();
        var id = newId.ToString("D");
        var createdOn = _clock.UtcNow.ToUnixTimeMilliseconds();
        return WriteReturningAsync(
            game,
            "创建播放集",
            connection =>
            {
                var orderRows = connection.Query(
                    "SELECT COALESCE(MAX(loadOrder), 0) + 1 AS next FROM playsets WHERE gameId = ?",
                    game.GameId);
                var nextOrder = orderRows.Count == 0
                    ? 1L
                    : PlaySetRepository.ReadNullableInt64(orderRows[0], "next") ?? 1L;
                connection.Execute(
                    "INSERT INTO playsets (gameId, id, name, isActive, loadOrder, createdOn, updatedOn) VALUES (?, ?, ?, 0, ?, ?, ?)",
                    game.GameId,
                    id,
                    playSetName,
                    nextOrder,
                    createdOn,
                    createdOn);
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
        var id = playSetId.ToString("D");
        var updatedAt = _clock.UtcNow.ToUnixTimeMilliseconds();
        return WriteAsync(
            game,
            "设置激活播放集",
            connection =>
            {
                connection.Execute(
                    "UPDATE playsets SET isActive = 0, updatedOn = ? WHERE gameId = ? AND isActive = 1",
                    updatedAt,
                    game.GameId);

                var changed = connection.Execute(
                    "UPDATE playsets SET isActive = 1, updatedOn = ? WHERE gameId = ? AND id = ?",
                    updatedAt,
                    game.GameId,
                    id);

                return changed == 0
                    ? AppResult<bool>.Fail(ErrorKind.NotFound, "playSet-not-found:" + id)
                    : AppResult<bool>.Ok(true);
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
        var id = playSetId.ToString("D");
        var updatedAt = _clock.UtcNow.ToUnixTimeMilliseconds();
        return WriteAsync(
            game,
            "重命名播放集",
            connection =>
            {
                var changed = connection.Execute(
                    "UPDATE playsets SET name = ?, updatedOn = ? WHERE gameId = ? AND id = ?",
                    trimmed,
                    updatedAt,
                    game.GameId,
                    id);

                return changed == 0
                    ? AppResult<bool>.Fail(ErrorKind.NotFound, "playSet-not-found:" + id)
                    : AppResult<bool>.Ok(true);
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
            connection =>
            {
                var active = connection.ExecuteScalar(
                    "SELECT isActive FROM playsets WHERE gameId = ? AND id = ?",
                    game.GameId,
                    id);

                if (active is null)
                {
                    return AppResult<bool>.Fail(ErrorKind.NotFound, "playSet-not-found:" + id);
                }

                if (PlaySetRepository.ToBoolean(active))
                {
                    _logger.Warn("播放集 " + id + " 处于激活状态，拒绝删除。");
                    return AppResult<bool>.Fail(ErrorKind.Conflict, "playSet-is-active:" + id);
                }

                DeletePlaySetRows(connection, game.GameId, id);

                return connection.Execute(
                    "DELETE FROM playsets WHERE gameId = ? AND id = ?",
                    game.GameId,
                    id) > 0
                    ? AppResult<bool>.Ok(true)
                    : AppResult<bool>.Fail(ErrorKind.NotFound, "playSet-not-found:" + id);
            },
            cancellationToken);
    }

    /// <summary>删除播放集的子表行（供投射/清理复用）。</summary>
    internal static void DeletePlaySetRows(SqliteConnection connection, string gameId, string playSetId)
    {
        connection.Execute("DELETE FROM playsets_mods WHERE gameId = ? AND playsetId = ?", gameId, playSetId);
        connection.Execute("DELETE FROM playsets_dlcs WHERE gameId = ? AND playsetId = ?", gameId, playSetId);
    }

    /// <summary>删掉这款游戏在自有库里的全部行（整份导入前先清空）。</summary>
    internal static void DeleteGameRows(SqliteConnection connection, string gameId)
    {
        connection.Execute("DELETE FROM playsets_mods WHERE gameId = ?", gameId);
        connection.Execute("DELETE FROM playsets_dlcs WHERE gameId = ?", gameId);
        connection.Execute("DELETE FROM playsets WHERE gameId = ?", gameId);
        connection.Execute("DELETE FROM mods WHERE gameId = ?", gameId);
    }

    private static bool PlaySetExists(SqliteConnection connection, string gameId, string playSetId)
    {
        var rows = connection.Query(
            "SELECT 1 AS ok FROM playsets WHERE gameId = ? AND id = ? LIMIT 1",
            gameId,
            playSetId);
        return rows.Count > 0;
    }

    private static void TouchPlaySet(SqliteConnection connection, string gameId, string playSetId, long updatedAt)
    {
        connection.Execute(
            "UPDATE playsets SET updatedOn = ? WHERE gameId = ? AND id = ?",
            updatedAt,
            gameId,
            playSetId);
    }

    private static Dictionary<Guid, IReadOnlyList<Guid>> ReadMemberships(SqliteConnection connection, string gameId)
    {
        var rows = connection.Query(
            "SELECT modId, playsetId FROM playsets_mods WHERE gameId = ? ORDER BY position ASC",
            gameId);

        var map = new Dictionary<Guid, List<Guid>>();
        foreach (var row in rows)
        {
            var modId = ParseId(row, "modId");
            var playSetId = ParseId(row, "playsetId");
            if (modId == Guid.Empty || playSetId == Guid.Empty)
            {
                continue;
            }

            if (!map.TryGetValue(modId, out var list))
            {
                list = new List<Guid>();
                map[modId] = list;
            }

            if (!list.Contains(playSetId))
            {
                list.Add(playSetId);
            }
        }

        var result = new Dictionary<Guid, IReadOnlyList<Guid>>(map.Count);
        foreach (var pair in map)
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    private static string ReadSourceOrDefault(IReadOnlyDictionary<string, object?> row)
    {
        var source = PlaySetRepository.ReadNullableString(row, "source");
        return string.IsNullOrWhiteSpace(source) ? "local" : source;
    }

    private static Guid ParseId(IReadOnlyDictionary<string, object?> row, string column)
    {
        var text = PlaySetRepository.ReadString(row, column);
        return Guid.TryParse(text, out var id) ? id : Guid.Empty;
    }

    private Task<T> ReadAsync<T>(
        GameInstallation game,
        string action,
        T fallback,
        Func<SqliteConnection, T> body,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var connection = _database.Open();
                    return body(connection);
                }
                catch (SqliteException exception)
                {
                    // 自有库读失败不该把 UI 打挂：记 Warn 后按空结果继续。
                    _logger.Warn(action + "失败（" + _database.Path + "）：" + exception.Message);
                    return fallback;
                }
                catch (IOException exception)
                {
                    _logger.Warn(action + "失败（" + _database.Path + "）：" + exception.Message);
                    return fallback;
                }
                catch (UnauthorizedAccessException exception)
                {
                    _logger.Warn(action + "无权限（" + _database.Path + "）：" + exception.Message);
                    return fallback;
                }
            },
            cancellationToken);
    }

    private Task<AppResult<bool>> WriteAsync(
        GameInstallation game,
        string action,
        Func<SqliteConnection, AppResult<bool>> body,
        CancellationToken cancellationToken)
        => WriteReturningAsync(game, action, body, cancellationToken);

    private Task<AppResult<T>> WriteReturningAsync<T>(
        GameInstallation game,
        string action,
        Func<SqliteConnection, AppResult<T>> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return _database.Write(body);
                }
                catch (SqliteException exception)
                {
                    _logger.Warn(action + "失败（" + _database.Path + "）：" + exception.Message);
                    return AppResult<T>.Fail(ErrorKind.Io, "sqlite:" + exception.Message);
                }
                catch (IOException exception)
                {
                    _logger.Warn(action + "失败（" + _database.Path + "）：" + exception.Message);
                    return AppResult<T>.Fail(ErrorKind.Io, "io:" + exception.Message);
                }
            },
            cancellationToken);
    }
}
