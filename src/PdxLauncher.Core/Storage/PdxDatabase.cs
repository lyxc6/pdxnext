namespace PdxLauncher.Core.Storage;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 本启动器自己的 SQLite 库（<c>data\pdxnext.sqlite</c>），是这个软件唯一的事实源。
/// <para>
/// 官方启动器的 <c>launcher-v2.sqlite</c> 只提供「它认识什么」；用户没装官方启动器时整库缺席，
/// 那时全部数据只能来自这里，所以播放集、Mod 库、设置、登录态、各缓存索引都落在这一个文件里。
/// </para>
/// <para>
/// 约定：每次操作现开现关（SQLite 打开成本极低，省掉长连接跨线程的麻烦）；写操作走
/// <see cref="Write{T}"/>：进程内按库路径串行 + <c>BEGIN IMMEDIATE</c> + 失败整事务回滚。
/// 建表与迁移幂等，只有 schema 版本要升时才复制一份 <c>pdxnext.sqlite.bak</c>。
/// </para>
/// </summary>
public sealed class PdxDatabase
{
    /// <summary>库文件名（放在 <see cref="PdxDataRoot.Directory"/> 下）。</summary>
    public const string FileName = "pdxnext.sqlite";

    /// <summary>当前 schema 版本；加表/加列时递增，并在 <see cref="Migrate"/> 里补一段迁移。</summary>
    public const int SchemaVersion = 2;

    /// <summary>schema 版本号所在的 meta 键。</summary>
    public const string SchemaVersionKey = "schema_version";

    private static readonly object GateLock = new();
    private static readonly Dictionary<string, SemaphoreSlim> WriteGates = new(StringComparer.OrdinalIgnoreCase);

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate;

    /// <summary>创建一个指向 <paramref name="path"/> 的库对象；文件本身在首次访问时才创建。</summary>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="logger">日志。</param>
    /// <param name="path">库文件路径；为空时用 <see cref="GetDefaultPath"/>。</param>
    public PdxDatabase(IFileSystem fileSystem, IAppLogger logger, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _logger = logger;
        Path = string.IsNullOrWhiteSpace(path) ? GetDefaultPath() : fileSystem.GetFullPath(path);
        _gate = GetGate(Path);
    }

    /// <summary>库文件绝对路径。</summary>
    public string Path { get; }

    /// <summary><c>&lt;data&gt;\pdxnext.sqlite</c>。</summary>
    public static string GetDefaultPath() => System.IO.Path.Combine(PdxDataRoot.Directory, FileName);

    /// <summary>迁移前的备份路径（<c>pdxnext.sqlite.bak</c>）。</summary>
    public static string GetBackupPath(string databasePath) => databasePath + ".bak";

    /// <summary>损坏库的留档路径（<c>pdxnext.sqlite.corrupt-&lt;时间戳&gt;</c>）。</summary>
    public static string GetCorruptPath(string databasePath, DateTimeOffset stamp) =>
        databasePath + ".corrupt-" + stamp.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 打开（必要时创建）库并做一次建表/迁移。
    /// <para>库文件损坏时改名留档、重建空库并记 Warn：宁可让用户丢缓存，也不能让启动器起不来。</para>
    /// </summary>
    public SqliteConnection Open()
    {
        _fileSystem.EnsureDirectory(System.IO.Path.GetDirectoryName(Path) ?? PdxDataRoot.Directory);
        var connection = TryOpen();
        if (connection is null)
        {
            RecoverCorrupt();
            connection = TryOpen() ?? throw new InvalidOperationException("无法创建自有库：" + Path);
        }

        try
        {
            ApplyPragmas(connection);
            EnsureSchema(connection);
        }
        catch (SqliteException exception)
        {
            // 建表都失败说明文件已损坏（不是缺表）：留档重建，再试一次。
            connection.Dispose();
            _logger.Warn("自有库建表失败，按损坏处理：" + exception.Message);
            RecoverCorrupt();
            var rebuilt = TryOpen();
            if (rebuilt is null)
            {
                throw;
            }

            connection = rebuilt;
            ApplyPragmas(connection);
            EnsureSchema(connection);
        }

        return connection;
    }

