namespace PdxLauncher.Core.Playsets;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Storage;

/// <summary>
/// 自有库里的 mod 表（<c>mods</c>）写入。官方库那边由 <see cref="PlaySetRepository"/> 负责，
/// 这里只写 <c>pdxnext.sqlite</c>：自有库带 <c>gameId</c>，官方库一条库一款游戏。
/// </summary>
public sealed class OwnModStore
{
    private readonly PdxDatabase _database;
    private readonly IClock _clock;
    private readonly IAppLogger _logger;

    /// <summary>创建写入器。</summary>
    public OwnModStore(PdxDatabase database, IClock clock, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _database = database;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// 按 id upsert 一批 mod（播放集保存时调）。已有的行只更新「我们有值」的列：
    /// 新值缺路径时保留旧路径，免得一次不完整的写入把可用信息抹掉。
    /// </summary>
    public AppResult<bool> UpsertMods(string gameId, IEnumerable<PlaySetMod> mods)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(mods);
        var rows = mods as IReadOnlyList<PlaySetMod> ?? mods.ToList();
        if (rows.Count == 0)
        {
            return AppResult<bool>.Ok(true);
        }

        try
        {
            _database.Write(connection => UpsertRows(connection, gameId, rows, _clock.UtcNow.ToUnixTimeMilliseconds()));
            return AppResult<bool>.Ok(true);
        }
        catch (SqliteException exception)
        {
            _logger.Warn("自有库写入 mod 行失败（" + gameId + "）：" + exception.Message);
            return AppResult<bool>.Fail(ErrorKind.Io, "sqlite:" + exception.Message);
        }
    }

    /// <summary>定向改一行的路径（mod 路径修复用）；库里没有这一行时返回失败。</summary>
    public AppResult<bool> UpdateModPaths(string gameId, Guid modId, string? dirPath, string? archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        if (modId == Guid.Empty)
        {
            return AppResult<bool>.Fail(ErrorKind.Invalid, "mod-id-empty");
        }

        try
        {
            var changed = _database.Write(connection => connection.Execute(
                "UPDATE mods SET dirPath = ?, archivePath = ?, updatedAt = ? WHERE gameId = ? AND id = ?",
                NullIfBlank(dirPath),
                NullIfBlank(archivePath),
                _clock.UtcNow.ToUnixTimeMilliseconds(),
                gameId,
                modId.ToString("D")));

            return changed > 0
                ? AppResult<bool>.Ok(true)
                : AppResult<bool>.Fail(ErrorKind.NotFound, "mod-not-found:" + modId.ToString("D"));
        }
        catch (SqliteException exception)
        {
            _logger.Warn("自有库改 mod 路径失败（" + modId.ToString("D") + "）：" + exception.Message);
            return AppResult<bool>.Fail(ErrorKind.Io, "sqlite:" + exception.Message);
        }
    }

    /// <summary>
    /// 把官方库的 Mod 库整体导入自有库：带上官方才知道的 <c>size</c> / <c>subscribedDate</c> /
    /// <c>thumbnailUrl</c>，这样官方库里没有这款游戏时也能显示大小与订阅时间。
    /// </summary>
    public AppResult<int> ImportFromLauncherLibrary(string gameId, IEnumerable<ModLibraryEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(entries);
        var rows = entries as IReadOnlyList<ModLibraryEntry> ?? entries.ToList();
        if (rows.Count == 0)
        {
            return AppResult<int>.Ok(0);
        }

        try
        {
            var imported = _database.Write(connection =>
                ImportLibraryRows(connection, gameId, rows, _clock.UtcNow.ToUnixTimeMilliseconds()));

            if (imported > 0)
            {
                _logger.Info("自有库导入官方 Mod 库：" + imported.ToString(CultureInfo.InvariantCulture) + " 行（" + gameId + "）");
            }

            return AppResult<int>.Ok(imported);
        }
        catch (SqliteException exception)
        {
            _logger.Warn("自有库导入官方 Mod 库失败（" + gameId + "）：" + exception.Message);
            return AppResult<int>.Fail(ErrorKind.Io, "sqlite:" + exception.Message);
        }
    }

