namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;

/// <summary>
/// <see cref="ModThumbnailCache"/> 与 <see cref="ModThumbnailService"/> 自检：
/// 索引 roundtrip、文件失效、URL 换名删旧、失败退避、按天保留清理、索引损坏不抛、离线零请求。
/// 下载器是假的，全部在临时目录里跑，不发任何网络请求。
/// </summary>
public static class ModThumbnailCacheChecks
{
    private const string UrlA = "https://images.steamusercontent.com/ugc/14689653902041455984/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/";
    private const string UrlB = "https://images.steamusercontent.com/ugc/14689653902041455984/BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB/";

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();
        var clock = new FakeClock();
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-thumbcache-" + Guid.NewGuid().ToString("N"));

        try
        {
            fs.EnsureDirectory(tempRoot);
            CheckIndexRoundtrip(fs, logger, clock, tempRoot);
            CheckUrlChange(fs, logger, clock, tempRoot);
            CheckBackoff(fs, logger, clock, tempRoot);
            CheckPrune(fs, logger, clock, tempRoot);
            CheckCorruptIndex(fs, logger, clock, tempRoot);
            CheckServiceOffline(fs, logger, clock, tempRoot);
            CheckServiceDownloads(fs, logger, clock, tempRoot);
        }
        finally
        {
            TryDelete(fs, tempRoot);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckIndexRoundtrip(IFileSystem fs, IAppLogger logger, FakeClock clock, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailCacheChecks] 索引 roundtrip 与文件失效");
        var root = Path.Combine(tempRoot, "roundtrip");
        var cache = new ModThumbnailCache(fs, logger, clock, root);
        var modId = Guid.NewGuid();
        var file = WriteImage(fs, cache, modId);

        cache.RecordSuccess(modId, UrlA, file);
        ChecksContext.Equal("记录成功后可以取到路径", file, cache.TryGetCachedPath(modId));
        ChecksContext.Equal("索引条目数", 1, cache.Count);

        var reloaded = new ModThumbnailCache(fs, logger, clock, root);
        ChecksContext.Equal("重新打开索引仍能取到路径", file, reloaded.TryGetCachedPath(modId));
        ChecksContext.Equal("重新打开索引条目数", 1, reloaded.Count);

        fs.Delete(file);
        ChecksContext.Check("缓存文件被删后返回 null", reloaded.TryGetCachedPath(modId) is null);
    }

    private static void CheckUrlChange(IFileSystem fs, IAppLogger logger, FakeClock clock, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailCacheChecks] URL 变化时删除旧文件");
        var cache = new ModThumbnailCache(fs, logger, clock, Path.Combine(tempRoot, "url-change"));
        var modId = Guid.NewGuid();
        var first = WriteImage(fs, cache, modId);
        cache.RecordSuccess(modId, UrlA, first);

        var second = Path.Combine(cache.CacheRoot, ModThumbnailCache.ImageFolderName, modId.ToString("N") + "-v2.png");
        fs.WriteAllTextAtomic(second, "png-v2");
        cache.RecordSuccess(modId, UrlB, second);

