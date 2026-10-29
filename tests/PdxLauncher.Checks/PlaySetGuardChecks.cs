namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Dlc;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.LoadFiles;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Playsets.Json;
using PdxLauncher.Core.Playsets.PlaysetConfig;

/// <summary>
/// 官方启动器改动防线的自检：
/// <list type="number">
/// <item>指纹只看 ModId + 启用态 + 顺序；名称、缩略图、版本这些派生字段不参与，否则每次官方刷新元数据都会误判成改动。</item>
/// <item>库内现状被外部改写时 <see cref="PlaySetEditor.HasPlaySetDriftedAsync"/> 必须判出改动，
/// 让保存流程放弃写入并重新加载——写库是「DELETE 全表 + 逐行 INSERT」，旧内存落盘会把官方改动整份盖掉。</item>
/// <item>官方换存储格式（launcher-v2.sqlite 消失、JSON 文档在）时 <see cref="PlaySetRepositoryRouter"/>
/// 必须兜底到对应实现，而不是让播放集页静默空白。</item>
/// </list>
/// </summary>
public static class PlaySetGuardChecks
{
    private static readonly Guid ModA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ModB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ModC = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PlaySetId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        var logger = new NullLogger();
        var clock = SystemClock.Instance;

        CheckFingerprint();
        CheckDriftDetection(fs, logger, clock);
        CheckRouterFallback(fs, logger, clock);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 指纹
    // ------------------------------------------------------------------

    private static void CheckFingerprint()
    {
        Console.WriteLine("[PlaySetGuardChecks] 播放集指纹");

        var baseline = PlaySetEditor.DescribePlaySetMods(new[]
        {
            MakeMod(ModA, "A", enabled: true, position: 0),
            MakeMod(ModB, "B", enabled: false, position: 1),
        });

        // 名称 / 缩略图 / 版本来自 mods 表与磁盘扫描，官方刷新元数据时会变；它们一变就判「有人在改播放集」是误报。
        var renamed = PlaySetEditor.DescribePlaySetMods(new[]
        {
            MakeMod(ModA, "改名后的 A", enabled: true, position: 0, thumbnail: @"C:\cache\a.png", version: "9.9.*"),
            MakeMod(ModB, "B2", enabled: false, position: 1, status: "failed"),
        });
        ChecksContext.Equal("指纹忽略名称/缩略图/版本/状态等派生字段", baseline, renamed);

        var enableChanged = PlaySetEditor.DescribePlaySetMods(new[]
        {
            MakeMod(ModA, "A", enabled: false, position: 0),
            MakeMod(ModB, "B", enabled: false, position: 1),
        });
        ChecksContext.Check("启用态变化改变指纹", !string.Equals(baseline, enableChanged, StringComparison.Ordinal));

        var orderChanged = PlaySetEditor.DescribePlaySetMods(new[]
        {
            MakeMod(ModB, "B", enabled: false, position: 0),
            MakeMod(ModA, "A", enabled: true, position: 1),
        });
        ChecksContext.Check("顺序变化改变指纹", !string.Equals(baseline, orderChanged, StringComparison.Ordinal));

        var identityChanged = PlaySetEditor.DescribePlaySetMods(new[]
        {
            MakeMod(ModC, "A", enabled: true, position: 0),
            MakeMod(ModB, "B", enabled: false, position: 1),
        });
        ChecksContext.Check("ModId 变化改变指纹", !string.Equals(baseline, identityChanged, StringComparison.Ordinal));

        // Position 可能乱序传入（仓储按 position 排序返回，调用方也可能先排过）：指纹必须先按 position 归一。
        var shuffled = PlaySetEditor.DescribePlaySetMods(new[]
        {
            MakeMod(ModB, "B", enabled: false, position: 1),
            MakeMod(ModA, "A", enabled: true, position: 0),
        });
        ChecksContext.Equal("指纹按 position 归一（传入顺序无关）", baseline, shuffled);
        ChecksContext.Equal("空列表的指纹为空串", string.Empty, PlaySetEditor.DescribePlaySetMods(Array.Empty<PlaySetMod>()));
    }

