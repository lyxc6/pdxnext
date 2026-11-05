namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Storage;

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
            var workshopRoot = Path.Combine(tempRoot, "workshop-info");
            var database = new PdxDatabase(fs, logger, Path.Combine(workshopRoot, PdxDatabase.FileName));
            var info = new WorkshopInfoCache(fs, logger, clock, workshopRoot, new SqliteWorkshopItemStore(database, logger));
            var assets = new FakeAssetDownloader(fs);
            var cache = new WorkshopImageCache(fs, assets, info);

            CheckScreenshotStemIsUrlHash(fs, cache, assets);
            CheckCoverUrlReused(fs, cache, assets, info);
            CheckLegacyShotNameNotReused(fs, cache, assets);
            CheckExtensionlessEmoticonStable(fs, cache, assets);
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

    /// <summary>
    /// 表情 URL 没有扩展名：缓存名只按 URL 哈希，落盘时由下载器按内容补扩展名；
    /// 带 query 的同一张表情先去 query，再命中和无 query 相同的文件。
    /// </summary>
    private static void CheckExtensionlessEmoticonStable(IFileSystem fs, WorkshopImageCache cache, FakeAssetDownloader assets)
    {
        const string Emoticon = "https://community.fastly.steamstatic.com/economy/emoticon/highrise";
        ChecksContext.Check(
            "表情缓存：带 query 的 URL 归一化到同一张表情",
            SteamEmoticon.TryCreate(Emoticon + "?v=2", ":highrise:", out var normalized, out _)
            && normalized == Emoticon,
            normalized);

        var callsBefore = assets.Calls;
        var first = cache.EnsureScreenshotAsync(WorkshopSource.Steam, "emoticon", normalized, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Check("表情缓存：无扩展名 URL 能落盘", first is not null);
        ChecksContext.Equal("表情缓存：首次下载调用一次", callsBefore + 1, assets.Calls);
        ChecksContext.Equal("表情缓存：落盘文件由下载器补扩展名", ".png", first is null ? string.Empty : Path.GetExtension(first));

        var second = cache.EnsureScreenshotAsync(WorkshopSource.Steam, "emoticon", Emoticon, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Check("表情缓存：规范化 URL 与无 query URL 命中同一文件", second == first);
        ChecksContext.Equal("表情缓存：命中后不重复下载", callsBefore + 1, assets.Calls);

        if (first is not null)
        {
            var stem = Path.GetFileNameWithoutExtension(first);
            ChecksContext.Check(
                "表情缓存：文件名仍按 URL 稳定哈希",
                stem.StartsWith("shot_", StringComparison.Ordinal) && stem.Length == "shot_".Length + 8,
                stem);
        }
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
