namespace PdxLauncher.Core.Playsets;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Storage;

/// <summary>
/// 官方 <c>launcher-v2.sqlite</c> 与自有库 <c>pdxnext.sqlite</c> 之间的双向搬运。
/// </summary>
/// <remarks>
/// 目标是「两个启动器都能用」：
/// <list type="bullet">
/// <item>官方库在：读写都以官方库为准（游戏读的是它），顺带把内容抄一份进自有库，官方库哪天没了也不丢。</item>
/// <item>官方库不在：只用自有库；等官方库重新出现（用户装了官方启动器），把自有库的内容投射回去。</item>
/// </list>
/// 两条记号记住进度：<c>imported:&lt;gameId&gt;</c>（上次抄进来的官方库 mtime）与
/// <c>pending-projection:&lt;gameId&gt;</c>（自有库有官方库还没有的改动）。
/// <para>只处理 sqlite 格式的游戏；CS2/EU5 走各自文件，不经过这里。</para>
/// </remarks>
public sealed class LauncherDbProjector
{
    /// <summary>「官方库已导入自有库」的 meta 前缀，值为导入时的官方库 mtime（毫秒）。</summary>
    public const string ImportedPrefix = "imported:";

    /// <summary>「自有库有待投射改动」的 meta 前缀。</summary>
    public const string PendingProjectionPrefix = "pending-projection:";

    /// <summary>「自有库已投射回官方库」的 meta 前缀。</summary>
    public const string ProjectedPrefix = "projected:";

    private readonly IFileSystem _fileSystem;
    private readonly PlaySetRepository _official;
    private readonly OwnDatabasePlaySetRepository _own;
    private readonly PdxDatabase _database;
    private readonly IAppLogger _logger;

    /// <summary>创建搬运器。</summary>
    public LauncherDbProjector(
        IFileSystem fileSystem,
        PlaySetRepository official,
        OwnDatabasePlaySetRepository own,
        PdxDatabase database,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(official);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _official = official;
        _own = own;
        _database = database;
        _logger = logger;
    }

    /// <summary>官方库文件是否在（这款游戏用 sqlite 存储时才有意义）。</summary>
    public static bool IsLauncherDatabasePresent(IFileSystem fileSystem, GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(game);
        return fileSystem.FileExists(PlaySetRepository.GetDatabasePath(game));
    }

    /// <summary>自有库里有官方库还没有的改动（官方库缺席期间写过东西）。</summary>
    public bool HasPendingProjection(GameInstallation game) =>
        _database.HasMeta(PendingProjectionPrefix + game.GameId);

    /// <summary>
    /// 读之前对齐一次：自有库有未投射改动就先推回官方库；官方库比上次导入新就先抄进自有库。
    /// 失败不阻断读取——读侧仍能拿到官方库里的数据。
    /// </summary>
    public async Task PrepareForReadAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (!IsLauncherDatabasePresent(_fileSystem, game))
        {
            return;
        }

        if (HasPendingProjection(game))
        {
            var projected = await ProjectAsync(game, cancellationToken).ConfigureAwait(false);
            if (projected.IsSuccess)
            {
                return;
            }

            _logger.Warn("自有库投射官方库失败，先按官方库当前内容读取（" + game.GameId + "）：" + projected.ErrorMessage);
        }

