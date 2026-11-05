namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Achievements;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 启动预热：写快照、切游戏去抖只写最后一款、失败只记日志、非 Steam 游戏不浪费一次 Init。
/// <para>
/// 预热最容易出的错是「什么都没发生」却没人发现：快照没写、写了旧游戏的、或者异常把
/// 后台任务炸掉。这些都不会崩，只会让用户点开成就页时又等 2.9 秒，所以逐条钉住。
/// </para>
/// </summary>
public static class AchievementPrewarmChecks
{
    public static int Run(IFileSystem fileSystem)
    {
        CheckWritesSnapshot(fileSystem);
        CheckSwitchDebounceKeepsLastGame(fileSystem);
        CheckFailureOnlyLogs(fileSystem);
        CheckNonSteamGameIsSkipped(fileSystem);
        CheckUnavailableSnapshotIsNotSaved(fileSystem);
        return 0;
    }

    private static void CheckWritesSnapshot(IFileSystem fileSystem)
    {
        var root = NewDirectory();
        var cache = NewCache(fileSystem, root);
        var game = NewGame("ck3", 730);
        var source = new FakeSource(_ => Snapshot(2));
        using var service = new AchievementPrewarmService(
            source,
            cache,
            () => game,
            new SilentLogger(),
            switchDebounce: TimeSpan.FromMilliseconds(30),
            startupDelay: TimeSpan.FromMilliseconds(30));

        service.Start();
        service.WaitForIdleAsync().GetAwaiter().GetResult();

        var loaded = cache.TryLoad("730");
        ChecksContext.Equal("预热：成功写回一次快照", 1, service.PrewarmCount);
        ChecksContext.Equal("预热：缓存键是 appid", "730", service.LastCacheKey);
        ChecksContext.Check("预热：快照能读回", loaded is not null && loaded.Entries.Count == 2);
        ChecksContext.Equal("预热：只取了一次数", 1, source.Calls);
        Delete(root);
    }

    private static void CheckSwitchDebounceKeepsLastGame(IFileSystem fileSystem)
    {
        var root = NewDirectory();
        var cache = NewCache(fileSystem, root);
        var game = NewGame("ck3", 730);
        var source = new FakeSource(_ => Snapshot(1));
        using var service = new AchievementPrewarmService(
            source,
            cache,
            () => game,
            new SilentLogger(),
            switchDebounce: TimeSpan.FromMilliseconds(40),
            startupDelay: TimeSpan.FromSeconds(30));

        service.Start();
        game = NewGame("eu5", 236850);
        service.NotifyGameChanged();
        game = NewGame("vic3", 529340);
        service.NotifyGameChanged();
        service.WaitForIdleAsync().GetAwaiter().GetResult();

        ChecksContext.Equal("预热：连续切游戏只预热最后停下的那款", 1, service.PrewarmCount);
        ChecksContext.Equal("预热：最后写的是最后一款游戏", "529340", service.LastCacheKey);
        var stale = cache.PathFor("730");
        ChecksContext.Check("预热：中间游戏的缓存没有被写", stale is null || !File.Exists(stale));
        ChecksContext.Equal("预热：去抖期间没有提前取数", 1, source.Calls);
        Delete(root);
    }

    private static void CheckFailureOnlyLogs(IFileSystem fileSystem)
    {
        var root = NewDirectory();
        var cache = NewCache(fileSystem, root);
        var game = NewGame("ck3", 730);
        var source = new FakeSource(_ => throw new InvalidOperationException("steam 炸了"));
        using var service = new AchievementPrewarmService(
            source,
            cache,
            () => game,
            new SilentLogger(),
            switchDebounce: TimeSpan.FromMilliseconds(20),
            startupDelay: TimeSpan.FromMilliseconds(20));

        service.Start();
        service.WaitForIdleAsync().GetAwaiter().GetResult();

        ChecksContext.Equal("预热：数据源失败不写快照", 0, service.PrewarmCount);
        ChecksContext.Check("预热：失败被记下来（不向上抛）", service.LastError is { Length: > 0 });
        Delete(root);
    }