        ChecksContext.Check("旧文件已删除", !fs.FileExists(first));
        ChecksContext.Equal("新文件可命中", second, cache.TryGetCachedPath(modId));
        ChecksContext.Equal("条目数仍为 1", 1, cache.Count);
    }

    private static void CheckBackoff(IFileSystem fs, IAppLogger logger, FakeClock clock, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailCacheChecks] 失败退避 6 小时");
        var cache = new ModThumbnailCache(fs, logger, clock, Path.Combine(tempRoot, "backoff"));
        var modId = Guid.NewGuid();
        cache.RecordFailure(modId, UrlA);

        ChecksContext.Check("刚失败时在退避窗口内", cache.IsInBackoff(modId));
        clock.Advance(TimeSpan.FromHours(5));
        ChecksContext.Check("5 小时后仍在退避窗口内", cache.IsInBackoff(modId));
        clock.Advance(TimeSpan.FromHours(2));
        ChecksContext.Check("7 小时后解除退避", !cache.IsInBackoff(modId));
        ChecksContext.Check("未知 mod 不在退避窗口内", !cache.IsInBackoff(Guid.NewGuid()));
    }

    private static void CheckPrune(IFileSystem fs, IAppLogger logger, FakeClock clock, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailCacheChecks] 按保留天数清理与孤儿文件");
        var root = Path.Combine(tempRoot, "prune");
        var cache = new ModThumbnailCache(fs, logger, clock, root);
        var oldId = Guid.NewGuid();
        var oldFile = WriteImage(fs, cache, oldId);
        cache.RecordSuccess(oldId, UrlA, oldFile);

        clock.Advance(TimeSpan.FromDays(40));
        var newId = Guid.NewGuid();
        var newFile = WriteImage(fs, cache, newId);
        cache.RecordSuccess(newId, UrlB, newFile);

        var orphan = Path.Combine(root, ModThumbnailCache.ImageFolderName, Guid.NewGuid().ToString("N") + ".png");
        fs.WriteAllTextAtomic(orphan, "orphan");

        var deleted = cache.Prune(30);
        ChecksContext.Check("过期条目被清理", cache.TryGetCachedPath(oldId) is null);
        ChecksContext.Check("未过期条目保留", cache.TryGetCachedPath(newId) is not null);
        ChecksContext.Check("孤儿文件被清理", !fs.FileExists(orphan));
        ChecksContext.Check("清理数量包含过期文件与孤儿", deleted >= 2, deleted.ToString(System.Globalization.CultureInfo.InvariantCulture));
        ChecksContext.Equal("清理后条目数", 1, cache.Count);
    }

    private static void CheckCorruptIndex(IFileSystem fs, IAppLogger logger, FakeClock clock, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailCacheChecks] 索引损坏不抛");
        var root = Path.Combine(tempRoot, "corrupt");
        fs.EnsureDirectory(root);
        fs.WriteAllTextAtomic(Path.Combine(root, ModThumbnailCache.IndexFileName), "{ this is not json");

        ModThumbnailCache? cache = null;
        var threw = false;
        try
        {
            cache = new ModThumbnailCache(fs, logger, clock, root);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or FormatException or ArgumentException)
        {
            threw = true;
            Console.WriteLine("  意外异常：" + exception.Message);
        }

        ChecksContext.Check("损坏索引不抛异常", !threw && cache is not null);
        ChecksContext.Equal("损坏索引视为空", 0, cache is null ? -1 : cache.Count);
    }

    private static void CheckServiceOffline(IFileSystem fs, IAppLogger logger, FakeClock clock, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailCacheChecks] 关闭联网时零请求");
        var downloader = new FakeAssetDownloader(fs);
        var service = new ModThumbnailService(fs, downloader, clock, logger, Path.Combine(tempRoot, "offline"));
        var entries = new[] { CreateEntry(Guid.NewGuid(), UrlA) };

        var result = service.FillAsync(entries, allowOnline: false, retentionDays: 30, CancellationToken.None)
            .GetAwaiter().GetResult();

        ChecksContext.Equal("离线时下载器调用次数", 0, downloader.Calls);
        ChecksContext.Equal("离线时尝试数", 0, result.Attempted);
        ChecksContext.Equal("离线时缓存命中数", 0, result.CacheHits);
        ChecksContext.Equal("离线时更新列表为空", 0, result.Updates.Count);
    }

    private static void CheckServiceDownloads(IFileSystem fs, IAppLogger logger, FakeClock clock, string tempRoot)
    {
        Console.WriteLine("[ModThumbnailCacheChecks] 联网补全与二次命中");
        var downloader = new FakeAssetDownloader(fs);
        var service = new ModThumbnailService(fs, downloader, clock, logger, Path.Combine(tempRoot, "online"));
        var modId = Guid.NewGuid();
        var entries = new[] { CreateEntry(modId, UrlA) };

        var first = service.FillAsync(entries, allowOnline: true, retentionDays: 30, CancellationToken.None)
            .GetAwaiter().GetResult();
        ChecksContext.Equal("第一次联网下载 1 次", 1, downloader.Calls);
        ChecksContext.Equal("第一轮尝试数", 1, first.Attempted);
        ChecksContext.Equal("第一轮失败数", 0, first.Failed);
        ChecksContext.Equal("下载后返回更新", 1, first.Updates.Count);
        ChecksContext.Check("更新路径存在", first.Updates.Count == 1 && fs.FileExists(first.Updates[0].ThumbnailPath));

        var second = service.FillAsync(entries, allowOnline: true, retentionDays: 30, CancellationToken.None)
            .GetAwaiter().GetResult();
        ChecksContext.Equal("二次调用不再下载", 1, downloader.Calls);
        ChecksContext.Equal("第二轮缓存命中数", 1, second.CacheHits);
        ChecksContext.Equal("第二轮尝试数", 0, second.Attempted);
        ChecksContext.Equal("缓存条目数", 1, service.Cache.Count);

        var failing = new FakeAssetDownloader(fs) { Succeed = false };
        var failingService = new ModThumbnailService(fs, failing, clock, logger, Path.Combine(tempRoot, "failing"));
        var failedEntry = CreateEntry(Guid.NewGuid(), UrlA);
        var failedResult = failingService.FillAsync(new[] { failedEntry }, allowOnline: true, retentionDays: 30, CancellationToken.None)
            .GetAwaiter().GetResult();
        ChecksContext.Equal("下载失败计数", 1, failedResult.Failed);
        ChecksContext.Check("失败后进入退避", failingService.Cache.IsInBackoff(failedEntry.ModId));

        var retry = failingService.FillAsync(new[] { failedEntry }, allowOnline: true, retentionDays: 30, CancellationToken.None)
            .GetAwaiter().GetResult();
        ChecksContext.Equal("退避期内不再尝试", 1, failing.Calls);
        ChecksContext.Equal("退避期内尝试数为 0", 0, retry.Attempted);
    }

    private static string WriteImage(IFileSystem fs, ModThumbnailCache cache, Guid modId)
    {
        var path = cache.TargetPathWithoutExtension(modId) + ".png";
        fs.WriteAllTextAtomic(path, "not-a-real-png-but-exists");
        return path;
    }

    private static ModLibraryEntry CreateEntry(Guid modId, string? thumbnailUrl) =>
        new(
            ModId: modId,
            Name: "mod-" + modId.ToString("N")[..6],
            GameRegistryId: "mod/ugc_" + modId.ToString("N")[..8] + ".mod",
            DirPath: null,
            ArchivePath: null,
            ThumbnailPath: null,
            RequiredVersion: "v4.*",
            Status: "ready_to_play",
            Source: "steam",
            SizeBytes: 1024,
            SubscribedOn: null,
            PlaySetIds: Array.Empty<Guid>(),
            IsUnregistered: false,
            ThumbnailUrl: thumbnailUrl);

    private static void TryDelete(IFileSystem fs, string path)
    {
        try
        {
            if (fs.DirectoryExists(path))
            {
                fs.Delete(path);
            }
        }
        catch (IOException exception)
        {
            Console.WriteLine("  warn 清理临时目录失败：" + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.WriteLine("  warn 清理临时目录无权限：" + exception.Message);
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan delta) => UtcNow = UtcNow + delta;
    }

    private sealed class FakeAssetDownloader : IAssetDownloader
    {
        private readonly IFileSystem _fileSystem;

        public FakeAssetDownloader(IFileSystem fileSystem) => _fileSystem = fileSystem;

        public int Calls { get; private set; }

        public bool Succeed { get; init; } = true;

        public Task<string?> DownloadAsync(string url, string targetPathWithoutExtension, CancellationToken cancellationToken)
        {
            Calls++;
            if (!Succeed)
            {
                return Task.FromResult<string?>(null);
            }

            var path = targetPathWithoutExtension + ".png";
            _fileSystem.WriteAllTextAtomic(path, "downloaded:" + url);
            return Task.FromResult<string?>(path);
        }
    }

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
