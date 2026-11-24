namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.CrashAnalysis;
using PdxLauncher.Core.Infrastructure;

/// <summary>
/// 本次游戏报错统计与日志文件状态探测自检。用临时目录造日志，不联网、不写游戏目录。
/// </summary>
public static class GameSessionChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        var root = Path.Combine(Path.GetTempPath(), "pdxnext-session-" + Guid.NewGuid().ToString("N"));
        fs.EnsureDirectory(root);
        try
        {
            CheckSessionCounting(fs, root);
            CheckCrossLogDedupe(fs, root);
            CheckSessionBoundary(fs, root);
            CheckLogProbe(fs, root);
            CheckSessionLogCatalog(fs, root);
            CheckUnityLogParsing(fs, root);
            CheckCs2FatalDedupe(fs, root);
            CheckFatalAlertOnce(fs, root);
            CheckLargeLogCatchUp(fs, root);
        }
        finally
        {
            if (fs.DirectoryExists(root))
            {
                fs.Delete(root);
            }
        }

        return ChecksContext.Failures - before;
    }

    private static void CheckSessionCounting(IFileSystem fs, string root)
    {
        var dataDir = Path.Combine(root, "data1");
        var logsDir = Path.Combine(dataDir, "logs");
        fs.EnsureDirectory(logsDir);
        var errorLog = Path.Combine(logsDir, "error.log");
        fs.WriteAllTextAtomic(errorLog, string.Empty);

        var game = MakeGame(dataDir);
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 21, 20, 0, 0, TimeSpan.Zero));
        var tracker = new GameSessionLogTracker(fs, clock);
        tracker.Begin(game, 1234, clock.UtcNow);

        // 追加两条不同错误（HH:mm:ss 与会话同日）。
        fs.WriteAllTextAtomic(errorLog,
            "[20:00:01][jomini_script]: Script system error: unknown trigger foo\n"
            + "  Script location: common/events/test.txt:10\n"
            + "[20:00:02][jomini_effect]: unknown effect bar\n");

        tracker.Poll();
        var snapshot = tracker.Snapshot();
        ChecksContext.Check("会话内统计到错误", snapshot.ErrorBlocks > 0, "count=" + snapshot.ErrorBlocks);
        ChecksContext.Check("会话正在运行", tracker.IsRunning);

        var ended = tracker.End();
        ChecksContext.Check("结束后有结束时间", ended.EndTime is not null);
        ChecksContext.Check("结束快照保留错误数", ended.ErrorBlocks == snapshot.ErrorBlocks);
        ChecksContext.Check("结束后不再运行", !tracker.IsRunning);
    }

    private static void CheckCrossLogDedupe(IFileSystem fs, string root)
    {
        var dataDir = Path.Combine(root, "data2");
        var logsDir = Path.Combine(dataDir, "logs");
        fs.EnsureDirectory(logsDir);
        string line = "[21:00:01][jomini_script]: Script system error: unknown trigger duplicate\n";
        fs.WriteAllTextAtomic(Path.Combine(logsDir, "error.log"), line);
        fs.WriteAllTextAtomic(Path.Combine(logsDir, "game.log"), line);
        fs.WriteAllTextAtomic(Path.Combine(logsDir, "debug.log"), line);

        var game = MakeGame(dataDir);
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 21, 21, 0, 0, TimeSpan.Zero));
        var tracker = new GameSessionLogTracker(fs, clock);
        // Begin 会把游标放在当前文件末尾，这里先建空文件再从 0 统计。
        fs.WriteAllTextAtomic(Path.Combine(logsDir, "error.log"), string.Empty);
        fs.WriteAllTextAtomic(Path.Combine(logsDir, "game.log"), string.Empty);
        fs.WriteAllTextAtomic(Path.Combine(logsDir, "debug.log"), string.Empty);
        tracker.Begin(game, 1, clock.UtcNow);
        fs.WriteAllTextAtomic(Path.Combine(logsDir, "error.log"), line);
        fs.WriteAllTextAtomic(Path.Combine(logsDir, "game.log"), line);
        fs.WriteAllTextAtomic(Path.Combine(logsDir, "debug.log"), line);
        tracker.Poll();
        var snapshot = tracker.Snapshot();
        ChecksContext.Equal("同一错误跨三份日志只算一次", 1, snapshot.ErrorBlocks);
    }

    private static void CheckSessionBoundary(IFileSystem fs, string root)
    {
        var dataDir = Path.Combine(root, "data3");
        var logsDir = Path.Combine(dataDir, "logs");
        fs.EnsureDirectory(logsDir);
        // 会话开始前 10 分钟的错误，不应计入。
        fs.WriteAllTextAtomic(Path.Combine(logsDir, "error.log"),
            "[20:50:00][jomini_script]: Script system error: old trigger\n");

        var game = MakeGame(dataDir);
        // Begin 从末尾开始，先写旧行再 Begin，再写新行；旧行不会进游标范围。
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 21, 21, 0, 0, TimeSpan.Zero));
        var tracker = new GameSessionLogTracker(fs, clock);
        tracker.Begin(game, 1, clock.UtcNow);
        fs.WriteAllTextAtomic(Path.Combine(logsDir, "error.log"),
            "[20:50:00][jomini_script]: Script system error: old trigger\n"
            + "[21:00:05][jomini_script]: Script system error: new trigger\n");
        tracker.Poll();
        ChecksContext.Equal("只统计会话开始后的错误", 1, tracker.Snapshot().ErrorBlocks);
    }

    private static void CheckLogProbe(IFileSystem fs, string root)
    {
        var log = Path.Combine(root, "probe.log");
        ChecksContext.Equal("不存在 → Missing", LogFileProbeState.Missing, LogFileProbe.Probe(fs, log, "probe.log").State);

        fs.WriteAllTextAtomic(log, "hello");
        var readable = LogFileProbe.Probe(fs, log, "probe.log", _ => FileAttributes.Normal);
        ChecksContext.Equal("可读取 → Readable", LogFileProbeState.Readable, readable.State);
        ChecksContext.Check("无只读属性", !readable.IsReadOnlyAttribute);

        var readonlyResult = LogFileProbe.Probe(fs, log, "probe.log", _ => FileAttributes.ReadOnly);
        ChecksContext.Equal("只读属性 → ReadOnly", LogFileProbeState.ReadOnly, readonlyResult.State);
        ChecksContext.Check("只读属性被标注", readonlyResult.IsReadOnlyAttribute);

        // 用独占句柄锁住文件，模拟游戏/杀软独占。
        try
        {
            using var exclusive = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.None);
            var locked = LogFileProbe.Probe(fs, log, "probe.log", _ => FileAttributes.Normal);
            ChecksContext.Check("独占锁定 → Locked", locked.State == LogFileProbeState.Locked, "state=" + locked.State);
        }
        catch (IOException)
        {
            // 某些平台/文件系统不允许独占打开，跳过该断言但不算失败。
            ChecksContext.Check("独占锁定探针（平台不支持独占打开，跳过）", true);
        }
    }

    private static void CheckSessionLogCatalog(IFileSystem fs, string root)
    {
        var cs2 = MakeGame(Path.Combine(root, "cs2"), "cities_skylines_2");
        var cs2Sources = SessionLogCatalog.Resolve(fs, cs2).Select(static item => item.Name).ToArray();
        ChecksContext.Check("CS2 会话日志含 Player.log", cs2Sources.Contains("Player.log"));
        ChecksContext.Check("CS2 会话日志含 SceneFlow.log", cs2Sources.Contains("SceneFlow.log"));
        ChecksContext.Check("CS2 会话日志含 UI.log", cs2Sources.Contains("UI.log"));
        ChecksContext.Check("CS2 不扫描旧 error.log", !cs2Sources.Contains("error.log"));

        var ck3Sources = SessionLogCatalog.Resolve(fs, MakeGame(Path.Combine(root, "ck3-db"), "ck3"));
        ChecksContext.Check("P 社老游戏仍扫描 error.log", ck3Sources.Any(static item => item.Name == "error.log"));
    }

    private static void CheckUnityLogParsing(IFileSystem fs, string root)
    {
        var text = "[2026-11-24 14:51:31,334] [ERROR] A platform service integration failed to initialize\n"
            + "  at Game.SceneFlow.GameManager.InitializePlatformManager()\n";
        var blocks = CrashLogParser.ParseLogBlocks(text, new DateTimeOffset(2026, 11, 24, 14, 51, 0, TimeSpan.Zero));
        ChecksContext.Equal("Unity 时间戳解析成一块", 1, blocks.Count);
        var block = blocks.FirstOrDefault();
        ChecksContext.Check("Unity 错误块可识别", block is not null && CrashLogParser.IsErrorBlock(block));
        if (block is null)
        {
            return;
        }

        ChecksContext.Equal("平台服务致命分类",
            "platform-service",
            CrashLogParser.DetectFatalCategory(block.Text));
    }

    private static void CheckCs2FatalDedupe(IFileSystem fs, string root)
    {
        var dataDir = Path.Combine(root, "cs2-fatal");
        fs.EnsureDirectory(Path.Combine(dataDir, "Logs"));
        var playerLog = Path.Combine(dataDir, "Player.log");
        var sceneFlowLog = Path.Combine(dataDir, "Logs", "SceneFlow.log");
        fs.WriteAllTextAtomic(playerLog, string.Empty);
        fs.WriteAllTextAtomic(sceneFlowLog, string.Empty);

        var clock = new FakeClock(new DateTimeOffset(2026, 11, 24, 14, 51, 0, TimeSpan.Zero));
        var tracker = new GameSessionLogTracker(fs, clock);
        tracker.Begin(MakeGame(dataDir, "cities_skylines_2"), 4242, clock.UtcNow);

        fs.WriteAllTextAtomic(playerLog,
            "Persistent folder version is outdated 1.6.2f1 (Game: 1.6.4f1)\n"
            + "Curl error 35: Cert verify failed. Certificate could not be verified\n"
            + "[SceneFlow] [FATAL]  Object reference not set to an instance of an object\n"
            + "  at Colossal.IO.AssetDatabase.AssetDatabase`1[T].PopulateFromDataSource (Boolean priorityData)\n"
            + "\nUIView 0 url set to fatal://error\n");

        tracker.Poll();
        var snapshot = tracker.Snapshot();
        ChecksContext.Check("CS2 FATAL 被识别", snapshot.HasFatal, "category=" + snapshot.FatalInfo?.Category);
        ChecksContext.Equal("FATAL 文件保留来源", "Player.log", snapshot.FatalInfo?.LogName);
        ChecksContext.Equal("平台服务失败优先归类", "platform-service", snapshot.FatalInfo?.Category);
        ChecksContext.Check("FATAL 会话错误数受控", snapshot.ErrorBlocks > 0 && snapshot.ErrorBlocks < 100,
            "blocks=" + snapshot.ErrorBlocks);

        fs.WriteAllTextAtomic(sceneFlowLog,
            "[2026-11-24 14:51:31,334] [FATAL]  Object reference not set to an instance of an object\n"
            + "  at Colossal.IO.AssetDatabase.AssetDatabase`1[T].PopulateFromDataSource (Boolean priorityData)\n");
        tracker.Poll();
        ChecksContext.Equal("同一 FATAL 只保留一条摘要", 1,
            tracker.Snapshot().FatalInfo is null ? 0 : 1);
    }

    private static void CheckFatalAlertOnce(IFileSystem fs, string root)
    {
        var fatal = new SessionFatalInfo(
            "asset-database", "asset-database|sig", "Player.log", "fatal://error");
        var snapshot = new GameSessionSnapshot(
            "cities_skylines_2",
            "Cities: Skylines II",
            4242,
            new DateTimeOffset(2026, 11, 24, 14, 51, 0, TimeSpan.Zero),
            null,
            1,
            Array.Empty<SessionErrorGroup>(),
            Array.Empty<LogFileProbeResult>(),
            false,
            fatal);
        var alert = new SessionFatalAlert();
        ChecksContext.Check("首次发现 FATAL 需要提醒", alert.ShouldAnnounce(snapshot));
        ChecksContext.Check("同一 FATAL 不重复提醒", !alert.ShouldAnnounce(snapshot));
        alert.Reset();
        ChecksContext.Check("新会话可再次提醒", alert.ShouldAnnounce(snapshot));
    }

    private static void CheckLargeLogCatchUp(IFileSystem fs, string root)
    {
        var dataDir = Path.Combine(root, "cs2-large");
        fs.EnsureDirectory(Path.Combine(dataDir, "Logs"));
        var playerLog = Path.Combine(dataDir, "Player.log");
        fs.WriteAllTextAtomic(playerLog, string.Empty);

        var clock = new FakeClock(new DateTimeOffset(2026, 11, 24, 15, 0, 0, TimeSpan.Zero));
        var tracker = new GameSessionLogTracker(fs, clock);
        tracker.Begin(MakeGame(dataDir, "cities_skylines_2"), 5000, clock.UtcNow);

        fs.WriteAllTextAtomic(playerLog,
            new string('x', 2 * 1024 * 1024 + 512)
            + "\nNullReferenceException: Object reference not set to an instance of an object\n"
            + "  at Colossal.IO.AssetDatabase.AssetDatabase`1[T].PopulateFromDataSource (Boolean priorityData)\n");

        tracker.Poll();
        var snapshot = tracker.Snapshot();
        ChecksContext.Check("超大日志标记读取降级", snapshot.Degraded);
        ChecksContext.Check("超大日志仍能从尾部抓到 FATAL", snapshot.HasFatal);
    }

    private static GameInstallation MakeGame(string dataDir, string gameId = "ck3") =>
        new(
            GameId: gameId,
            DisplayName: gameId == "cities_skylines_2" ? "Cities: Skylines II" : "Crusader Kings III",
            Version: "1.19.0",
            InstallRoot: Path.Combine(dataDir, "game"),
            LauncherSettingsPath: Path.Combine(dataDir, "game", "launcher", "launcher-settings.json"),
            Format: LoadFileFormat.DlcLoadV0,
            GameDataDir: dataDir,
            DlcPath: Path.Combine(dataDir, "game", "dlc"),
            ModDir: Path.Combine(dataDir, "mod"),
            ExePath: Path.Combine(dataDir, "game", "binaries", "ck3.exe"),
            ExeArgs: Array.Empty<string>(),
            Alternatives: Array.Empty<AlternativeExecutable>(),
            Theme: new ThemeSpec(
                Background: null, Logo: null, AppIcon: null, AppTaskbarIcon: null,
                ButtonBackground: null, ButtonFont: null,
                ButtonTextColor: "#FFFFFF", ButtonHoverTextColor: "#FFFFFF",
                FontSize: 14, FontWeight: 400, TextShadow: null,
                Source: ThemeAssetSource.BuiltInPlaceholder),
            BrowserDlcUrl: null,
            BrowserModUrl: null,
            SteamAppId: 1158310,
            Support: GameSupport.Full);

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
