namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;

/// <summary>
/// <see cref="WorkshopImageCache"/> 的自检：截图文件名按 URL 哈希（不是列表序号）、
/// 同一张图在「封面槽位」里已存在时不再往 <c>shots\</c> 存第二份、旧命名不会被当成命中。
/// </summary>
public static class WorkshopImageCacheChecks
{
    private const string CoverUrl = "https://images.steamusercontent.com/ugc/100/AAA/";
    private const string OtherUrl = "https://images.steamusercontent.com/ugc/200/BBB/";

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-imgcache-" + Guid.NewGuid().ToString("N"));

        try
        {
            var clock = new FixedClock();
            var logger = new NullLogger();
            var info = new WorkshopInfoCache(fs, logger, clock, Path.Combine(tempRoot, "workshop-info"));
            var assets = new FakeAssetDownloader(fs);
            var cache = new WorkshopImageCache(fs, assets, info);

            CheckScreenshotStemIsUrlHash(fs, cache, assets);
            CheckCoverUrlReused(fs, cache, assets, info);
            CheckLegacyShotNameNotReused(fs, cache, assets);
        }
        finally
        {
            try
            {
                if (fs.DirectoryExists(tempRoot))
                {
                    fs.Delete(tempRoot);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 临时目录删不掉不影响断言结果。
            }
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>同一 URL 两次调用命中同一个文件；不同 URL 落成不同文件。</summary>
    private static void CheckScreenshotStemIsUrlHash(IFileSystem fs, WorkshopImageCache cache, FakeAssetDownloader assets)
    {
        var first = cache.EnsureScreenshotAsync(WorkshopSource.Steam, "123", CoverUrl, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Check("图片缓存：首次下载返回本地路径", first is not null);
        ChecksContext.Equal("图片缓存：首次下载调用一次", 1, assets.Calls);

        var second = cache.EnsureScreenshotAsync(WorkshopSource.Steam, "123", CoverUrl, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Check("图片缓存：同一 URL 复用同一文件", second == first);
        ChecksContext.Equal("图片缓存：同一 URL 不再下载", 1, assets.Calls);

        var other = cache.EnsureScreenshotAsync(WorkshopSource.Steam, "123", OtherUrl, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Check("图片缓存：不同 URL 落成不同文件", other is not null && other != first);
        ChecksContext.Equal("图片缓存：不同 URL 才触发第二次下载", 2, assets.Calls);

        if (first is not null)
        {
            var fileName = Path.GetFileNameWithoutExtension(first);
            ChecksContext.Check(
                "图片缓存：文件名是 shot_ 加 8 位 hex",
                fileName.StartsWith("shot_", StringComparison.Ordinal) && fileName.Length == "shot_".Length + 8);
        }
    }

    /// <summary>封面槽位已记着同一 URL 时，截图直接复用封面，不落第二份。</summary>
    private static void CheckCoverUrlReused(
        IFileSystem fs,
        WorkshopImageCache cache,
        FakeAssetDownloader assets,
        WorkshopInfoCache info)
    {
        var cover = cache.EnsureCoverAsync(WorkshopSource.Steam, "456", CoverUrl, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Check("图片缓存：封面下载成功", cover is not null);

        var callsBefore = assets.Calls;
        var shot = cache.EnsureScreenshotAsync(WorkshopSource.Steam, "456", CoverUrl, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("图片缓存：截图复用封面路径", shot == cover);
        ChecksContext.Equal("图片缓存：复用封面时不发请求", callsBefore, assets.Calls);

        var shotsDirectory = cache.ShotsDirectory(WorkshopSource.Steam, "456");
        var shotFiles = fs.EnumerateFiles(shotsDirectory, "*", recursive: false).ToList();
        ChecksContext.Equal("图片缓存：shots 目录里没有第二份", 0, shotFiles.Count);

        var key = WorkshopInfoCache.Key(WorkshopSource.Steam, "456");
        ChecksContext.Check(
            "图片缓存：封面记账仍在",
            info.TryGetCachedCoverForUrl(key, CoverUrl, out var recorded) && recorded == cover);
    }

    /// <summary>目录里只剩旧命名的 <c>shot_NN</c> 时，不能被当成「已下载」。</summary>
    private static void CheckLegacyShotNameNotReused(IFileSystem fs, WorkshopImageCache cache, FakeAssetDownloader assets)
    {
        var directory = cache.ShotsDirectory(WorkshopSource.Steam, "789");
        fs.WriteAllTextAtomic(Path.Combine(directory, "shot_00.png"), "legacy");

        var callsBefore = assets.Calls;
        var path = cache.EnsureScreenshotAsync(WorkshopSource.Steam, "789", OtherUrl, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("图片缓存：旧命名截图不算命中", path is not null && path != Path.Combine(directory, "shot_00.png"));
        ChecksContext.Equal("图片缓存：旧命名不算命中会重新下载", callsBefore + 1, assets.Calls);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeAssetDownloader : IAssetDownloader
    {
        private readonly IFileSystem _fileSystem;

        public FakeAssetDownloader(IFileSystem fileSystem) => _fileSystem = fileSystem;

        public int Calls { get; private set; }

        public Task<string?> DownloadAsync(string url, string targetPathWithoutExtension, CancellationToken cancellationToken)
        {
            Calls++;
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
