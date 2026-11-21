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

    private static GameInstallation MakeGame(string dataDir) =>
        new(
            GameId: "ck3",
            DisplayName: "Crusader Kings III",
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