    /// <summary>在已打开的事务里 upsert mod 行（<see cref="OwnDatabasePlaySetRepository"/> 复用以保持单事务）。</summary>
    internal static void UpsertRows(
        SqliteConnection connection,
        string gameId,
        IReadOnlyList<PlaySetMod> mods,
        long updatedAt)
    {
        foreach (var mod in mods)
        {
            if (mod is null || mod.ModId == Guid.Empty)
            {
                continue;
            }

            connection.Execute(
                "INSERT INTO mods (gameId, id, displayName, dirPath, archivePath, gameRegistryId, steamId, " +
                "source, requiredVersion, status, updatedAt) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?) " +
                "ON CONFLICT(gameId, id) DO UPDATE SET " +
                "displayName = excluded.displayName, " +
                "dirPath = COALESCE(excluded.dirPath, mods.dirPath), " +
                "archivePath = COALESCE(excluded.archivePath, mods.archivePath), " +
                "gameRegistryId = COALESCE(excluded.gameRegistryId, mods.gameRegistryId), " +
                "steamId = COALESCE(excluded.steamId, mods.steamId), " +
                "source = excluded.source, " +
                "requiredVersion = COALESCE(excluded.requiredVersion, mods.requiredVersion), " +
                "status = excluded.status, " +
                "updatedAt = excluded.updatedAt",
                gameId,
                mod.ModId.ToString("D"),
                mod.Name ?? string.Empty,
                NullIfBlank(mod.DirPath),
                NullIfBlank(mod.ArchivePath),
                NullIfBlank(mod.GameRegistryId),
                WorkshopIdOrNull(mod),
                SourceFor(mod),
                NullIfBlank(mod.RequiredVersion),
                string.IsNullOrWhiteSpace(mod.Status) ? "ready_to_play" : mod.Status,
                updatedAt);
        }
    }

    internal static int ImportLibraryRows(
        SqliteConnection connection,
        string gameId,
        IReadOnlyList<ModLibraryEntry> entries,
        long updatedAt)
    {
        var count = 0;
        foreach (var entry in entries)
        {
            if (entry is null || entry.ModId == Guid.Empty)
            {
                continue;
            }

            connection.Execute(
                "INSERT INTO mods (gameId, id, displayName, dirPath, archivePath, gameRegistryId, steamId, " +
                "source, requiredVersion, status, size, subscribedDate, thumbnailUrl, thumbnailPath, updatedAt) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?) " +
                "ON CONFLICT(gameId, id) DO UPDATE SET " +
                "displayName = excluded.displayName, " +
                "dirPath = COALESCE(excluded.dirPath, mods.dirPath), " +
                "archivePath = COALESCE(excluded.archivePath, mods.archivePath), " +
                "gameRegistryId = COALESCE(excluded.gameRegistryId, mods.gameRegistryId), " +
                "steamId = COALESCE(excluded.steamId, mods.steamId), " +
                "source = excluded.source, " +
                "requiredVersion = COALESCE(excluded.requiredVersion, mods.requiredVersion), " +
                "status = excluded.status, " +
                "size = COALESCE(excluded.size, mods.size), " +
                "subscribedDate = COALESCE(excluded.subscribedDate, mods.subscribedDate), " +
                "thumbnailUrl = COALESCE(excluded.thumbnailUrl, mods.thumbnailUrl), " +
                "thumbnailPath = COALESCE(excluded.thumbnailPath, mods.thumbnailPath), " +
                "updatedAt = excluded.updatedAt",
                gameId,
                entry.ModId.ToString("D"),
                entry.Name ?? string.Empty,
                NullIfBlank(entry.DirPath),
                NullIfBlank(entry.ArchivePath),
                NullIfBlank(entry.GameRegistryId),
                SteamWorkshop.GetWorkshopId(entry.SteamId, entry.GameRegistryId),
                string.IsNullOrWhiteSpace(entry.Source) ? "local" : entry.Source,
                NullIfBlank(entry.RequiredVersion),
                string.IsNullOrWhiteSpace(entry.Status) ? "ready_to_play" : entry.Status,
                entry.SizeBytes,
                entry.SubscribedOn?.ToUnixTimeMilliseconds(),
                NullIfBlank(entry.ThumbnailUrl),
                NullIfBlank(entry.ThumbnailPath),
                updatedAt);
            count++;
        }

        return count;
    }

    /// <summary>取创意工坊 id；拿不到返回 null（不拿目录名硬编造，见 <see cref="ModCatalog"/> 的注释）。</summary>
    internal static string? WorkshopIdOrNull(PlaySetMod mod) =>
        SteamWorkshop.GetWorkshopId(mod.SteamId, mod.GameRegistryId);

    /// <summary>
    /// 来源：工坊 id 拿得到就是订阅（steam）；只有 PDX Mods id 时记 pdx_mods；
    /// 两者都没有才是本地（local）。
    /// <para>
    /// 之前只判工坊 id，PDX 渠道 mod 会以 <c>local</c> 落库，导出官方分享 JSON 时被当成
    /// 「本地 mod」整条排除。PDX id 在 gameRegistryId（<c>pdx:&lt;id&gt;</c>）里还在，所以这里补上。
    /// </para>
    /// </summary>
    internal static string SourceFor(PlaySetMod mod)
    {
        if (WorkshopIdOrNull(mod) is not null)
        {
            return ModOrigins.SteamSource;
        }

        return PdxModIds.FromRegistryId(mod.GameRegistryId) is not null
            || PdxModIds.Normalize(mod.PdxId) is not null
            ? ModOrigins.PdxModsSource
            : ModOrigins.LocalSource;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
