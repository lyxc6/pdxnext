namespace PdxLauncher.Checks;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.LoadFiles;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>
/// 「本地 mod / 订阅 mod 分不清」「本地 mod 进游戏后没生效」两件事的判定与修复自检。
/// <para>
/// 三块拼起来才是完整链路：
/// <list type="number">
/// <item><see cref="ModOrigins"/>：来源归一（source 优先，缺了才看工坊 id）。</item>
/// <item><see cref="GamePathRules"/> + <see cref="ModLoadRules"/>：哪一行会被加载文件丢掉、为什么。</item>
/// <item><see cref="ModPathRepairer"/>：把丢路径的行补回来、把非 ASCII 目录改成 ASCII 名。</item>
/// </list>
/// 真机现场（Victoria 3）：本地 mod 目录叫 <c>更改虚空造物</c>，路径照样写进了 <c>content_load.json</c>，
/// 但游戏不加载任何非 ASCII 路径的 mod，表现就是「明明启用了却不生效」。
/// </para>
/// </summary>
public static class ModPathChecks
{
    private const string Victoria3 = "victoria3";
    private const string Ck3 = "ck3";

    /// <summary>真机那条本地 mod 的 id：改名建议名是 <c>mod_978102dc</c>。</summary>
    private static readonly Guid LocalModId = new("978102dc-1c55-42e5-a6cd-7481c538e16e");

    /// <summary>真机那条订阅 mod 的工坊 id（dirPath/archivePath 都是 NULL 的那条）。</summary>
    private const string StaleWorkshopId = "3717461054";

    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();
        var clock = SystemClock.Instance;

        CheckOriginMatrix();
        CheckPathRules();
        CheckSkipKeys();
        CheckRepairFillFromDisk(fs, logger, clock);
        CheckRepairRename(fs, logger, clock);
        CheckRepairNonAsciiParent(fs, logger, clock);
        CheckKeysExist();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>source 优先、缺了才看工坊 id；不能用「有没有 steamId」当唯一依据。</summary>
    private static void CheckOriginMatrix()
    {
        Console.WriteLine("[ModPathChecks] 来源归一");
        ChecksContext.Equal("source=local ⇒ 本地", ModOrigin.Local, ModOrigins.Normalize("local", null));
        ChecksContext.Equal("source=steam ⇒ 订阅", ModOrigin.Subscribed, ModOrigins.Normalize("steam", null));
        ChecksContext.Equal("source=paradox ⇒ 订阅", ModOrigin.Subscribed, ModOrigins.Normalize("paradox", null));
        ChecksContext.Equal("source=pdx_mods ⇒ 订阅", ModOrigin.Subscribed, ModOrigins.Normalize("pdx_mods", null));
        ChecksContext.Equal("source 大小写不敏感", ModOrigin.Local, ModOrigins.Normalize("LOCAL", "999"));

        // 真机案例：CK3 有一条 source=local 却带着 steamId 的本地 mod。source 说了算。
        ChecksContext.Equal(
            "source=local 压过非空工坊 id（真机 CK3）",
            ModOrigin.Local,
            ModOrigins.Normalize("local", "3613769385"));

        ChecksContext.Equal("source 缺失 + 有工坊 id ⇒ 订阅", ModOrigin.Subscribed, ModOrigins.Normalize(null, "123"));
        ChecksContext.Equal("source 缺失 + 无工坊 id ⇒ 本地", ModOrigin.Local, ModOrigins.Normalize(null, null));
        ChecksContext.Equal("source 缺失 + 空串工坊 id ⇒ 本地", ModOrigin.Local, ModOrigins.Normalize("", "   "));
        ChecksContext.Equal("不认识的 source 退回工坊 id", ModOrigin.Local, ModOrigins.Normalize("mystery", null));

        ChecksContext.Equal("徽标键：本地", "CommonSourceLocal", ModOrigins.DescribeKey(ModOrigin.Local));
        ChecksContext.Equal("徽标键：订阅", "CommonSourceSubscribed", ModOrigins.DescribeKey(ModOrigin.Subscribed));
        ChecksContext.Equal("徽标键：由字段直取", "CommonSourceSubscribed", ModOrigins.DescribeKey("steam", null));
        ChecksContext.Check("IsSubscribed 与 Normalize 一致", ModOrigins.IsSubscribed("pdx_mods", null));
        ChecksContext.Check("IsSubscribed 对本地为假", !ModOrigins.IsSubscribed("local", "1"));
    }