        var marker = _database.ReadMeta(ImportedPrefix + game.GameId);
        if (marker is null || ChangedSince(game, marker))
        {
            var imported = await ImportAsync(game, cancellationToken).ConfigureAwait(false);
            if (!imported.IsSuccess)
            {
                _logger.Warn("官方库导入自有库失败（" + game.GameId + "）：" + imported.ErrorMessage);
            }
        }
    }

    /// <summary>
    /// 官方库写成功之后调：把官方库整份抄进自有库（含新建/改名/删除/激活这些结构变化）。
    /// 抄失败只记 Warn——官方库已经写对了，游戏能读到，下次读之前会再抄一次。
    /// </summary>
    public async Task MirrorFromOfficialAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (!IsLauncherDatabasePresent(_fileSystem, game))
        {
            return;
        }

        var imported = await ImportAsync(game, cancellationToken).ConfigureAwait(false);
        if (!imported.IsSuccess)
        {
            _logger.Warn("官方库镜像进自有库失败（" + game.GameId + "）：" + imported.ErrorMessage);
        }
    }

    /// <summary>自有库写成功之后调：官方库不在时只记「待投射」，在时立刻投射。</summary>
    public async Task SyncAfterOwnWriteAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (IsLauncherDatabasePresent(_fileSystem, game))
        {
            var projected = await ProjectAsync(game, cancellationToken).ConfigureAwait(false);
            if (!projected.IsSuccess)
            {
                _logger.Warn("自有库投射官方库失败（" + game.GameId + "）：" + projected.ErrorMessage);
                MarkPending(game);
            }

            return;
        }

        // 官方库缺席：等它哪天出现再投射。绝不主动创建 launcher-v2.sqlite（用户明确要求）。
        MarkPending(game);
    }

    /// <summary>官方库 → 自有库（整份替换这款游戏的数据）。</summary>
    public async Task<AppResult<bool>> ImportAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        var databasePath = PlaySetRepository.GetDatabasePath(game);
        if (!_fileSystem.FileExists(databasePath))
        {
            return AppResult<bool>.Fail(ErrorKind.NotFound, "database-not-found:" + databasePath);
        }

        var playSets = await _official.GetPlaySetsAsync(game, cancellationToken).ConfigureAwait(false);
        var library = await _official.GetModLibraryAsync(game, cancellationToken).ConfigureAwait(false);

        var modsByPlaySet = new Dictionary<Guid, IReadOnlyList<PlaySetMod>>();
        var dlcsByPlaySet = new Dictionary<Guid, IReadOnlyList<PlaySetDlc>>();
        foreach (var playSet in playSets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            modsByPlaySet[playSet.Id] = await _official
                .GetPlaySetModsAsync(game, playSet.Id, cancellationToken)
                .ConfigureAwait(false);
            dlcsByPlaySet[playSet.Id] = await _official
                .GetPlaySetDlcsAsync(game, playSet.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            var gameId = game.GameId;
            var updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _database.Write(connection =>
            {
                OwnDatabasePlaySetRepository.DeleteGameRows(connection, gameId);
                OwnModStore.ImportLibraryRows(connection, gameId, library, updatedAt);

                var order = 0;
                foreach (var playSet in playSets)
                {
                    order++;
                    connection.Execute(
                        "INSERT INTO playsets (gameId, id, name, isActive, loadOrder, createdOn, updatedOn) " +
                        "VALUES (?, ?, ?, ?, ?, ?, ?)",
                        gameId,
                        playSet.Id.ToString("D"),
                        playSet.Name,
                        playSet.IsActive ? 1 : 0,
                        order,
                        updatedAt,
                        updatedAt);

                    var position = 0;
                    foreach (var mod in modsByPlaySet[playSet.Id])
                    {
                        connection.Execute(
                            "INSERT INTO playsets_mods (gameId, playsetId, modId, enabled, position) VALUES (?, ?, ?, ?, ?)",
                            gameId,
                            playSet.Id.ToString("D"),
                            mod.ModId.ToString("D"),
                            mod.Enabled ? 1 : 0,
                            position);
                        position++;
                    }

                    foreach (var dlc in dlcsByPlaySet[playSet.Id])
                    {
                        connection.Execute(
                            "INSERT INTO playsets_dlcs (gameId, playsetId, dlcId, enabled) VALUES (?, ?, ?, ?)",
                            gameId,
                            playSet.Id.ToString("D"),
                            dlc.DlcId,
                            dlc.Enabled ? 1 : 0);
                    }
                }
            });

            _database.WriteMeta(ImportedPrefix + gameId, ReadDatabaseStampMillis(databasePath));
            _logger.Info("官方库已导入自有库：播放集 " + playSets.Count.ToString(CultureInfo.InvariantCulture)
                + " 个、mod " + library.Count.ToString(CultureInfo.InvariantCulture) + " 行（" + gameId + "）");
            return AppResult<bool>.Ok(true);
        }
        catch (SqliteException exception)
        {
            _logger.Warn("官方库导入自有库失败（" + game.GameId + "）：" + exception.Message);
            return AppResult<bool>.Fail(ErrorKind.Io, "sqlite:" + exception.Message);
        }
    }

    /// <summary>
    /// 自有库 → 官方库：播放集名单、每个播放集的 mod / DLC、以及 mod 行的路径与来源。
    /// 官方库不存在时直接返回 <see cref="ErrorKind.NotFound"/>，绝不创建。
    /// </summary>
    public async Task<AppResult<bool>> ProjectAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        var databasePath = PlaySetRepository.GetDatabasePath(game);
        if (!_fileSystem.FileExists(databasePath))
        {
            return AppResult<bool>.Fail(ErrorKind.NotFound, "database-not-found:" + databasePath);
        }

        var playSets = await _own.GetPlaySetsAsync(game, cancellationToken).ConfigureAwait(false);
        if (playSets.Count == 0)
        {
            // 自有库这款游戏还没数据：没什么可投射的，别去动官方库。
            ClearPending(game);
            return AppResult<bool>.Ok(true);
        }

        var upserted = await _official.UpsertPlaySetsAsync(game, playSets, cancellationToken).ConfigureAwait(false);
        if (!upserted.IsSuccess)
        {
            return AppResult<bool>.Fail(upserted.Kind, upserted.ErrorMessage ?? "playsets-upsert-failed");
        }

        foreach (var playSet in playSets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mods = await _own.GetPlaySetModsAsync(game, playSet.Id, cancellationToken).ConfigureAwait(false);
            var saveMods = await _official.SavePlaySetModsAsync(game, playSet.Id, mods, cancellationToken).ConfigureAwait(false);
            if (!saveMods.IsSuccess)
            {
                return AppResult<bool>.Fail(saveMods.Kind, saveMods.ErrorMessage ?? "playSet-mods-save-failed");
            }

            var dlcs = await _own.GetPlaySetDlcsAsync(game, playSet.Id, cancellationToken).ConfigureAwait(false);
            if (dlcs.Count > 0)
            {
                var saveDlcs = await _official.SavePlaySetDlcsAsync(game, playSet.Id, dlcs, cancellationToken).ConfigureAwait(false);
                if (!saveDlcs.IsSuccess)
                {
                    _logger.Warn("投射 DLC 行失败（" + playSet.Name + "）：" + saveDlcs.ErrorMessage);
                }
            }
        }

        var library = await _own.GetModLibraryAsync(game, cancellationToken).ConfigureAwait(false);
        var modRows = new List<PlaySetMod>(library.Count);
        foreach (var entry in library)
        {
            modRows.Add(new PlaySetMod(
                entry.ModId,
                entry.Name,
                entry.DirPath ?? string.Empty,
                entry.ArchivePath,
                entry.GameRegistryId,
                entry.ThumbnailPath,
                entry.RequiredVersion,
                entry.Status,
                true,
                0,
                entry.SteamId,
                entry.Source));
        }

        var updated = await _official.UpdateModRowsAsync(game, modRows, cancellationToken).ConfigureAwait(false);
        if (!updated.IsSuccess)
        {
            _logger.Warn("投射 mod 行失败（" + game.GameId + "）：" + updated.ErrorMessage);
        }

        _database.WriteMeta(ProjectedPrefix + game.GameId, ReadDatabaseStampMillis(databasePath));
        ClearPending(game);
        _logger.Info("自有库已投射官方库：播放集 " + playSets.Count.ToString(CultureInfo.InvariantCulture)
            + " 个（" + game.GameId + "）");
        return AppResult<bool>.Ok(true);
    }

    private void MarkPending(GameInstallation game)
    {
        if (!_database.HasMeta(PendingProjectionPrefix + game.GameId))
        {
            _database.WriteMeta(PendingProjectionPrefix + game.GameId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        }
    }

    private void ClearPending(GameInstallation game)
    {
        if (HasPendingProjection(game))
        {
            _database.DeleteMeta(PendingProjectionPrefix + game.GameId);
        }
    }

    private bool ChangedSince(GameInstallation game, string marker)
    {
        if (!long.TryParse(marker, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lastSeen))
        {
            return true;
        }

        var databasePath = PlaySetRepository.GetDatabasePath(game);
        if (long.TryParse(ReadDatabaseStampMillis(databasePath), NumberStyles.Integer, CultureInfo.InvariantCulture, out var current))
        {
            return current > lastSeen;
        }

        return true;
    }

    /// <summary>官方库的写入时间（毫秒）。<see cref="IFileSystem"/> 没有 mtime 接口，这里直接问文件。</summary>
    private static string ReadDatabaseStampMillis(string databasePath)
    {
        try
        {
            var info = new FileInfo(databasePath);
            return info.Exists ? new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) : "0";
        }
        catch (IOException)
        {
            return "0";
        }
        catch (UnauthorizedAccessException)
        {
            return "0";
        }
    }
}
