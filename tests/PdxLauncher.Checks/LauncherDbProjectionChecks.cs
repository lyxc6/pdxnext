namespace PdxLauncher.Checks;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>
/// 自有库 ↔ 官方 <c>launcher-v2.sqlite</c> 的双向搬运自检。
/// <para>
/// 两条硬约束在这里钉住：
/// <list type="number">
/// <item>官方库在场时，第一次读之前要先把官方数据导进自有库（用户已有的播放集不能丢），
/// 之后每次写都要镜像回官方库（官方启动器仍然认识这些数据）。</item>
/// <item>官方库缺席时（用户没装官方启动器）只写自有库，<b>绝不创建 launcher-v2.sqlite</b>——
/// 凭空造一个官方库会让真正的官方启动器装好后认错数据。</item>
/// </list>
/// </para>
/// </summary>
public static class LauncherDbProjectionChecks
{
    private const string GameId = "victoria3";

    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();
        var clock = SystemClock.Instance;

        CheckImportFromOfficial(fs, logger, clock);
        CheckProjectToOfficial(fs, logger, clock);
        CheckMissingLauncherDatabase(fs, logger, clock);
        CheckPendingProjection(fs, logger, clock);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>官方库既有数据第一次读之前必须搬进自有库（含路径、来源、播放集归属）。</summary>
    private static void CheckImportFromOfficial(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[LauncherDbProjectionChecks] 官方库 → 自有库");
        var root = PlaysetChecks.CreateTempRoot(fs, "projector-import");
        try
        {
            var game = CreateGame(fs, root);
            SeedOfficialDatabase(fs, game);
            var (database, own, official, projector) = CreateStack(fs, root, logger, clock, game);

            // 读之前先 PrepareForReadAsync：官方库在且自有库为空时自动导入。
            projector.PrepareForReadAsync(game, CancellationToken.None).GetAwaiter().GetResult();

            var playSets = own.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("导入后自有库有 1 个播放集", 1, playSets.Count);
            ChecksContext.Equal("播放集名字来自官方库", "官方播放集", playSets[0].Name);
            ChecksContext.Check("活动态一并导入", playSets[0].IsActive);

            var mods = own.GetPlaySetModsAsync(game, playSets[0].Id, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("导入后播放集有 2 行", 2, mods.Count);
            ChecksContext.Equal("第 0 行是订阅的 mod", "订阅的 mod", mods[0].Name);
            ChecksContext.Equal("订阅来源随行导入", ModOrigins.SteamSource, mods[0].Source ?? "<null>");
            ChecksContext.Equal("订阅 mod 的工坊 id 也导入", "111", mods[0].SteamId ?? "<null>");
            ChecksContext.Equal("第 1 行是本地的 mod", "本地的 mod", mods[1].Name);
            ChecksContext.Equal("本地来源随行导入", ModOrigins.LocalSource, mods[1].Source ?? "<null>");
            ChecksContext.Check("本地行没有工坊 id", mods[1].SteamId is null);
            ChecksContext.Equal("本地行路径原样导入", @"C:\Users\u\Documents\Victoria 3\mod\local", mods[1].DirPath);

            var library = own.GetModLibraryAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("Mod 库导入 2 条", 2, library.Count);
            ChecksContext.Equal("Mod 库里的本地条目判别正确", 1, library.Count(e => e.IsLocalOnly));
            ChecksContext.Equal("Mod 库里的订阅条目判别正确", 1, library.Count(e => e.IsSubscribed));
            ChecksContext.Check(
                "导入后写了 imported 标记",
                database.ReadMeta(LauncherDbProjector.ImportedPrefix + GameId) is not null);

            // 第二次 PrepareForReadAsync 在没有改动时不该重复搬运（这里只看结果仍然正确）。
            projector.PrepareForReadAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal(
                "重复 Prepare 不会把行数翻倍",
                1,
                own.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult().Count);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>自有库写完之后，官方库要能看到同一批播放集、mod 行与来源。</summary>
    private static void CheckProjectToOfficial(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[LauncherDbProjectionChecks] 自有库 → 官方库");
        var root = PlaysetChecks.CreateTempRoot(fs, "projector-project");
        try
        {
            var game = CreateGame(fs, root);
            SeedOfficialDatabase(fs, game);
            var (database, own, official, projector) = CreateStack(fs, root, logger, clock, game);
            projector.PrepareForReadAsync(game, CancellationToken.None).GetAwaiter().GetResult();

            // 在自有库里新加一个播放集与 mod，然后投射。
            // 注意：官方库的补登记只接受「磁盘上真的有内容」的 mod（HasRegisteredContent），
            // 所以夹具必须把目录造出来，否则行会写进 playsets_mods 却读不回来。
            var modDir = Path.Combine(root, "documents", "Victoria 3", "mod", "mod_abc12345");
            fs.EnsureDirectory(modDir);
            var newId = own.CreatePlaySetAsync(game, "新建集", CancellationToken.None).GetAwaiter().GetResult().Value;
            own.SavePlaySetModsAsync(
                game,
                newId,
                [MakeMod(Guid.NewGuid(), "新增本地 mod", modDir, source: ModOrigins.LocalSource)],
                CancellationToken.None).GetAwaiter().GetResult();

            projector.SyncAfterOwnWriteAsync(game, CancellationToken.None).GetAwaiter().GetResult();

            var officialSets = official.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("官方库补登记后共 2 个播放集", 2, officialSets.Count);
            ChecksContext.Check("官方库能看到新集", officialSets.Any(p => p.Id == newId && p.Name == "新建集"));

            var officialMods = official.GetPlaySetModsAsync(game, newId, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("官方库能看到新集的 1 行 mod", 1, officialMods.Count);
            ChecksContext.Equal("新行名字对", "新增本地 mod", officialMods[0].Name);
            ChecksContext.Equal("新行路径回写成功", modDir, officialMods[0].DirPath);
            ChecksContext.Equal("新行来源回写为本地", ModOrigins.LocalSource, officialMods[0].Source ?? "<null>");

            ChecksContext.Check(
                "投射后记了 projected 标记",
                database.ReadMeta(LauncherDbProjector.ProjectedPrefix + GameId) is not null);
            ChecksContext.Check("投射后不再挂着 pending", !projector.HasPendingProjection(game));

            // 官方库里原有的原版播放集没有被抹掉。
            ChecksContext.Check(
                "官方库原有播放集仍在",
                officialSets.Any(p => p.Name == "官方播放集"));
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>官方库缺席时只写自有库，绝不创建 launcher-v2.sqlite。</summary>
    private static void CheckMissingLauncherDatabase(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[LauncherDbProjectionChecks] 官方库缺席");
        var root = PlaysetChecks.CreateTempRoot(fs, "projector-missing");
        try
        {
            var game = CreateGame(fs, root);
            var officialPath = PlaySetRepository.GetDatabasePath(game);
            ChecksContext.Check("前置：官方库确实不存在", !fs.FileExists(officialPath));

            var (database, own, _, projector) = CreateStack(fs, root, logger, clock, game);
            ChecksContext.Check(
                "IsLauncherDatabasePresent 判为缺席",
                !LauncherDbProjector.IsLauncherDatabasePresent(fs, game));

            projector.PrepareForReadAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("Prepare 之后仍不创建官方库", !fs.FileExists(officialPath));

            var missingModDir = Path.Combine(root, "documents", "Victoria 3", "mod", "mod_onlydb1");
            fs.EnsureDirectory(missingModDir);
            var id = own.CreatePlaySetAsync(game, "只有自有库", CancellationToken.None).GetAwaiter().GetResult().Value;
            own.SavePlaySetModsAsync(
                game,
                id,
                [MakeMod(Guid.NewGuid(), "本地 mod", missingModDir, source: ModOrigins.LocalSource)],
                CancellationToken.None).GetAwaiter().GetResult();

            projector.SyncAfterOwnWriteAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("写自有库之后依然不创建官方库", !fs.FileExists(officialPath));

            var projected = projector.ProjectAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("主动投射在缺库时报失败", !projected.IsSuccess);
            ChecksContext.Check(
                "缺库时错误码是 database-not-found",
                (projected.ErrorMessage ?? string.Empty).StartsWith("database-not-found:", StringComparison.Ordinal),
                projected.ErrorMessage);
            ChecksContext.Check("失败也没创建官方库", !fs.FileExists(officialPath));

            ChecksContext.Check("自有库数据仍然在", own.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult().Count == 1);
            ChecksContext.Check(
                "缺库时挂起投射标记，等官方库出现再补",
                projector.HasPendingProjection(game));
            ChecksContext.Check(
                "pending 标记真的写进了 meta",
                database.ReadMeta(LauncherDbProjector.PendingProjectionPrefix + GameId) is not null);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>官方库后出现时，下一次读要把挂起的投射补上。</summary>
    private static void CheckPendingProjection(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[LauncherDbProjectionChecks] 挂起投射补做");
        var root = PlaysetChecks.CreateTempRoot(fs, "projector-pending");
        try
        {
            var game = CreateGame(fs, root);
            var officialPath = PlaySetRepository.GetDatabasePath(game);
            var (database, own, official, projector) = CreateStack(fs, root, logger, clock, game);

            var offlineModDir = Path.Combine(root, "documents", "Victoria 3", "mod", "mod_offline1");
            fs.EnsureDirectory(offlineModDir);
            var id = own.CreatePlaySetAsync(game, "离线建的集", CancellationToken.None).GetAwaiter().GetResult().Value;
            own.SavePlaySetModsAsync(
                game,
                id,
                [MakeMod(Guid.NewGuid(), "离线本地 mod", offlineModDir, source: ModOrigins.LocalSource)],
                CancellationToken.None).GetAwaiter().GetResult();
            projector.SyncAfterOwnWriteAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("离线写后挂起标记在", projector.HasPendingProjection(game));

            // 官方启动器装好了：出现一个空 schema 的官方库。
            SeedEmptyOfficialDatabase(fs, game);
            ChecksContext.Check("官方库出现了", LauncherDbProjector.IsLauncherDatabasePresent(fs, game));

            projector.PrepareForReadAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("补做投射后不再 pending", !projector.HasPendingProjection(game));

            var officialSets = official.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("官方库补到了离线建的集", officialSets.Any(p => p.Id == id && p.Name == "离线建的集"));
            var officialMods = official.GetPlaySetModsAsync(game, id, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("官方库补到了离线 mod 行", 1, officialMods.Count);
            ChecksContext.Equal("离线 mod 的名字对", "离线本地 mod", officialMods[0].Name);
            ChecksContext.Check("补做后自有库数据没被抹掉", own.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult().Count == 1);
            ChecksContext.Check(
                "补做后记了 projected 标记",
                database.ReadMeta(LauncherDbProjector.ProjectedPrefix + GameId) is not null);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 夹具
    // ------------------------------------------------------------------

    private static GameInstallation CreateGame(IFileSystem fs, string root)
    {
        var gameDataDir = Path.Combine(root, "documents", "Victoria 3");
        var modDir = Path.Combine(gameDataDir, "mod");
        fs.EnsureDirectory(gameDataDir);
        fs.EnsureDirectory(modDir);
        return PlaysetChecks.CreateGame(
            GameId,
            Path.Combine(root, "steamapps", "common", "Victoria 3"),
            LoadFileFormat.ContentLoadV1_1,
            gameDataDir,
            Path.Combine(root, "steamapps", "common", "Victoria 3", "game", "dlc"),
            modDir,
            steamAppId: 529340);
    }

    private static (PdxDatabase Database, OwnDatabasePlaySetRepository Own, PlaySetRepository Official, LauncherDbProjector Projector) CreateStack(
        IFileSystem fs,
        string root,
        NullLogger logger,
        IClock clock,
        GameInstallation game)
    {
        var database = new PdxDatabase(fs, logger, Path.Combine(root, "pdxnext.sqlite"));
        var own = new OwnDatabasePlaySetRepository(database, logger, clock);
        var official = new PlaySetRepository(fs, logger, clock);
        var projector = new LauncherDbProjector(fs, official, own, database, logger);
        return (database, own, official, projector);
    }

    /// <summary>造一个「官方启动器已经用过」的库：1 个活动播放集 + 1 个订阅 mod + 1 个本地 mod。</summary>
    private static void SeedOfficialDatabase(IFileSystem fs, GameInstallation game)
    {
        var path = PlaySetRepository.GetDatabasePath(game);
        fs.EnsureDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        using var connection = SqliteConnection.OpenOrCreate(path);
        CreateOfficialTables(connection);

        var playSetId = "11111111-1111-1111-1111-111111111111";
        connection.Execute(
            "INSERT INTO playsets (id, name, isActive, createdOn) VALUES (?, ?, ?, ?)",
            playSetId,
            "官方播放集",
            1,
            1000L);

        connection.Execute(
            "INSERT INTO mods (id, displayName, dirPath, archivePath, gameRegistryId, steamId, requiredVersion, status, source, size) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "订阅的 mod",
            @"D:\steamapps\workshop\content\529340\111",
            null,
            null,
            "111",
            "1.13.*",
            "ready_to_play",
            "steam",
            1024L);
        connection.Execute(
            "INSERT INTO mods (id, displayName, dirPath, archivePath, gameRegistryId, steamId, requiredVersion, status, source, size) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            "本地的 mod",
            @"C:\Users\u\Documents\Victoria 3\mod\local",
            null,
            string.Empty,
            null,
            string.Empty,
            "ready_to_play",
            "local",
            2048L);

        connection.Execute(
            "INSERT INTO playsets_mods (playSetId, modId, enabled, position) VALUES (?, ?, ?, ?)",
            playSetId,
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            1,
            0);
        connection.Execute(
            "INSERT INTO playsets_mods (playSetId, modId, enabled, position) VALUES (?, ?, ?, ?)",
            playSetId,
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            0,
            1);
    }

    /// <summary>只有空表、没有数据的官方库（模拟「刚装好官方启动器」）。</summary>
    private static void SeedEmptyOfficialDatabase(IFileSystem fs, GameInstallation game)
    {
        var path = PlaySetRepository.GetDatabasePath(game);
        fs.EnsureDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        using var connection = SqliteConnection.OpenOrCreate(path);
        CreateOfficialTables(connection);
    }

    private static void CreateOfficialTables(SqliteConnection connection)
    {
        connection.Execute(
            "CREATE TABLE playsets (id char(36) PRIMARY KEY, name varchar(255) NOT NULL, isActive boolean, " +
            "loadOrder integer, createdOn datetime NOT NULL, updatedOn datetime)");
        connection.Execute(
            "CREATE TABLE mods (id char(36) PRIMARY KEY, displayName varchar(255), thumbnailPath text, dirPath text, " +
            "archivePath text, gameRegistryId text, steamId text, requiredVersion varchar(255), status text NOT NULL, " +
            "source text NOT NULL, size integer, subscribedDate integer)");
        connection.Execute(
            "CREATE TABLE playsets_mods (playsetId char(36), modId char(36), enabled boolean DEFAULT '1', position integer)");
        connection.Execute(
            "CREATE TABLE playsets_dlcs (playsetId char(36), dlcId text NOT NULL, enabled boolean NOT NULL DEFAULT '0', " +
            "PRIMARY KEY (playsetId, dlcId))");
    }

    private static PlaySetMod MakeMod(Guid modId, string name, string dirPath, string? source)
        => new(
            ModId: modId,
            Name: name,
            DirPath: dirPath,
            ArchivePath: null,
            GameRegistryId: string.Empty,
            ThumbnailPath: null,
            RequiredVersion: string.Empty,
            Status: "ready_to_play",
            Enabled: true,
            Position: 0,
            SteamId: null,
            Source: source);

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