    private static void CheckNonSteamGameIsSkipped(IFileSystem fileSystem)
    {
        var root = NewDirectory();
        var cache = NewCache(fileSystem, root);
        var game = NewGame("pa", null);
        var source = new FakeSource(_ => Snapshot(1));
        using var service = new AchievementPrewarmService(
            source,
            cache,
            () => game,
            new SilentLogger(),
            switchDebounce: TimeSpan.FromMilliseconds(20),
            startupDelay: TimeSpan.FromMilliseconds(20));

        service.Start();
        service.WaitForIdleAsync().GetAwaiter().GetResult();

        ChecksContext.Equal("预热：非 Steam 游戏不发起取数", 0, source.Calls);
        ChecksContext.Equal("预热：非 Steam 游戏不写快照", 0, service.PrewarmCount);
        Delete(root);
    }

    private static void CheckUnavailableSnapshotIsNotSaved(IFileSystem fileSystem)
    {
        var root = NewDirectory();
        var cache = NewCache(fileSystem, root);
        var game = NewGame("ck3", 730);
        var source = new FakeSource(_ => new AchievementSnapshot
        {
            Availability = AchievementAvailability.SteamUnavailable,
            FetchedAtUtc = DateTimeOffset.UtcNow,
        });
        using var service = new AchievementPrewarmService(
            source,
            cache,
            () => game,
            new SilentLogger(),
            switchDebounce: TimeSpan.FromMilliseconds(20),
            startupDelay: TimeSpan.FromMilliseconds(20));

        service.Start();
        service.WaitForIdleAsync().GetAwaiter().GetResult();

        ChecksContext.Equal("预热：Steam 不可用不写快照", 0, service.PrewarmCount);
        ChecksContext.Check("预热：Steam 不可用不算失败", service.LastError is null);
        Delete(root);
    }

    private static AchievementSnapshot Snapshot(int count)
    {
        var entries = new List<AchievementEntry>(count);
        for (var i = 0; i < count; i++)
        {
            entries.Add(new AchievementEntry(
                "ACH_" + i,
                "成就" + i,
                "说明" + i,
                IsHidden: false,
                IsUnlocked: i == 0,
                UnlockTimeUtc: i == 0 ? DateTimeOffset.UnixEpoch : null,
                GlobalPercent: 10 + i,
                IconUrl: null));
        }

        return new AchievementSnapshot
        {
            Availability = AchievementAvailability.Available,
            Entries = entries,
            FetchedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static AchievementSnapshotCache NewCache(IFileSystem fileSystem, string root) =>
        new(fileSystem, Path.Combine(root, "achievement-cache"));

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pdxnext-prewarm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static GameInstallation NewGame(string gameId, int? steamAppId) =>
        new(
            GameId: gameId,
            DisplayName: gameId,
            Version: "test",
            InstallRoot: Path.Combine(Path.GetTempPath(), "pdxnext-game", gameId),
            LauncherSettingsPath: Path.Combine(Path.GetTempPath(), "pdxnext-game", gameId, "launcher-settings.json"),
            Format: LoadFileFormat.ContentLoadV1,
            GameDataDir: Path.Combine(Path.GetTempPath(), "pdxnext-game", gameId, "game"),
            DlcPath: Path.Combine(Path.GetTempPath(), "pdxnext-game", gameId, "dlc"),
            ModDir: Path.Combine(Path.GetTempPath(), "pdxnext-game", gameId, "mod"),
            ExePath: Path.Combine(Path.GetTempPath(), "pdxnext-game", gameId, "binaries", gameId + ".exe"),
            ExeArgs: [],
            Alternatives: [],
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
            SteamAppId: steamAppId,
            Support: GameSupport.Full);

    private sealed class FakeSource : IAchievementSource
    {
        private readonly Func<string, AchievementSnapshot> _factory;

        public FakeSource(Func<string, AchievementSnapshot> factory) => _factory = factory;

        public int Calls { get; private set; }

        public Task<AchievementSnapshot> GetAsync(string gameId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_factory(gameId));
        }
    }

    private sealed class SilentLogger : IAppLogger
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