    /// <summary>只有 Victoria 3 有非 ASCII 路径限制。</summary>
    private static void CheckPathRules()
    {
        Console.WriteLine("[ModPathChecks] 路径限制表");
        ChecksContext.Check("Victoria 3 需要 ASCII 路径", GamePathRules.RequiresAsciiPaths(Victoria3));
        ChecksContext.Check("gameId 大小写不敏感", GamePathRules.RequiresAsciiPaths("Victoria3"));
        ChecksContext.Check("CK3 不限制", !GamePathRules.RequiresAsciiPaths(Ck3));
        ChecksContext.Check("Stellaris 不限制", !GamePathRules.RequiresAsciiPaths("stellaris"));
        ChecksContext.Check("EU4 不限制", !GamePathRules.RequiresAsciiPaths("eu4"));
        ChecksContext.Check("gameId 为 null 不限制", !GamePathRules.RequiresAsciiPaths(null));
        ChecksContext.Equal("提示键：Victoria 3", "ModsWarnNonAsciiPath", GamePathRules.DescribeAsciiRuleKey(Victoria3));
        ChecksContext.Equal("提示键：CK3 没有", null, GamePathRules.DescribeAsciiRuleKey(Ck3));
    }

    /// <summary>六种「会被跳过」的原因各自命中，顺序符合优先级。</summary>
    private static void CheckSkipKeys()
    {
        Console.WriteLine("[ModPathChecks] 加载文件跳过规则");

        var v3 = MakeGame(Victoria3, LoadFileFormat.ContentLoadV1_1);
        var ck3 = MakeGame(Ck3, LoadFileFormat.DlcLoadV0);
        var v1 = MakeGame("prison_architect_pc_console", LoadFileFormat.ContentLoadV1);
        var cs2 = MakeGame("cities_skylines_2", LoadFileFormat.PlaysetConfigV1);

        ChecksContext.Equal(
            "两条路径都空 ⇒ 无路径",
            ModLoadRules.WarnNoPath,
            ModLoadRules.DescribeSkipKey(v3, MakeMod(null, null, "mod/a.mod", true)));

        ChecksContext.Equal(
            "停用的行不报警告",
            null,
            ModLoadRules.DescribeSkipKey(v3, MakeMod(null, null, null, false)));

        ChecksContext.Equal(
            "停用且无路径也不报警告",
            null,
            ModLoadRules.DescribeSkipKey(v3, MakeMod(null, null, null, false)));

        ChecksContext.Equal(
            "V0 缺 gameRegistryId ⇒ 写不出这一行",
            ModLoadRules.WarnNoRegistryId,
            ModLoadRules.DescribeSkipKey(ck3, MakeMod(@"C:\mods\a", null, string.Empty, true)));

        ChecksContext.Equal(
            "V0 有 gameRegistryId 就正常",
            null,
            ModLoadRules.DescribeSkipKey(ck3, MakeMod(@"C:\mods\a", null, "mod/a.mod", true)));

        ChecksContext.Equal(
            "V1 只有目录型 mod ⇒ 写不进去",
            ModLoadRules.WarnNoArchive,
            ModLoadRules.DescribeSkipKey(v1, MakeMod(@"C:\mods\a", null, "mod/a.mod", true)));

        ChecksContext.Equal(
            "V1 有压缩包就正常",
            null,
            ModLoadRules.DescribeSkipKey(v1, MakeMod(null, @"C:\mods\a.zip", "mod/a.mod", true)));

        ChecksContext.Equal(
            "V1.1 目录型可表达，不报缺压缩包",
            null,
            ModLoadRules.DescribeSkipKey(v3, MakeMod(@"C:\mods\ascii", null, "mod/a.mod", true)));

        ChecksContext.Equal(
            "路径不存在 ⇒ 缺文件",
            ModLoadRules.WarnMissingPath,
            ModLoadRules.DescribeSkipKey(
                v3,
                MakeMod(@"C:\mods\ascii", null, "mod/a.mod", true),
                _ => false,
                _ => false));

        ChecksContext.Equal(
            "路径存在就不报缺文件",
            null,
            ModLoadRules.DescribeSkipKey(
                v3,
                MakeMod(@"C:\mods\ascii", null, "mod/a.mod", true),
                _ => true,
                _ => true));

        ChecksContext.Equal(
            "Victoria 3 + 中文目录名 ⇒ 非 ASCII 路径",
            ModLoadRules.WarnNonAsciiPath,
            ModLoadRules.DescribeSkipKey(
                v3,
                MakeMod(@"C:\Users\u\Documents\Victoria 3\mod\更改虚空造物", null, string.Empty, true)));

        ChecksContext.Equal(
            "Victoria 3 + 中文父目录（用户名）⇒ 只提示父目录",
            ModLoadRules.WarnNonAsciiParent,
            ModLoadRules.DescribeSkipKey(
                v3,
                MakeMod(@"C:\Users\张三\Documents\Victoria 3\mod\ascii_mod", null, string.Empty, true)));

        ChecksContext.Equal(
            "CK3 + 中文目录名不报警告（真机本地 mod 正常加载）",
            null,
            ModLoadRules.DescribeSkipKey(
                ck3,
                MakeMod(@"C:\mods\甜蜜约会", null, "mod/甜蜜约会.mod", true)));

        ChecksContext.Equal(
            "CS2 没有加载文件可写，恒为 null",
            null,
            ModLoadRules.DescribeSkipKey(cs2, MakeMod(null, null, null, true)));

        ChecksContext.Check("非 ASCII 判定：中文为真", ModLoadRules.ContainsNonAscii("更改虚空"));
        ChecksContext.Check("非 ASCII 判定：带重音的拉丁字母为真", ModLoadRules.ContainsNonAscii("Café"));
        ChecksContext.Check("非 ASCII 判定：纯 ASCII 为假", !ModLoadRules.ContainsNonAscii(@"D:\app\Steam\mod_x"));
        ChecksContext.Check("非 ASCII 判定：null 为假", !ModLoadRules.ContainsNonAscii(null));
    }