    // ------------------------------------------------------------------
    // 保存前的漂移检测
    // ------------------------------------------------------------------

    private static void CheckDriftDetection(IFileSystem fs, IAppLogger logger, IClock clock)
    {
        Console.WriteLine("[PlaySetGuardChecks] 保存前漂移检测");

        var root = CreateTempRoot(fs, "drift");
        try
        {
            var game = CreateGame("guardcheck", root);
            fs.EnsureDirectory(game.GameDataDir);

            var repository = new PlaySetRepository(fs, logger, clock);
            var editor = new PlaySetEditor(repository, new LoadFileWriter(fs, logger), logger);

            var emptyFingerprint = PlaySetEditor.DescribePlaySetMods(Array.Empty<PlaySetMod>());
            var oneModFingerprint = PlaySetEditor.DescribePlaySetMods(new[]
            {
                MakeMod(ModA, "A", enabled: true, position: 0),
            });

            // 库不存在：仓储按「空结果 + Warn」返回，不抛。
            ChecksContext.Check(
                "库不存在 + 空基线 → 不算改动",
                !HasDrifted(editor, game, emptyFingerprint));

            ChecksContext.Check(
                "库读到空 + 基线非空 → 判为改动（保存会被拦下，不用旧内存覆盖）",
                HasDrifted(editor, game, oneModFingerprint));

            // 坏库（不是 sqlite 文件）：仓储记 Warn 后返回空，比对照样给出「改动」而不是把异常抛到 UI。
            fs.WriteAllBytesAtomic(PlaySetRepository.GetDatabasePath(game), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
            ChecksContext.Check(
                "坏库不抛且判为改动（宁可放弃本次保存也不覆盖官方数据）",
                HasDrifted(editor, game, oneModFingerprint));
            ChecksContext.Check(
                "坏库 + 空基线 → 不算改动（说明走的是「读到空」而不是抛异常路径）",
                !HasDrifted(editor, game, emptyFingerprint));
        }
        finally
        {
            TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 格式兜底路由
    // ------------------------------------------------------------------

    private static void CheckRouterFallback(IFileSystem fs, IAppLogger logger, IClock clock)
    {
        Console.WriteLine("[PlaySetGuardChecks] 播放集格式兜底路由");

        var root = CreateTempRoot(fs, "router");
        try
        {
            var sqlite = new PlaySetRepository(fs, logger, clock);
            var json = new PlaysetsJsonRepository(fs, logger, new ModCatalog(fs, logger), new DlcCatalog(fs, logger));
            var config = new PlaysetConfigRepository(fs, logger, clock);
            var router = new PlaySetRepositoryRouter(sqlite, json, config, fs, logger);

            // ① 数据库在 → sqlite；即使同目录还有 playsets.json 也不改判（「空库」不等于「换格式」）。
            var withDb = CreateGame("routerdb", Path.Combine(root, "db"));
            fs.EnsureDirectory(withDb.GameDataDir);
            fs.WriteAllBytesAtomic(PlaySetRepository.GetDatabasePath(withDb), new byte[] { 1, 2, 3 });
            fs.WriteAllTextAtomic(Path.Combine(withDb.GameDataDir, PlaysetsJsonDocument.FileName), "{}");
            ChecksContext.Check("数据库存在 → 仍走 sqlite", ReferenceEquals(sqlite, router.For(withDb)));

            // ② 官方把 sqlite 迁成 playsets.json：数据库消失、文档在。
            var jsonGame = CreateGame("routerjson", Path.Combine(root, "json"));
            fs.EnsureDirectory(jsonGame.GameDataDir);
            fs.WriteAllTextAtomic(Path.Combine(jsonGame.GameDataDir, PlaysetsJsonDocument.FileName), "{}");
            ChecksContext.Check(
                "数据库消失 + 有 playsets.json → 兜底 playsets.json",
                ReferenceEquals(json, router.For(jsonGame)));

            // ③ 迁成 CS2 那套 playset_config.json。
            var configGame = CreateGame("routercfg", Path.Combine(root, "cfg"));
            fs.EnsureDirectory(PlaysetConfigPaths.GetDirectory(configGame));
            fs.WriteAllTextAtomic(PlaysetConfigPaths.GetConfigPath(configGame), "{}");
            ChecksContext.Check(
                "数据库消失 + 有 playset_config.json → 兜底 playset_config.json",
                ReferenceEquals(config, router.For(configGame)));

            // ④ 三样都没有 → 仍走 sqlite（读空 + Warn），不乱猜格式。
            var noneGame = CreateGame("routernone", Path.Combine(root, "none"));
            fs.EnsureDirectory(noneGame.GameDataDir);
            ChecksContext.Check("数据库与 JSON 都不存在 → 仍走 sqlite", ReferenceEquals(sqlite, router.For(noneGame)));

            // ⑤ 显式声明的格式不受兜底影响。
            var eu5 = CreateGame("routereu5", Path.Combine(root, "eu5"), LoadFileFormat.PlaysetsJsonV1);
            ChecksContext.Check("Format=PlaysetsJsonV1 → playsets.json", ReferenceEquals(json, router.For(eu5)));
            var cs2 = CreateGame("routercs2", Path.Combine(root, "cs2"), LoadFileFormat.PlaysetConfigV1);
            ChecksContext.Check("Format=PlaysetConfigV1 → playset_config.json", ReferenceEquals(config, router.For(cs2)));
        }
        finally
        {
            TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 夹具
    // ------------------------------------------------------------------

    private static bool HasDrifted(PlaySetEditor editor, GameInstallation game, string fingerprint) =>
        editor.HasPlaySetDriftedAsync(game, PlaySetId, fingerprint, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    private static PlaySetMod MakeMod(
        Guid modId,
        string name,
        bool enabled,
        int position,
        string? thumbnail = null,
        string version = "1.13.*",
        string status = "ready_to_play") =>
        new(
            ModId: modId,
            Name: name,
            DirPath: "/fake/" + name,
            ArchivePath: null,
            GameRegistryId: "mod/" + name + ".mod",
            ThumbnailPath: thumbnail,
            RequiredVersion: version,
            Status: status,
            Enabled: enabled,
            Position: position);

    private static GameInstallation CreateGame(
        string gameId,
        string installRoot,
        LoadFileFormat format = LoadFileFormat.ContentLoadV1_1) =>
        new(
            GameId: gameId,
            DisplayName: gameId,
            Version: "test",
            InstallRoot: installRoot,
            LauncherSettingsPath: Path.Combine(installRoot, "launcher-settings.json"),
            Format: format,
            GameDataDir: Path.Combine(installRoot, "data"),
            DlcPath: Path.Combine(installRoot, "game", "dlc"),
            ModDir: Path.Combine(installRoot, "data", "mod"),
            ExePath: Path.Combine(installRoot, "binaries", gameId + ".exe"),
            ExeArgs: Array.Empty<string>(),
            Alternatives: Array.Empty<AlternativeExecutable>(),
            Theme: new ThemeSpec(
                Background: null,
                Logo: null,
                AppIcon: null,
                AppTaskbarIcon: null,
                ButtonBackground: null,
                ButtonFont: null,
                ButtonTextColor: "#FFFFFF",
                ButtonHoverTextColor: "#FFFFFF",
                FontSize: 14,
                FontWeight: 400,
                TextShadow: null,
                Source: ThemeAssetSource.BuiltInPlaceholder),
            BrowserDlcUrl: null,
            BrowserModUrl: null,
            SteamAppId: null,
            Support: GameSupport.Full);

    private static string CreateTempRoot(IFileSystem fs, string label)
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "pdxnext-playsetguard-" + label + "-" + Guid.NewGuid().ToString("N"));
        fs.EnsureDirectory(tempRoot);
        return tempRoot;
    }

    private static void TryDelete(IFileSystem fs, string path)
    {
        try
        {
            if (fs.DirectoryExists(path))
            {
                fs.Delete(path);
            }
        }
        catch (IOException)
        {
            // 自检不因为清不掉临时目录而失败。
        }
    }

    /// <summary>自检不关心日志内容：记 Warn 的用例只验证「不抛、给出预期结果」。</summary>
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
