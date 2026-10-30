namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 自检：保存播放集时给「官方 mod 库里还没有行」的 mod 补登记。
/// <para>
/// 真机场景：刚订阅的工坊 mod（Steam 已下载、官方启动器还没扫描到）在 mods 表里没有行。
/// 不补登记就写 playsets_mods，会留下悬空外键，游戏里读不出这个 mod。
/// 补登记必须：同一个事务、同一次 .pdxnext.bak 备份、幂等、只补磁盘上确实有内容的行、
/// 兼容没有全部列的窄表（老库与自检夹具）。
/// </para>
/// </summary>
public static class ModRegistrationChecks
{
    private static readonly Guid PlaySetId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        var logger = new NullLogger();

        CheckRegistersWorkshopMod(fs, logger);
        CheckIdempotent(fs, logger);
        CheckSkipsModWithoutContent(fs, logger);
        CheckNarrowModsTable(fs, logger);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 用例
    // ------------------------------------------------------------------

    /// <summary>磁盘上有内容、库里没有行 → 保存时补登记，字段按官方约定写。</summary>
    private static void CheckRegistersWorkshopMod(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[ModRegistrationChecks] 补登记未登记的工坊 mod");
        var root = PlaysetChecks.CreateTempRoot(fs, "modregistration-new");
        try
        {
            var game = CreateGame(fs, root, includeSteamId: true);
            var modDirectory = Path.Combine(root, "library", "steamapps", "workshop", "content", "529340", "3742679779");
            fs.EnsureDirectory(modDirectory);

            var mod = new PlaySetMod(
                Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                "Daoyu Cheat",
                modDirectory,
                ArchivePath: null,
                GameRegistryId: string.Empty,
                ThumbnailPath: null,
                RequiredVersion: "1.13.*",
                Status: "ready_to_play",
                Enabled: true,
                Position: 0,
                SteamId: "3742679779");

            var repository = new PlaySetRepository(fs, logger, SystemClock.Instance);
            var saved = repository
                .SavePlaySetModsAsync(game, PlaySetId, new[] { mod }, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ChecksContext.Check("保存播放集 mod 行成功（含补登记）", saved.IsSuccess, saved.ErrorMessage ?? "<null>");

            var databasePath = PlaySetRepository.GetDatabasePath(game);
            var rows = ReadMods(fs, databasePath);
            ChecksContext.Equal("mods 表多了 1 行", 1, rows.Count);
            if (rows.Count == 1)
            {
                var row = rows[0];
                ChecksContext.Equal("补登记写 displayName", "Daoyu Cheat", ReadText(row, "displayName"));
                ChecksContext.Equal("补登记写数字 steamId", "3742679779", ReadText(row, "steamId"));
                ChecksContext.Equal("工坊 mod 的 source 记 steam", "steam", ReadText(row, "source"));
                ChecksContext.Equal("补登记写 dirPath", modDirectory, ReadText(row, "dirPath"));
                ChecksContext.Equal("补登记写 requiredVersion", "1.13.*", ReadText(row, "requiredVersion"));
                ChecksContext.Equal("补登记保持 ready_to_play", "ready_to_play", ReadText(row, "status"));
            }

            ChecksContext.Equal(
                "playsets_mods 也写入了同一行",
                1,
                CountPlaySetMods(fs, databasePath));
            ChecksContext.Check(
                "写入前生成了 .pdxnext.bak 备份",
                fs.FileExists(databasePath + ".pdxnext.bak"));
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>再保存一次不重复插入（幂等）。</summary>
    private static void CheckIdempotent(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[ModRegistrationChecks] 补登记幂等");
        var root = PlaysetChecks.CreateTempRoot(fs, "modregistration-idempotent");
        try
        {
            var game = CreateGame(fs, root, includeSteamId: true);
            var modDirectory = Path.Combine(game.ModDir, "LocalMod");
            fs.EnsureDirectory(modDirectory);

            var mod = new PlaySetMod(
                Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                "Local Mod",
                modDirectory,
                ArchivePath: null,
                GameRegistryId: "mod/Local Mod.mod",
                ThumbnailPath: null,
                RequiredVersion: "1.13.*",
                Status: "ready_to_play",
                Enabled: true,
                Position: 0,
                SteamId: null);

            var repository = new PlaySetRepository(fs, logger, SystemClock.Instance);
            repository.SavePlaySetModsAsync(game, PlaySetId, new[] { mod }, CancellationToken.None).GetAwaiter().GetResult();
            repository.SavePlaySetModsAsync(game, PlaySetId, new[] { mod }, CancellationToken.None).GetAwaiter().GetResult();

            var databasePath = PlaySetRepository.GetDatabasePath(game);
            ChecksContext.Equal("重复保存后 mods 表仍只有 1 行", 1, ReadMods(fs, databasePath).Count);

            var rows = ReadMods(fs, databasePath);
            if (rows.Count == 1)
            {
                ChecksContext.Equal(
                    "没有数字工坊 id 时 source 记 local",
                    "local",
                    ReadText(rows[0], "source"));
                ChecksContext.Check("没有数字工坊 id 时 steamId 为 NULL", ReadNullableText(rows[0], "steamId") is null);
            }
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>磁盘上没有内容的 mod 不登记（避免往官方库里塞脏行）。</summary>
    private static void CheckSkipsModWithoutContent(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[ModRegistrationChecks] 没有内容的 mod 不登记");
        var root = PlaysetChecks.CreateTempRoot(fs, "modregistration-missing");
        try
        {
            var game = CreateGame(fs, root, includeSteamId: true);

            var mod = new PlaySetMod(
                Guid.Parse("11111111-2222-3333-4444-555555555555"),
                "Deleted Mod",
                Path.Combine(game.ModDir, "Deleted Mod"),
                ArchivePath: null,
                GameRegistryId: "mod/Deleted Mod.mod",
                ThumbnailPath: null,
                RequiredVersion: string.Empty,
                Status: "missing",
                Enabled: true,
                Position: 0,
                SteamId: "2216623537");

            var repository = new PlaySetRepository(fs, logger, SystemClock.Instance);
            var saved = repository
                .SavePlaySetModsAsync(game, PlaySetId, new[] { mod }, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ChecksContext.Check("内容不存在的 mod 仍能保存播放集行", saved.IsSuccess, saved.ErrorMessage ?? "<null>");

            var databasePath = PlaySetRepository.GetDatabasePath(game);
            ChecksContext.Equal("内容不存在时 mods 表不新增行", 0, ReadMods(fs, databasePath).Count);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>窄表（没有 steamId 列）不报错，缺的列自动跳过。</summary>
    private static void CheckNarrowModsTable(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[ModRegistrationChecks] 窄 mods 表");
        var root = PlaysetChecks.CreateTempRoot(fs, "modregistration-narrow");
        try
        {
            var game = CreateGame(fs, root, includeSteamId: false);
            var modDirectory = Path.Combine(game.ModDir, "Narrow Mod");
            fs.EnsureDirectory(modDirectory);

            var mod = new PlaySetMod(
                Guid.Parse("99999999-8888-7777-6666-555555555555"),
                "Narrow Mod",
                modDirectory,
                ArchivePath: null,
                GameRegistryId: "mod/Narrow Mod.mod",
                ThumbnailPath: null,
                RequiredVersion: "1.20.*",
                Status: "ready_to_play",
                Enabled: true,
                Position: 0,
                SteamId: "2216623537");

            var repository = new PlaySetRepository(fs, logger, SystemClock.Instance);
            var saved = repository
                .SavePlaySetModsAsync(game, PlaySetId, new[] { mod }, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ChecksContext.Check("窄 mods 表（无 steamId 列）保存不失败", saved.IsSuccess, saved.ErrorMessage ?? "<null>");

            var rows = ReadMods(fs, PlaySetRepository.GetDatabasePath(game));
            ChecksContext.Equal("窄表也补登记了 1 行", 1, rows.Count);
            if (rows.Count == 1)
            {
                ChecksContext.Equal("窄表写了 displayName", "Narrow Mod", ReadText(rows[0], "displayName"));
            }
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 辅助
    // ------------------------------------------------------------------

    private static GameInstallation CreateGame(IFileSystem fs, string root, bool includeSteamId)
    {
        var installRoot = Path.Combine(root, "library", "steamapps", "common", "Victoria 3");
        var gameDataDir = Path.Combine(root, "documents", "Victoria 3");
        var modDir = Path.Combine(gameDataDir, "mod");
        fs.EnsureDirectory(installRoot);
        fs.EnsureDirectory(gameDataDir);
        fs.EnsureDirectory(modDir);

        var game = PlaysetChecks.CreateGame(
            "victoria3",
            installRoot,
            LoadFileFormat.ContentLoadV1_1,
            gameDataDir,
            Path.Combine(installRoot, "game", "dlc"),
            modDir,
            529340);
        SeedLauncherTables(fs, PlaySetRepository.GetDatabasePath(game), includeSteamId);
        return game;
    }

    private static void SeedLauncherTables(IFileSystem fs, string databasePath, bool includeSteamId)
    {
        using var connection = SqliteConnection.OpenOrCreate(databasePath);
        connection.Execute(
            "CREATE TABLE playsets (id char(36) PRIMARY KEY, name varchar(255) NOT NULL, isActive boolean, " +
            "loadOrder integer, createdOn datetime NOT NULL, updatedOn datetime)");
        connection.Execute(
            includeSteamId
                ? "CREATE TABLE mods (id char(36) PRIMARY KEY, displayName varchar(255), thumbnailPath text, " +
                  "dirPath text, archivePath text, gameRegistryId text, steamId text, requiredVersion varchar(255), " +
                  "status text NOT NULL, source text NOT NULL)"
                : "CREATE TABLE mods (id char(36) PRIMARY KEY, displayName varchar(255), thumbnailPath text, " +
                  "dirPath text, archivePath text, gameRegistryId text, requiredVersion varchar(255), " +
                  "status text NOT NULL, source text NOT NULL)");
        connection.Execute(
            "CREATE TABLE playsets_mods (playsetId char(36), modId char(36), enabled boolean DEFAULT '1', position integer)");
        connection.Execute(
            "CREATE TABLE playsets_dlcs (playsetId char(36), dlcId text NOT NULL, enabled boolean NOT NULL DEFAULT '0', " +
            "PRIMARY KEY (playsetId, dlcId))");
        connection.Execute(
            "INSERT INTO playsets (id, name, isActive, createdOn) VALUES (?, ?, ?, ?)",
            PlaySetId.ToString("D"),
            "补登记",
            1,
            1000L);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ReadMods(IFileSystem fs, string databasePath)
    {
        using var connection = SqliteConnection.OpenReadOnly(databasePath);
        // 窄表没有 steamId 列：整行取回，缺列时按 null 读。
        return connection.Query("SELECT * FROM mods");
    }

    private static int CountPlaySetMods(IFileSystem fs, string databasePath)
    {
        using var connection = SqliteConnection.OpenReadOnly(databasePath);
        var value = connection.ExecuteScalar("SELECT COUNT(*) FROM playsets_mods");
        return value is null ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>读一列的文本值（Core 里的同名 helper 是 internal，自检里自己来）。</summary>
    private static string ReadText(IReadOnlyDictionary<string, object?> row, string column)
        => ReadNullableText(row, column) ?? string.Empty;

    private static string? ReadNullableText(IReadOnlyDictionary<string, object?> row, string column)
    {
        if (!row.TryGetValue(column, out var value) || value is null || value is DBNull)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>自检用的静默日志。</summary>
    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