    /// <summary>没有路径的订阅 mod：按工坊 id 在磁盘上找到目录后补回库。</summary>
    private static void CheckRepairFillFromDisk(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[ModPathChecks] 修复：无路径的订阅 mod");
        var root = PlaysetChecks.CreateTempRoot(fs, "modpath-fill");
        try
        {
            var game = MakeGameAt(fs, root, Victoria3, LoadFileFormat.ContentLoadV1_1);
            var workshopDir = Path.Combine(root, "workshop", "content", "529340", StaleWorkshopId);
            fs.EnsureDirectory(workshopDir);

            var repository = CreateRepository(fs, root, logger, clock);
            var playSetId = CreatePlaySet(repository, game);
            SaveMods(repository, game, playSetId, MakeMod(null, null, null, true, StaleWorkshopId));

            var scanned = new List<PlaySetMod>
            {
                MakeMod(workshopDir, null, null, true, StaleWorkshopId, name: "喜儿修改器"),
            };
            var repairer = new ModPathRepairer(fs, repository, new FakeModCatalog(scanned), logger);
            var repairs = repairer.FindAsync(game, CancellationToken.None).GetAwaiter().GetResult();

            ChecksContext.Equal("找到一条修复", 1, repairs.Count);
            ChecksContext.Equal("类型是补路径", ModPathRepairKind.FillFromDisk, repairs[0].Kind);
            ChecksContext.Equal("建议路径来自磁盘", workshopDir, repairs[0].SuggestedPath);
            ChecksContext.Equal("补得到就不用报警告", null, repairs[0].ReasonKey);
            ChecksContext.Equal("当前路径为空", null, repairs[0].CurrentPath);

            var applied = repairer.ApplyAsync(game, repairs, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("应用成功", applied.IsSuccess);
            ChecksContext.Equal("修了 1 条", 1, applied.Value);

            var library = repository.GetModLibraryAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("库里的路径补上了", workshopDir, library[0].DirPath);

            // 再来一次：已经没有可修的了。
            var again = repairer.FindAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("修完之后不再有建议", 0, again.Count);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>中文目录名的本地 mod：改成 ASCII 名，撞名时加 _2，并把库里的路径一起改掉。</summary>
    private static void CheckRepairRename(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[ModPathChecks] 修复：非 ASCII 目录改名");
        var root = PlaysetChecks.CreateTempRoot(fs, "modpath-rename");
        try
        {
            var game = MakeGameAt(fs, root, Victoria3, LoadFileFormat.ContentLoadV1_1);
            var modRoot = game.ModDir;
            var localDir = Path.Combine(modRoot, "更改虚空造物");
            fs.EnsureDirectory(localDir);

            var repository = CreateRepository(fs, root, logger, clock);
            var playSetId = CreatePlaySet(repository, game);
            SaveMods(
                repository,
                game,
                playSetId,
                MakeMod(localDir, null, string.Empty, true, steam: null, name: "虚空造物+货运红利", modId: LocalModId, source: ModOrigins.LocalSource));

            var repairer = new ModPathRepairer(fs, repository, new FakeModCatalog([]), logger);
            var repairs = repairer.FindAsync(game, CancellationToken.None).GetAwaiter().GetResult();

            ChecksContext.Equal("找到一条修复", 1, repairs.Count);
            ChecksContext.Equal("类型是改名", ModPathRepairKind.RenameDirectory, repairs[0].Kind);
            ChecksContext.Equal(
                "建议名是 mod_<id 前 8 位>",
                Path.Combine(modRoot, "mod_978102dc"),
                repairs[0].SuggestedPath);
            ChecksContext.Equal("原因是非 ASCII 路径", ModLoadRules.WarnNonAsciiPath, repairs[0].ReasonKey);

            var applied = repairer.ApplyAsync(game, repairs, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("应用成功", applied.IsSuccess);
            ChecksContext.Check("旧目录已经不在了", !fs.DirectoryExists(localDir));
            ChecksContext.Check("新目录存在", fs.DirectoryExists(Path.Combine(modRoot, "mod_978102dc")));

            var library = repository.GetModLibraryAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("库里的路径跟着改", Path.Combine(modRoot, "mod_978102dc"), library[0].DirPath);

            // 撞名：占位的 mod_978102dc 已存在时应该给 _2。
            var otherRoot = PlaysetChecks.CreateTempRoot(fs, "modpath-collision");
            try
            {
                var otherGame = MakeGameAt(fs, otherRoot, Victoria3, LoadFileFormat.ContentLoadV1_1);
                var otherLocal = Path.Combine(otherGame.ModDir, "更改虚空造物");
                fs.EnsureDirectory(otherLocal);
                fs.EnsureDirectory(Path.Combine(otherGame.ModDir, "mod_978102dc"));

                var otherRepository = CreateRepository(fs, otherRoot, logger, clock);
                var otherPlaySet = CreatePlaySet(otherRepository, otherGame);
                SaveMods(
                    otherRepository,
                    otherGame,
                    otherPlaySet,
                    MakeMod(otherLocal, null, string.Empty, true, steam: null, name: "虚空造物+货运红利", modId: LocalModId, source: ModOrigins.LocalSource));

                var otherRepairer = new ModPathRepairer(fs, otherRepository, new FakeModCatalog([]), logger);
                var otherRepairs = otherRepairer.FindAsync(otherGame, CancellationToken.None).GetAwaiter().GetResult();
                ChecksContext.Equal("撞名时给 _2", Path.Combine(otherGame.ModDir, "mod_978102dc_2"), otherRepairs[0].SuggestedPath);
            }
            finally
            {
                PlaysetChecks.TryDelete(fs, otherRoot);
            }
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>父目录带中文（用户名）时救不了，只出提示、不给建议值。</summary>
    private static void CheckRepairNonAsciiParent(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[ModPathChecks] 修复：父目录非 ASCII 只能提示");
        var root = PlaysetChecks.CreateTempRoot(fs, "modpath-parent");
        try
        {
            var game = MakeGameAt(fs, root, Victoria3, LoadFileFormat.ContentLoadV1_1);
            // 造一个「中文用户名」的目录链：root\张三\Documents\Victoria 3\mod\ascii_mod
            var localDir = Path.Combine(root, "张三", "Documents", "Victoria 3", "mod", "ascii_mod");
            fs.EnsureDirectory(localDir);

            var repository = CreateRepository(fs, root, logger, clock);
            var playSetId = CreatePlaySet(repository, game);
            SaveMods(
                repository,
                game,
                playSetId,
                MakeMod(localDir, null, string.Empty, true, steam: null, name: "ascii mod", modId: LocalModId, source: ModOrigins.LocalSource));

            var repairer = new ModPathRepairer(fs, repository, new FakeModCatalog([]), logger);
            var repairs = repairer.FindAsync(game, CancellationToken.None).GetAwaiter().GetResult();

            ChecksContext.Equal("找到一条提示", 1, repairs.Count);
            ChecksContext.Equal("原因指向父目录", ModLoadRules.WarnNonAsciiParent, repairs[0].ReasonKey);
            ChecksContext.Equal("不给建议值（不能替用户改用户名）", null, repairs[0].SuggestedPath);

            var applied = repairer.ApplyAsync(game, repairs, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("没有可做的事也算成功", applied.IsSuccess);
            ChecksContext.Equal("修了 0 条", 0, applied.Value);
            ChecksContext.Check("目录原封不动", fs.DirectoryExists(localDir));
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>Core 返回的文案键必须在 zh-CN / en-US 两侧都存在（否则界面上是空白）。</summary>
    private static void CheckKeysExist()
    {
        Console.WriteLine("[ModPathChecks] 文案键存在性");
        (string File, string Key)[] entries =
        [
            ("common.json", ModOrigins.LocalKey),
            ("common.json", ModOrigins.SubscribedKey),
            ("mods.json", ModLoadRules.WarnNoPath),
            ("mods.json", ModLoadRules.WarnNoRegistryId),
            ("mods.json", ModLoadRules.WarnNoArchive),
            ("mods.json", ModLoadRules.WarnMissingPath),
            ("mods.json", ModLoadRules.WarnNonAsciiPath),
            ("mods.json", ModLoadRules.WarnNonAsciiParent),
            ("mods.json", GamePathRules.DescribeAsciiRuleKey(Victoria3)!),
            // 「添加模组」子页用到的文案键（页面与 ViewModel 都在 App 层，这里只兜底「键不存在」）。
            ("mods.json", "mods.addPage.add"),
            ("mods.json", "mods.addPage.back"),
            ("mods.json", "mods.addPage.candidateCount"),
            ("mods.json", "mods.addPage.empty"),
            ("mods.json", "mods.addPage.hint"),
            ("mods.json", "mods.addPage.listName"),
            ("mods.json", "mods.addPage.noMatch"),
            ("mods.json", "mods.addPage.searchName"),
            ("mods.json", "mods.addPage.searchPlaceholder"),
            ("mods.json", "mods.addPage.selectedCount"),
            ("mods.json", "mods.addPage.target"),
            ("mods.json", "mods.addPage.targetMissing"),
            ("mods.json", "mods.addPage.title"),
            ("mods.json", "mods.addPage.version"),
        ];

        string[] languages = ["zh-CN", "en-US"];
        foreach (var language in languages)
        {
            var path = I18nResw.ResourcesPath(I18nResw.RepoRoot(), language);
            if (!File.Exists(path))
            {
                ChecksContext.Check("文案表存在：" + language, false, path);
                continue;
            }

            var table = I18nResw.Read(I18nResw.RepoRoot(), language);
            foreach (var (_, key) in entries)
            {
                var found = I18nResw.Get(table, key) is not null;
                ChecksContext.Check(language + " 有键 " + key, found, path);
            }
        }
    }

    private static string RepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tools")))
            {
                return Path.Combine(dir.FullName, relative);
            }

            dir = dir.Parent;
        }

        return relative;
    }

    private static OwnDatabasePlaySetRepository CreateRepository(
        IFileSystem fs,
        string root,
        NullLogger logger,
        IClock clock)
    {
        var database = new PdxDatabase(fs, logger, Path.Combine(root, "own.sqlite"));
        return new OwnDatabasePlaySetRepository(database, logger, clock);
    }

    private static Guid CreatePlaySet(OwnDatabasePlaySetRepository repository, GameInstallation game) =>
        repository.CreatePlaySetAsync(game, "自检播放集", CancellationToken.None).GetAwaiter().GetResult().Value;

    private static void SaveMods(
        OwnDatabasePlaySetRepository repository,
        GameInstallation game,
        Guid playSetId,
        params PlaySetMod[] mods) =>
        repository.SavePlaySetModsAsync(game, playSetId, mods, CancellationToken.None).GetAwaiter().GetResult();

    private static GameInstallation MakeGameAt(IFileSystem fs, string root, string gameId, LoadFileFormat format)
    {
        var gameDataDir = Path.Combine(root, "documents", gameId);
        fs.EnsureDirectory(Path.Combine(gameDataDir, "mod"));
        fs.EnsureDirectory(Path.Combine(root, "install", gameId));
        return PlaysetChecks.CreateGame(
            gameId,
            Path.Combine(root, "install", gameId),
            format,
            gameDataDir,
            Path.Combine(root, "install", gameId, "game", "dlc"),
            Path.Combine(gameDataDir, "mod"),
            steamAppId: 529340);
    }

    private static GameInstallation MakeGame(string gameId, LoadFileFormat format) =>
        new(
            gameId,
            gameId,
            "1.0",
            @"C:\install",
            @"C:\install\launcher\launcher-settings.json",
            format,
            @"C:\documents",
            @"C:\install\game\dlc",
            @"C:\documents\mod",
            @"C:\install\bin.exe",
            [],
            [],
            null!,
            null,
            null,
            null,
            GameSupport.Full);

    private static PlaySetMod MakeMod(
        string? dirPath,
        string? archivePath,
        string? registryId,
        bool enabled,
        string? steam = null,
        string name = "某个 mod",
        Guid? modId = null,
        string? source = null)
        => new(
            ModId: modId ?? Guid.NewGuid(),
            Name: name,
            DirPath: dirPath ?? string.Empty,
            ArchivePath: archivePath,
            GameRegistryId: registryId ?? string.Empty,
            ThumbnailPath: null,
            RequiredVersion: string.Empty,
            Status: "ready_to_play",
            Enabled: enabled,
            Position: 0,
            SteamId: steam,
            Source: source);

    private sealed class FakeModCatalog(IReadOnlyList<PlaySetMod> mods) : IModCatalog
    {
        public Task<IReadOnlyList<PlaySetMod>> DiscoverAllAsync(GameInstallation game, CancellationToken cancellationToken) =>
            Task.FromResult(mods);
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
