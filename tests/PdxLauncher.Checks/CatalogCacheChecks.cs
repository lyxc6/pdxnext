namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Dlc;
using PdxLauncher.Core.Launching;

/// <summary>
/// 「切换游戏很慢」修复的两条关键契约自检：
/// <list type="number">
/// <item><see cref="DlcCatalog"/> 的扫描结果按 TTL 缓存，同一游戏重复扫描只遍历一次目录，
/// <see cref="DlcCatalog.InvalidateCache"/> 之后能立刻看到新增的 DLC。</item>
/// <item><see cref="ContinueGameReader.ReadAsync"/> 的文件 IO 不在调用线程上跑（原先全是同步 IO，
/// 直接从 UI 线程读盘）。</item>
/// </list>
/// </summary>
public static class CatalogCacheChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckDlcCatalogCaching(fs);
        CheckContinueGameReaderOffThread(fs);

        return ChecksContext.Failures - before;
    }

    // ------------------------------------------------------------------ DLC 扫描缓存

    private static void CheckDlcCatalogCaching(IFileSystem fs)
    {
        Console.WriteLine("[CatalogCacheChecks] DLC 扫描缓存");

        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-catalog-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dlcDir = Path.Combine(tempRoot, "game", "dlc");
            fs.EnsureDirectory(dlcDir);

            // 放一份真实的 CK3 .dlc 夹具。
            var fixture = ChecksContext.FixturePath("ck3/dlc001.dlc");
            fs.WriteAllBytesAtomic(Path.Combine(dlcDir, "dlc001.dlc"), fs.ReadAllBytes(fixture));

            var logger = new RecordingLogger();
            var clock = new FakeClock();
            var catalog = new DlcCatalog(fs, logger, clock);
            var game = BuildGame("ck3", tempRoot, dlcDir);

            var first = catalog.DiscoverAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("首次扫描解析出 1 个 DLC", 1, first.Count);
            ChecksContext.Equal("首次扫描记了 1 次「DLC 扫描完成」", 1, logger.CountOf("DLC 扫描完成"));

            // 再扫一次（模拟主页 + DLC 页各扫一遍）：命中缓存，不再遍历目录。
            var second = catalog.DiscoverAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("第二次扫描仍返回 1 个", 1, second.Count);
            ChecksContext.Equal("TTL 内不重复扫描（日志仍 1 次）", 1, logger.CountOf("DLC 扫描完成"));
            ChecksContext.Check("两次扫描返回同一份结果实例", ReferenceEquals(first, second));

            // 模拟用户装了一个 DLC：TTL 内不该看到（这是缓存的代价，靠显式刷新弥补）。
            var secondFixtureRoot = Path.Combine(dlcDir, "dlc002");
            fs.EnsureDirectory(secondFixtureRoot);
            fs.WriteAllBytesAtomic(
                Path.Combine(secondFixtureRoot, "dlc002.dlc"),
                fs.ReadAllBytes(ChecksContext.FixturePath("stellaris/dlc001.dlc")));

            var stale = catalog.DiscoverAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("TTL 内看不到新装的 DLC", 1, stale.Count);

            // 用户点刷新 → InvalidateCache → 立刻重扫。
            catalog.InvalidateCache(game);
            var refreshed = catalog.DiscoverAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("失效后看得到新装的 DLC", 2, refreshed.Count);
            ChecksContext.Equal("失效后重扫记了第 2 次「DLC 扫描完成」", 2, logger.CountOf("DLC 扫描完成"));

            // TTL 过期后自动重扫（无需显式失效）。
            clock.Advance(TimeSpan.FromSeconds(61));
            var expired = catalog.DiscoverAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("TTL 过期后仍返回 2 个", 2, expired.Count);
            ChecksContext.Equal("TTL 过期触发第 3 次扫描", 3, logger.CountOf("DLC 扫描完成"));

            // ClearCache 之后同样重扫。
            catalog.ClearCache();
            catalog.DiscoverAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("ClearCache 后重扫（第 4 次）", 4, logger.CountOf("DLC 扫描完成"));

            // 目录不存在时缓存空结果，不该每次切换都去 Warn + 空扫。
            var goneGame = BuildGame("ck3", tempRoot, Path.Combine(tempRoot, "no-such-dlc"));
            var beforeMiss = logger.CountOf("DLC 目录不存在");
            catalog.DiscoverAsync(goneGame, CancellationToken.None).GetAwaiter().GetResult();
            catalog.DiscoverAsync(goneGame, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("缺失目录的空结果也被缓存（只 Warn 1 次）", beforeMiss + 1, logger.CountOf("DLC 目录不存在"));
        }
        finally
        {
            Cleanup(fs, tempRoot);
        }
    }

    // ------------------------------------------------------------------ ContinueGameReader 线程

    private static void CheckContinueGameReaderOffThread(IFileSystem fs)
    {
        Console.WriteLine("[CatalogCacheChecks] ContinueGameReader 不在调用线程读盘");

        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-continue-thread-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dataDir = Path.Combine(tempRoot, "data");
            fs.EnsureDirectory(dataDir);
            fs.WriteAllTextAtomic(
                Path.Combine(dataDir, "continue_game.json"),
                "{\"title\":\"测试存档\",\"desc\":\"\",\"gameVersion\":\"1.20.1.2\",\"date\":\"1444.11.11\"}");

            var recorder = new ThreadRecordingFileSystem(fs);
            var reader = new ContinueGameReader(recorder, new RecordingLogger());
            var game = BuildGame("ck3", tempRoot, Path.Combine(tempRoot, "dlc"), dataDir);

            var callerThread = Environment.CurrentManagedThreadId;
            var task = reader.ReadAsync(game, CancellationToken.None);
            var info = task.GetAwaiter().GetResult();

            ChecksContext.Check("读到 continue_game 信息", info is not null, info?.Title);
            ChecksContext.Equal("标题解析正确", "测试存档", info?.Title ?? "(null)");
            ChecksContext.Check(
                "文件 IO 发生在其它线程（不是调用线程）",
                recorder.FirstReadThreadId is int threadId && threadId != callerThread,
                "调用线程 " + callerThread + "，读盘线程 " + (recorder.FirstReadThreadId?.ToString() ?? "(无)"));
        }
        finally
        {
            Cleanup(fs, tempRoot);
        }
    }

    // ------------------------------------------------------------------ 辅助

    private static GameInstallation BuildGame(
        string gameId,
        string installRoot,
        string dlcPath,
        string? gameDataDir = null) =>
        new(
            GameId: gameId,
            DisplayName: gameId,
            Version: "test",
            InstallRoot: installRoot,
            LauncherSettingsPath: Path.Combine(installRoot, "launcher-settings.json"),
            Format: LoadFileFormat.DlcLoadV0,
            GameDataDir: gameDataDir ?? Path.Combine(installRoot, "data"),
            DlcPath: dlcPath,
            ModDir: Path.Combine(installRoot, "mod"),
            ExePath: Path.Combine(installRoot, "game.exe"),
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
                ButtonHoverTextColor: "#CCCCCC",
                FontSize: 14,
                FontWeight: 400,
                TextShadow: null,
                Source: ThemeAssetSource.BuiltInPlaceholder),
            BrowserDlcUrl: null,
            BrowserModUrl: null,
            SteamAppId: 1158310,
            Support: GameSupport.Full);

    private static void Cleanup(IFileSystem fs, string tempRoot)
    {
        try
        {
            if (fs.DirectoryExists(tempRoot))
            {
                fs.Delete(tempRoot);
            }
        }
        catch (IOException)
        {
            // 清理失败不影响断言结果
        }
    }

    // ------------------------------------------------------------------ 替身

    /// <summary>记录「第一次读盘发生在哪个线程」，其余操作全部转给真实实现。</summary>
    private sealed class ThreadRecordingFileSystem : IFileSystem
    {
        private readonly IFileSystem _inner;
        private int _firstReadThreadId;

        public ThreadRecordingFileSystem(IFileSystem inner) => _inner = inner;

        public int? FirstReadThreadId => _firstReadThreadId == 0 ? null : _firstReadThreadId;

        public bool FileExists(string path)
        {
            RecordThread();
            return _inner.FileExists(path);
        }

        public string ReadAllText(string path)
        {
            RecordThread();
            return _inner.ReadAllText(path);
        }

        public byte[] ReadAllBytes(string path)
        {
            RecordThread();
            return _inner.ReadAllBytes(path);
        }

        public Stream OpenRead(string path) => _inner.OpenRead(path);

        public long GetFileSize(string path) => _inner.GetFileSize(path);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> data) => _inner.WriteAllBytesAtomic(path, data);

        public void WriteAllTextAtomic(string path, string text) => _inner.WriteAllTextAtomic(path, text);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) =>
            _inner.EnumerateFiles(directory, searchPattern, recursive);

        public IEnumerable<string> EnumerateDirectories(string directory) => _inner.EnumerateDirectories(directory);

        public string Combine(params string[] parts) => _inner.Combine(parts);

        public string GetFullPath(string path) => _inner.GetFullPath(path);

        public string? GetDirectoryName(string path) => _inner.GetDirectoryName(path);

        public string GetFileName(string path) => _inner.GetFileName(path);

        public void EnsureDirectory(string path) => _inner.EnsureDirectory(path);

        public void Delete(string path) => _inner.Delete(path);

        public void Copy(string source, string destination, bool overwrite) => _inner.Copy(source, destination, overwrite);

        public void Move(string source, string destination, bool overwrite) => _inner.Move(source, destination, overwrite);

        public void MoveDirectory(string source, string destination) => _inner.MoveDirectory(source, destination);

        private void RecordThread()
        {
            if (_firstReadThreadId == 0)
            {
                _firstReadThreadId = Environment.CurrentManagedThreadId;
            }
        }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Infos { get; } = new();

        public List<string> Warnings { get; } = new();

        public void Info(string message) => Infos.Add(message);

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null) => Warnings.Add(message);

        public int CountOf(string fragment) =>
            Infos.Count(m => m.Contains(fragment, StringComparison.Ordinal))
            + Warnings.Count(m => m.Contains(fragment, StringComparison.Ordinal));
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan delta) => UtcNow += delta;
    }
}