    /// <summary>
    /// 在写锁内执行 <paramref name="body"/>：进程内串行 + <c>BEGIN IMMEDIATE</c>；抛异常则整事务回滚再抛。
    /// </summary>
    public T Write<T>(Func<SqliteConnection, T> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _gate.Wait();
        try
        {
            using var connection = Open();
            connection.BeginImmediate();
            try
            {
                var result = body(connection);
                connection.Commit();
                return result;
            }
            catch
            {
                connection.Rollback();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>写操作的 <c>void</c> 版本。</summary>
    public void Write(Action<SqliteConnection> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Write<bool>(connection =>
        {
            body(connection);
            return true;
        });
    }

    /// <summary>读 <c>meta</c> 表；不存在返回 <c>null</c>，读失败只 Warn。</summary>
    public string? ReadMeta(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        try
        {
            using var connection = Open();
            return connection.ExecuteScalar("SELECT value FROM meta WHERE key = ?", key) as string;
        }
        catch (SqliteException exception)
        {
            _logger.Warn("读 meta 失败（" + key + "）：" + exception.Message);
            return null;
        }
    }

    /// <summary>写 <c>meta</c> 表（覆盖）；失败只 Warn。</summary>
    public void WriteMeta(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            Write(connection => connection.Execute(
                "INSERT INTO meta (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                key,
                value));
        }
        catch (SqliteException exception)
        {
            _logger.Warn("写 meta 失败（" + key + "）：" + exception.Message);
        }
    }

    /// <summary>meta 键是否存在。</summary>
    public bool HasMeta(string key) => ReadMeta(key) is not null;

    /// <summary>删一条 meta 键（没有这条键也算成功）。</summary>
    public bool DeleteMeta(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        try
        {
            Write(connection => connection.Execute("DELETE FROM meta WHERE key = ?", key));
            return true;
        }
        catch (SqliteException exception)
        {
            _logger.Warn("自有库删 meta 失败（" + key + "）：" + exception.Message);
            return false;
        }
    }

    private SqliteConnection? TryOpen()
    {
        try
        {
            return SqliteConnection.OpenOrCreate(Path);
        }
        catch (SqliteException exception)
        {
            _logger.Warn("打开自有库失败：" + exception.Message);
            return null;
        }
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        // DELETE 日志模式：不留 -wal/-shm 两个尾巴文件，用户备份/迁移只要拷一个文件。
        connection.ExecuteScalar("PRAGMA journal_mode=DELETE");
        // 不开外键：官方库的窄表（老版本）没有外键，两边行为要一致，级联由代码显式做。
        connection.Execute("PRAGMA foreign_keys=OFF");
        // 忙等 5 秒（SqliteConnection 打开时已设，这里兜底：连接被外部复用的情况）。
        connection.Execute("PRAGMA busy_timeout=5000");
    }

    private void EnsureSchema(SqliteConnection connection)
    {
        var current = ReadSchemaVersion(connection);
        if (current < SchemaVersion)
        {
            if (current > 0)
            {
                // 只在真的要改结构时备份：旧版本数据不能因为一次升级就没救。
                BackupBeforeMigration();
            }

            Migrate(connection, current);
            connection.Execute(
                "INSERT INTO meta (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                SchemaVersionKey,
                SchemaVersion.ToString(CultureInfo.InvariantCulture));
            _logger.Info("自有库 schema 就绪：v" + SchemaVersion.ToString(CultureInfo.InvariantCulture) + "（" + Path + "）");
        }

        // 每次打开都查一次闸门（只读一行 meta）：上次迁移中途失败时下次启动会接着导。
        EnsureWorkshopImport(connection);
    }

    /// <summary>
    /// v2 的一次性数据搬迁：把 <c>documents</c> 里两段工坊缓存 JSON 倒进 <c>workshop_items</c> /
    /// <c>workshop_pages</c>，成功后删旧行并写 meta 闸门。
    /// <para>
    /// 幂等靠 meta 键 <c>workshop-import-v2</c>；任一段解析失败就整体保留旧行，下次启动重试，
    /// 绝不因为缓存搬家把用户数据删掉。
    /// </para>
    /// </summary>
    private void EnsureWorkshopImport(SqliteConnection connection)
    {
        const string importKey = "workshop-import-v2";
        var marker = connection.ExecuteScalar("SELECT value FROM meta WHERE key = ?", importKey) as string;
        if (string.Equals(marker, "done", StringComparison.Ordinal))
        {
            return;
        }

        var infoJson = ReadDocument(connection, JsonDocumentKeys.WorkshopInfo);
        var pagesJson = ReadDocument(connection, JsonDocumentKeys.WorkshopPages);
        if (infoJson is null && pagesJson is null)
        {
            WriteMeta(connection, importKey, "done");
            return;
        }

        if (infoJson is not null)
        {
            var items = new SqliteWorkshopItemStore(this, _logger);
            if (!items.Import(connection, infoJson, WorkshopInfoCache.MaxEntries))
            {
                _logger.Warn("工坊条目缓存迁移未完成，保留旧数据下次重试。");
                return;
            }
        }

        if (pagesJson is not null)
        {
            var pages = new SqliteWorkshopPageStore(this, _logger);
            if (!pages.Import(connection, pagesJson, SteamItemPageCache.MaxEntries))
            {
                _logger.Warn("工坊分页缓存迁移未完成，保留旧数据下次重试。");
                return;
            }
        }

        if (infoJson is not null)
        {
            DeleteDocument(connection, JsonDocumentKeys.WorkshopInfo);
        }

        if (pagesJson is not null)
        {
            DeleteDocument(connection, JsonDocumentKeys.WorkshopPages);
        }

        WriteMeta(connection, importKey, "done");
        _logger.Info("工坊缓存已迁移到真表（workshop_items / workshop_pages）。");
    }

    private static string? ReadDocument(SqliteConnection connection, string key) =>
        connection.ExecuteScalar("SELECT json FROM documents WHERE key = ?", key) as string;

    private static void DeleteDocument(SqliteConnection connection, string key) =>
        connection.Execute("DELETE FROM documents WHERE key = ?", key);

    private static void WriteMeta(SqliteConnection connection, string key, string value) =>
        connection.Execute(
            "INSERT INTO meta (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            key,
            value);

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        // 先建 meta，否则空库上读版本会报「no such table」。
        connection.Execute("CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
        var value = connection.ExecuteScalar("SELECT value FROM meta WHERE key = ?", SchemaVersionKey) as string;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private void BackupBeforeMigration()
    {
        var backupPath = GetBackupPath(Path);
        try
        {
            _fileSystem.Copy(Path, backupPath, overwrite: true);
            _logger.Info("迁移前已备份自有库：" + backupPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 备份失败不阻断：迁移本身是幂等的 CREATE/ALTER，宁可继续也别让应用起不来。
            _logger.Warn("备份自有库失败（继续迁移）：" + exception.Message);
        }
    }

    private void Migrate(SqliteConnection connection, int fromVersion)
    {
        // v1：初始结构。以后加表/加列时在这里按 fromVersion 逐段升级。
        _ = fromVersion;
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS settings (" +
            "id INTEGER PRIMARY KEY CHECK (id = 1), json TEXT NOT NULL, updatedAt INTEGER NOT NULL)");
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS session (" +
            "id INTEGER PRIMARY KEY CHECK (id = 1), json TEXT NOT NULL, updatedAt INTEGER NOT NULL)");
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS documents (" +
            "key TEXT PRIMARY KEY, json TEXT NOT NULL, updatedAt INTEGER NOT NULL)");
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS mods (" +
            "gameId TEXT NOT NULL, id TEXT NOT NULL, displayName TEXT NOT NULL DEFAULT '', " +
            "dirPath TEXT, archivePath TEXT, gameRegistryId TEXT, steamId TEXT, " +
            "source TEXT NOT NULL DEFAULT 'local', requiredVersion TEXT, " +
            "status TEXT NOT NULL DEFAULT 'ready_to_play', size INTEGER, subscribedDate INTEGER, " +
            "thumbnailUrl TEXT, thumbnailPath TEXT, tags TEXT, updatedAt INTEGER NOT NULL DEFAULT 0, " +
            "PRIMARY KEY (gameId, id))");
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS playsets (" +
            "gameId TEXT NOT NULL, id TEXT NOT NULL, name TEXT NOT NULL, " +
            "isActive INTEGER NOT NULL DEFAULT 0, loadOrder INTEGER NOT NULL DEFAULT 0, " +
            "createdOn INTEGER, updatedOn INTEGER, PRIMARY KEY (gameId, id))");
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS playsets_mods (" +
            "gameId TEXT NOT NULL, playsetId TEXT NOT NULL, modId TEXT NOT NULL, " +
            "enabled INTEGER NOT NULL DEFAULT 1, position INTEGER NOT NULL DEFAULT 0)");
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS playsets_dlcs (" +
            "gameId TEXT NOT NULL, playsetId TEXT NOT NULL, dlcId TEXT NOT NULL, " +
            "enabled INTEGER NOT NULL DEFAULT 1)");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_mods_game ON mods (gameId)");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_playsets_mods ON playsets_mods (gameId, playsetId, position)");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_playsets_dlcs ON playsets_dlcs (gameId, playsetId)");

        // v2：工坊缓存从「一整段 JSON」改成一览表 + 分页表。
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS workshop_items (" +
            "source TEXT NOT NULL, id TEXT NOT NULL, game_id TEXT, name TEXT, author TEXT, " +
            "rating REAL, ratings_total INTEGER, subscriptions INTEGER, updated_utc INTEGER, " +
            "size_bytes INTEGER, cover_url TEXT, cover_file TEXT, cover_fetched_utc INTEGER, " +
            "detail_text TEXT, detail_screenshots TEXT, detail_fetched_utc INTEGER, " +
            "fetched_utc INTEGER NOT NULL, creator_steam_id TEXT, tags TEXT, " +
            "views INTEGER, lifetime_subscriptions INTEGER, PRIMARY KEY (source, id))");
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS workshop_pages (" +
            "published_file_id TEXT PRIMARY KEY, fetched_utc INTEGER NOT NULL, payload TEXT NOT NULL)");
        connection.Execute(
            "CREATE INDEX IF NOT EXISTS ix_workshop_items_game ON workshop_items (game_id, fetched_utc)");
        connection.Execute(
            "CREATE INDEX IF NOT EXISTS ix_workshop_items_fetched ON workshop_items (fetched_utc)");
    }

    private void RecoverCorrupt()
    {
        if (!_fileSystem.FileExists(Path))
        {
            return;
        }

        var corruptPath = GetCorruptPath(Path, DateTimeOffset.UtcNow);
        try
        {
            _fileSystem.Move(Path, corruptPath, overwrite: true);
            _logger.Warn("自有库疑似损坏，已改名留档：" + corruptPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 连改名都失败时只能就地覆盖：先删掉，让 OpenOrCreate 重建。
            _logger.Error("自有库损坏且无法改名，就地删除重建：" + Path, exception);
            try
            {
                _fileSystem.Delete(Path);
            }
            catch (Exception deleteException) when (deleteException is IOException or UnauthorizedAccessException)
            {
                _logger.Error("删除损坏的自有库也失败：" + Path, deleteException);
            }
        }
    }

    private static SemaphoreSlim GetGate(string path)
    {
        lock (GateLock)
        {
            if (!WriteGates.TryGetValue(path, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                WriteGates[path] = gate;
            }

            return gate;
        }
    }
}
