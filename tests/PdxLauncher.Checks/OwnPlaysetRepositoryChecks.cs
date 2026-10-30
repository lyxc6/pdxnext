namespace PdxLauncher.Checks;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>
/// 自有库播放集仓储 <see cref="OwnDatabasePlaySetRepository"/> 的自检：11 个接口方法、
/// 按 gameId 隔离、<c>playSet-not-found</c> 错误码、活动集唯一、删除级联。
/// <para>
/// 这是「用户没装官方启动器」时唯一的播放集来源，也是官方库在场时的镜像目标。
/// 它把不同游戏的 mod 与播放集混在一起（单库多游戏），所以 gameId 隔离一旦漏了，
/// 换游戏就会看到别人的播放集——这类错误不会抛异常，只能在自检里钉住。
/// </para>
/// </summary>
public static class OwnPlaysetRepositoryChecks
{
    private const string Ck3 = "ck3";
    private const string Victoria3 = "victoria3";

    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();
        var clock = SystemClock.Instance;

        CheckPlaySetLifecycle(fs, logger, clock);
        CheckModsRoundTrip(fs, logger, clock);
        CheckGameIsolation(fs, logger, clock);
        CheckErrors(fs, logger, clock);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>建、改、排序、激活、改名、删（含级联）全流程。</summary>
    private static void CheckPlaySetLifecycle(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[OwnPlaysetRepositoryChecks] 播放集生命周期");
        var root = PlaysetChecks.CreateTempRoot(fs, "ownplayset-life");
        try
        {
            var (game, repository) = CreateRepository(fs, root, logger, clock, Ck3);

            var created = repository.CreatePlaySetAsync(game, "我的播放集", CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check("建播放集成功", created.IsSuccess, created.ErrorMessage);
            var playSetId = created.Value;
            ChecksContext.Check("建播放集返回非空 id", playSetId != Guid.Empty, playSetId.ToString());

            var all = Read(repository.GetPlaySetsAsync(game, CancellationToken.None));
            ChecksContext.Equal("列表里 1 个播放集", 1, all.Count);
            ChecksContext.Equal("名字正确", "我的播放集", all[0].Name);
            ChecksContext.Check("新播放集不是活动集", !all[0].IsActive);

            var blank = repository.CreatePlaySetAsync(game, "   ", CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("空名播放集也能建（名字兜底）", blank.IsSuccess);
            ChecksContext.Equal(
                "空名兜底成 New Playset",
                "New Playset",
                Read(repository.GetPlaySetsAsync(game, CancellationToken.None)).Single(p => p.Id == blank.Value).Name);

            ChecksContext.Check(
                "改名成功",
                repository.RenamePlaySetAsync(game, playSetId, "改名后", CancellationToken.None).GetAwaiter().GetResult().IsSuccess);
            ChecksContext.Equal(
                "改名生效",
                "改名后",
                Read(repository.GetPlaySetsAsync(game, CancellationToken.None)).Single(p => p.Id == playSetId).Name);

            ChecksContext.Check(
                "激活成功",
                repository.SetActivePlaySetAsync(game, playSetId, CancellationToken.None).GetAwaiter().GetResult().IsSuccess);
            var active = repository.GetActivePlaySetAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("活动集是刚激活的那个", playSetId, active?.Id ?? Guid.Empty);

            ChecksContext.Check(
                "切到另一个集后仍只有一个活动集",
                repository.SetActivePlaySetAsync(game, blank.Value, CancellationToken.None).GetAwaiter().GetResult().IsSuccess);
            ChecksContext.Equal(
                "活动集唯一",
                1,
                Read(repository.GetPlaySetsAsync(game, CancellationToken.None)).Count(p => p.IsActive));

            var deleteActive = repository.DeletePlaySetAsync(game, blank.Value, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("删活动集被拒绝", !deleteActive.IsSuccess);
            ChecksContext.Equal("拒绝原因带 playSet-is-active", "playSet-is-active:" + blank.Value, deleteActive.ErrorMessage ?? "<null>");

            // 先塞点行进去，验证删除会连子表一起清。
            var modId = Guid.NewGuid();
            SeedMod(repository, game, modId, "被删的 mod", @"D:\fake\mod");
            repository.SavePlaySetModsAsync(
                game,
                playSetId,
                [MakeMod(modId, "被删的 mod", @"D:\fake\mod", enabled: true, position: 0)],
                CancellationToken.None).GetAwaiter().GetResult();
            repository.SavePlaySetDlcsAsync(
                game,
                playSetId,
                [new PlaySetDlc("dlc/001.dlc", true)],
                CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal(
                "删前有 1 行 mod",
                1,
                Read(repository.GetPlaySetModsAsync(game, playSetId, CancellationToken.None)).Count);

            ChecksContext.Check(
                "删非活动集成功",
                repository.DeletePlaySetAsync(game, playSetId, CancellationToken.None).GetAwaiter().GetResult().IsSuccess);
            ChecksContext.Equal(
                "删后播放集列表少一个",
                1,
                Read(repository.GetPlaySetsAsync(game, CancellationToken.None)).Count);
            ChecksContext.Equal(
                "删后子表行也清了",
                0,
                Read(repository.GetPlaySetModsAsync(game, playSetId, CancellationToken.None)).Count);
            ChecksContext.Equal(
                "删后 DLC 行也清了",
                0,
                Read(repository.GetPlaySetDlcsAsync(game, playSetId, CancellationToken.None)).Count);

            using var probe = SqliteConnection.OpenReadOnly(OwnDatabasePath(root, Ck3));
            ChecksContext.Equal(
                "库里的 playsets_mods 全清",
                0L,
                probe.ExecuteScalar("SELECT COUNT(*) FROM playsets_mods WHERE gameId = ?", Ck3) as long? ?? -1L);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>mod 行往返：字段原样保存、顺序按 position 归位、播放集归属聚合。</summary>
    private static void CheckModsRoundTrip(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[OwnPlaysetRepositoryChecks] mod 行往返");
        var root = PlaysetChecks.CreateTempRoot(fs, "ownplayset-mods");
        try
        {
            var (game, repository) = CreateRepository(fs, root, logger, clock, Victoria3);
            var playSetId = repository.CreatePlaySetAsync(game, "V3", CancellationToken.None).GetAwaiter().GetResult().Value;

            var localId = Guid.NewGuid();
            var steamId = Guid.NewGuid();
            var mods = new[]
            {
                MakeMod(steamId, "订阅的 mod", @"D:\steam\workshop\content\529340\111", enabled: true, position: 0, source: ModOrigins.SteamSource, steam: "111"),
                MakeMod(localId, "本地的 mod", @"C:\Users\u\Documents\Victoria 3\mod\local", enabled: false, position: 1, source: ModOrigins.LocalSource),
            };

            var saved = repository.SavePlaySetModsAsync(game, playSetId, mods, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("保存 mod 行成功", saved.IsSuccess, saved.ErrorMessage);

            var read = Read(repository.GetPlaySetModsAsync(game, playSetId, CancellationToken.None));
            ChecksContext.Equal("读回 2 行", 2, read.Count);
            ChecksContext.Equal("顺序按 position：第 0 行是订阅的", "订阅的 mod", read[0].Name);
            ChecksContext.Equal("顺序按 position：第 1 行是本地的", "本地的 mod", read[1].Name);
            ChecksContext.Check("启用态原样保留", read[0].Enabled && !read[1].Enabled);
            ChecksContext.Equal("路径原样保留", @"C:\Users\u\Documents\Victoria 3\mod\local", read[1].DirPath);
            ChecksContext.Equal("来源原样保留", ModOrigins.LocalSource, read[1].Source ?? "<null>");
            ChecksContext.Equal("工坊 id 原样保留", "111", read[0].SteamId ?? "<null>");

            // 用同一批行重存（positions 反序），库内顺序应跟着 position 走。
            var reordered = new[]
            {
                MakeMod(localId, "本地的 mod", @"C:\Users\u\Documents\Victoria 3\mod\local", enabled: false, position: 0, source: ModOrigins.LocalSource),
                MakeMod(steamId, "订阅的 mod", @"D:\steam\workshop\content\529340\111", enabled: true, position: 1, source: ModOrigins.SteamSource, steam: "111"),
            };
            repository.SavePlaySetModsAsync(game, playSetId, reordered, CancellationToken.None).GetAwaiter().GetResult();
            var afterReorder = Read(repository.GetPlaySetModsAsync(game, playSetId, CancellationToken.None));
            ChecksContext.Equal("重排后第 0 行是本地的", "本地的 mod", afterReorder[0].Name);
            ChecksContext.Equal("重排后仍是 2 行（不是追加）", 2, afterReorder.Count);

            var library = ReadLibrary(repository, game);
            ChecksContext.Equal("Mod 库有 2 条", 2, library.Count);
            var localEntry = library.Single(e => e.ModId == localId);
            ChecksContext.Equal("Mod 库聚合到播放集归属", 1, localEntry.PlaySetIds.Count);
            ChecksContext.Equal("Mod 库里的来源是本地", ModOrigin.Local, localEntry.Origin);
            ChecksContext.Check("Mod 库本地条目不叫订阅", !localEntry.IsSubscribed);
            var steamEntry = library.Single(e => e.ModId == steamId);
            ChecksContext.Equal("Mod 库里的来源是订阅", ModOrigin.Subscribed, steamEntry.Origin);

            // 路径修复走 UpdateModPathsAsync：只改路径，不动顺序与启停。
            var repaired = repository.UpdateModPathsAsync(
                game,
                localId,
                @"C:\Users\u\Documents\Victoria 3\mod\mod_978102dc",
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("修路径成功", repaired.IsSuccess, repaired.ErrorMessage);
            var afterRepair = Read(repository.GetPlaySetModsAsync(game, playSetId, CancellationToken.None));
            ChecksContext.Equal("修后路径变了", @"C:\Users\u\Documents\Victoria 3\mod\mod_978102dc", afterRepair[0].DirPath);
            ChecksContext.Check("修路径不动启用态", !afterRepair[0].Enabled);

            // 未登记进库的 mod 也能写进播放集（EnsureModsRegistered 的等价路径）。
            var unregisteredId = Guid.NewGuid();
            repository.SavePlaySetModsAsync(
                game,
                playSetId,
                [MakeMod(unregisteredId, "未登记的 mod", @"D:\fake\unregistered", enabled: true, position: 2)],
                CancellationToken.None).GetAwaiter().GetResult();
            var withUnregistered = ReadLibrary(repository, game);
            ChecksContext.Equal("补登后 Mod 库有 3 条", 3, withUnregistered.Count);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>同一个库文件里两个 gameId 必须互不可见。</summary>
    private static void CheckGameIsolation(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[OwnPlaysetRepositoryChecks] gameId 隔离");
        var root = PlaysetChecks.CreateTempRoot(fs, "ownplayset-isolation");
        try
        {
            var (ck3Game, repository) = CreateRepository(fs, root, logger, clock, Ck3);
            var (v3Game, _) = CreateRepository(fs, root, logger, clock, Victoria3);

            var ck3Set = repository.CreatePlaySetAsync(ck3Game, "CK3 集", CancellationToken.None).GetAwaiter().GetResult().Value;
            var v3Set = repository.CreatePlaySetAsync(v3Game, "V3 集", CancellationToken.None).GetAwaiter().GetResult().Value;

            repository.SavePlaySetModsAsync(
                ck3Game,
                ck3Set,
                [MakeMod(Guid.NewGuid(), "CK3 的 mod", @"C:\ck3\mod", enabled: true, position: 0)],
                CancellationToken.None).GetAwaiter().GetResult();
            repository.SavePlaySetModsAsync(
                v3Game,
                v3Set,
                [MakeMod(Guid.NewGuid(), "V3 的 mod", @"C:\v3\mod", enabled: true, position: 0)],
                CancellationToken.None).GetAwaiter().GetResult();
            repository.SetActivePlaySetAsync(ck3Game, ck3Set, CancellationToken.None).GetAwaiter().GetResult();

            var ck3Sets = Read(repository.GetPlaySetsAsync(ck3Game, CancellationToken.None));
            var v3Sets = Read(repository.GetPlaySetsAsync(v3Game, CancellationToken.None));
            ChecksContext.Equal("CK3 只看到自己的 1 个集", 1, ck3Sets.Count);
            ChecksContext.Equal("V3 只看到自己的 1 个集", 1, v3Sets.Count);
            ChecksContext.Equal("CK3 集名字对", "CK3 集", ck3Sets[0].Name);
            ChecksContext.Equal("V3 集名字对", "V3 集", v3Sets[0].Name);

            ChecksContext.Check("CK3 的活动集生效", ck3Sets[0].IsActive);
            ChecksContext.Check("V3 没有被 CK3 的活动集带歪", !v3Sets[0].IsActive);

            var ck3Mods = Read(repository.GetPlaySetModsAsync(ck3Game, ck3Set, CancellationToken.None));
            var v3Mods = Read(repository.GetPlaySetModsAsync(v3Game, v3Set, CancellationToken.None));
            ChecksContext.Equal("CK3 集只有 CK3 的 mod", "CK3 的 mod", ck3Mods.Single().Name);
            ChecksContext.Equal("V3 集只有 V3 的 mod", "V3 的 mod", v3Mods.Single().Name);

            var ck3Lib = ReadLibrary(repository, ck3Game);
            ChecksContext.Equal("CK3 Mod 库不含 V3 的 mod", 1, ck3Lib.Count);
            ChecksContext.Equal("CK3 Mod 库那条是 CK3 的", "CK3 的 mod", ck3Lib[0].Name);

            // 用另一个 gameId 的集 id 去读，读不到任何行。
            ChecksContext.Equal(
                "跨游戏读播放集行读到空",
                0,
                Read(repository.GetPlaySetModsAsync(ck3Game, v3Set, CancellationToken.None)).Count);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>不存在的 id 一律返回<see cref="ErrorKind.NotFound"/> 与稳定错误码。</summary>
    private static void CheckErrors(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[OwnPlaysetRepositoryChecks] 错误码");
        var root = PlaysetChecks.CreateTempRoot(fs, "ownplayset-errors");
        try
        {
            var (game, repository) = CreateRepository(fs, root, logger, clock, Ck3);
            var missing = Guid.NewGuid();

            var rename = repository.RenamePlaySetAsync(game, missing, "x", CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("改不存在的播放集失败", !rename.IsSuccess);
            ChecksContext.Equal("改不存在的播放集错误码", "playSet-not-found:" + missing, rename.ErrorMessage ?? "<null>");

            var activate = repository.SetActivePlaySetAsync(game, missing, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("激活不存在的播放集失败", !activate.IsSuccess);
            ChecksContext.Equal("激活不存在的播放集错误码", "playSet-not-found:" + missing, activate.ErrorMessage ?? "<null>");

            var delete = repository.DeletePlaySetAsync(game, missing, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("删不存在的播放集失败", !delete.IsSuccess);
            ChecksContext.Equal("删不存在的播放集错误码", "playSet-not-found:" + missing, delete.ErrorMessage ?? "<null>");

            var emptyId = repository.UpdateModPathsAsync(game, Guid.Empty, @"C:\x", null, CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check("modId 为空时修路径失败", !emptyId.IsSuccess);
            ChecksContext.Equal("modId 为空的错误码", "mod-id-empty", emptyId.ErrorMessage ?? "<null>");

            var missingMod = repository.UpdateModPathsAsync(game, Guid.NewGuid(), @"C:\x", null, CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check("修不存在的 mod 路径失败", !missingMod.IsSuccess);
            ChecksContext.Check(
                "修不存在的 mod 错误码带 mod-not-found",
                (missingMod.ErrorMessage ?? string.Empty).StartsWith("mod-not-found:", StringComparison.Ordinal),
                missingMod.ErrorMessage);

            ChecksContext.Check(
                "空库读播放集不抛异常",
                Read(repository.GetPlaySetsAsync(game, CancellationToken.None)).Count == 0);
            ChecksContext.Check(
                "空库活动集为 null",
                repository.GetActivePlaySetAsync(game, CancellationToken.None).GetAwaiter().GetResult() is null);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 辅助
    // ------------------------------------------------------------------

    /// <summary>每个用例一个独立库文件（同一 root 下按 gameId 复用，用于隔离用例）。</summary>
    private static (GameInstallation Game, OwnDatabasePlaySetRepository Repository) CreateRepository(
        IFileSystem fs,
        string root,
        NullLogger logger,
        IClock clock,
        string gameId)
    {
        var gameDataDir = Path.Combine(root, "documents", gameId);
        fs.EnsureDirectory(gameDataDir);
        var game = PlaysetChecks.CreateGame(
            gameId,
            Path.Combine(root, "install", gameId),
            LoadFileFormat.ContentLoadV1_1,
            gameDataDir,
            Path.Combine(root, "install", gameId, "game", "dlc"),
            Path.Combine(gameDataDir, "mod"),
            steamAppId: gameId == Victoria3 ? 529340 : null);

        var database = new PdxDatabase(fs, logger, OwnDatabasePath(root, gameId));
        var repository = new OwnDatabasePlaySetRepository(database, logger, clock);
        return (game, repository);
    }

    private static string OwnDatabasePath(string root, string gameId) => Path.Combine(root, "own-" + gameId + ".sqlite");

    private static void SeedMod(
        OwnDatabasePlaySetRepository repository,
        GameInstallation game,
        Guid modId,
        string name,
        string dirPath)
    {
        repository.SavePlaySetModsAsync(
            game,
            Guid.NewGuid(),
            [MakeMod(modId, name, dirPath, enabled: true, position: 0)],
            CancellationToken.None).GetAwaiter().GetResult();
    }

    private static PlaySetMod MakeMod(
        Guid modId,
        string name,
        string dirPath,
        bool enabled,
        int position,
        string? source = null,
        string? steam = null,
        string? archive = null,
        string? registry = null,
        string requiredVersion = "")
        => new(
            ModId: modId,
            Name: name,
            DirPath: dirPath,
            ArchivePath: archive,
            GameRegistryId: registry ?? string.Empty,
            ThumbnailPath: null,
            RequiredVersion: requiredVersion,
            Status: "ready_to_play",
            Enabled: enabled,
            Position: position,
            SteamId: steam,
            Source: source);

    private static List<T> Read<T>(Task<IReadOnlyList<T>> task) => task.GetAwaiter().GetResult().ToList();

    private static List<ModLibraryEntry> ReadLibrary(OwnDatabasePlaySetRepository repository, GameInstallation game)
        => Read(repository.GetModLibraryAsync(game, CancellationToken.None));

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
