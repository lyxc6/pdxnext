namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Storage;

/// <summary>
/// 创意工坊「条目页 HTML 解析结果」缓存（<see cref="SteamItemPageCache"/>，v2 起背后是 <c>workshop_pages</c> 真表）的自检：
/// 落库往返、TTL、容量淘汰、超大条目拒绝、旧索引文件容错、还原映射。
/// <para>
/// 起因：每次打开详情都会拉 1–3 MB 的 SSR 页面，是单次交互里最大的一笔请求预算；
/// 这个缓存把解析结果存库，重开同一个 mod 时零请求。
/// </para>
/// </summary>
public static class SteamItemPageCacheChecks
{
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var root = Path.Combine(Path.GetTempPath(), "pdx-checks-item-pages-" + Guid.NewGuid().ToString("N"));
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

        var before = ChecksContext.Failures;
        try
        {
            CheckRoundTrip(fileSystem, root, clock);
            CheckCollectionRoundTrip(fileSystem, root, clock);
            CheckCollectionCacheGuards(fileSystem, root, clock);
            CheckTtl(fileSystem, root, clock);
            CheckEviction(fileSystem, root, clock);
            CheckOversizeRejected(fileSystem, root, clock);
            CheckLegacyIndexIgnored(fileSystem, root, clock);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ChecksContext.Check("条目页缓存：未抛出异常", false, ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 临时目录删不掉不影响断言。
            }
        }

        return ChecksContext.Failures - before;
    }

    /// <summary>写入后重建实例仍然读得到，字段不丢（评论是首屏复用的关键）。</summary>
    private static void CheckRoundTrip(IFileSystem fileSystem, string root, MutableClock clock)
    {
        var first = CreateCache(fileSystem, clock, root);
        first.Set("2216623537", SamplePage());

        ChecksContext.Equal("条目页缓存：写入后条目数", 1, first.Count);

        var second = CreateCache(fileSystem, clock, root);
        var found = second.TryGet("2216623537", out var entry);

        ChecksContext.Check("条目页缓存：重建后仍能读到", found);
        if (!found)
        {
            return;
        }

        ChecksContext.Equal("条目页缓存：作者名", "Author", entry.AuthorName);
        ChecksContext.Equal("条目页缓存：作者主页", "https://steamcommunity.com/profiles/1", entry.AuthorProfileUrl);
        ChecksContext.Equal("条目页缓存：描述", "<p>hi</p>", entry.DescriptionHtml);
        ChecksContext.Equal("条目页缓存：画廊张数", 2, entry.ImageUrls.Count);
        ChecksContext.Equal("条目页缓存：截图张数", 1, entry.ScreenshotUrls.Count);
        ChecksContext.Equal("条目页缓存：评论总数", 42, entry.TotalComments);
        ChecksContext.Equal("条目页缓存：首屏评论条数", 1, entry.Comments.Count);

        var page = SteamItemPageCache.Restore(entry);
        ChecksContext.Check(
            "条目页缓存：还原后评论字段完整",
            page.Comments.Count == 1
                && page.Comments[0].Id == "c1"
                && page.Comments[0].AuthorName == "Commenter"
                && page.Comments[0].TextHtml == "<b>nice</b>"
                && page.Comments[0].TextPlain == "nice",
            page.Comments.Count == 0 ? "(没有评论)" : page.Comments[0].TextPlain);
        ChecksContext.Equal("条目页缓存：还原后评论总数", 42, page.TotalComments);
        ChecksContext.Equal("条目页缓存：还原后作者名", "Author", page.AuthorName);
    }

    /// <summary>合集子项是条目页缓存里最贵也最关键的字段：49 项必须完整往返。</summary>
    private static void CheckCollectionRoundTrip(IFileSystem fileSystem, string root, MutableClock clock)
    {
        var cache = CreateCache(fileSystem, clock, Path.Combine(root, "collection"));
        cache.Set("collection-49", CollectionPage(49), requireCollectionItems: true);

        var found = cache.TryGet("collection-49", out var entry);
        ChecksContext.Check("条目页缓存：合集写入后能读到", found);
        if (!found)
        {
            return;
        }

        var restored = SteamItemPageCache.Restore(entry);
        ChecksContext.Check("条目页缓存：合集页识别", restored.IsCollectionPage);
        ChecksContext.Equal("条目页缓存：合集子项数", 49, restored.CollectionItemList.Count);
        ChecksContext.Equal("条目页缓存：首个子项 id", "child-0", restored.CollectionItemList[0].PublishedFileId);
        ChecksContext.Equal("条目页缓存：首个子项标题", "Child 0", restored.CollectionItemList[0].Title);
        ChecksContext.Check(
            "条目页缓存：子项缩略图保留",
            restored.CollectionItemList[0].PreviewUrl?.Contains("child-0.jpg", StringComparison.Ordinal) == true);
    }

    /// <summary>合集缓存的两条保护：解析不到子项不覆盖旧内容；超限删旧行而不是留旧内容。</summary>
    private static void CheckCollectionCacheGuards(IFileSystem fileSystem, string root, MutableClock clock)
    {
        var cache = CreateCache(fileSystem, clock, Path.Combine(root, "collection-guards"));
        cache.Set("collection-guarded", CollectionPage(3), requireCollectionItems: true);

        // 瞬时解析失败（页面有作者/描述但没有 collectionItem）不能把已知合集写成 0 项。
        cache.Set("collection-guarded", SamplePage(), requireCollectionItems: true);
        var kept = cache.TryGet("collection-guarded", out var keptEntry);
        ChecksContext.Check("条目页缓存：空子项不覆盖旧合集", kept);
        if (kept)
        {
            ChecksContext.Equal("条目页缓存：旧合集子项仍在", 3, SteamItemPageCache.Restore(keptEntry).CollectionItemList.Count);
        }

        // 降级（去评论/截图/配图）后仍超限：旧行必须删除，不能再被命中。
        cache.Set("collection-guarded", CollectionPage(500, hugeText: new string('x', 400)), requireCollectionItems: true);
        ChecksContext.Check("条目页缓存：超大合集删除旧行", !cache.TryGet("collection-guarded", out _));
    }

    private static SteamWorkshopItemPage CollectionPage(int count, string? hugeText = null)
    {
        var items = new List<SteamWorkshopCollectionItemPage>(count);
        for (var i = 0; i < count; i++)
        {
            items.Add(new SteamWorkshopCollectionItemPage(
                "child-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hugeText ?? "Child " + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "https://images.steamusercontent.com/child-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".jpg",
                "Author",
                hugeText ?? "Short description " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return new SteamWorkshopItemPage(
            "Author",
            "https://steamcommunity.com/profiles/1",
            hugeText ?? "<p>collection</p>",
            [],
            [],
            0,
            [],
            items);
    }


    private static void CheckTtl(IFileSystem fileSystem, string root, MutableClock clock)
    {
        var cache = CreateCache(fileSystem, clock, Path.Combine(root, "ttl"));
        cache.Set("ttl-1", SamplePage());

        ChecksContext.Check("条目页缓存：刚写入是新鲜的", cache.TryGet("ttl-1", out var entry) && cache.IsFresh(entry));

        clock.Advance(SteamItemPageCache.DefaultPageTtl + TimeSpan.FromMinutes(1));
        ChecksContext.Check(
            "条目页缓存：超过 12 小时算过期",
            cache.TryGet("ttl-1", out var stale) && !cache.IsFresh(stale));
    }

    /// <summary>超过容量时先淘汰最旧的条目。</summary>
    private static void CheckEviction(IFileSystem fileSystem, string root, MutableClock clock)
    {
        var evictRoot = Path.Combine(root, "evict");
        var cache = CreateCache(fileSystem, clock, evictRoot);

        for (var i = 0; i < SteamItemPageCache.MaxEntries + 1; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            cache.Set("id-" + i, SamplePage());
        }

        ChecksContext.Equal("条目页缓存：容量上限生效", SteamItemPageCache.MaxEntries, cache.Count);
        ChecksContext.Check("条目页缓存：最旧的条目被淘汰", !cache.TryGet("id-0", out _));
        ChecksContext.Check(
            "条目页缓存：最新的条目还在",
            cache.TryGet("id-" + SteamItemPageCache.MaxEntries, out _));

        cache.Clear();
        ChecksContext.Equal("条目页缓存：清空后条目数归零", 0, cache.Count);
    }

    /// <summary>单条超过 128 KB 直接拒绝：真表没有内存兜底，写进去只会让库无限长。</summary>
    private static void CheckOversizeRejected(IFileSystem fileSystem, string root, MutableClock clock)
    {
        var bigRoot = Path.Combine(root, "big");
        var cache = CreateCache(fileSystem, clock, bigRoot);
        var huge = new string('x', SteamItemPageCache.MaxEntryBytes + 1024);

        cache.Set("big-1", SamplePage() with { DescriptionHtml = huge });

        ChecksContext.Check("条目页缓存：超大条目没有被写入", !cache.TryGet("big-1", out _));
        ChecksContext.Equal("条目页缓存：超大条目不占条目数", 0, cache.Count);
    }

    /// <summary>旧版 workshop-pages.json 已不再被读取：放在目录里也不能把数据带进来。</summary>
    private static void CheckLegacyIndexIgnored(IFileSystem fileSystem, string root, MutableClock clock)
    {
        var legacyRoot = Path.Combine(root, "legacy");
        fileSystem.EnsureDirectory(legacyRoot);
        fileSystem.WriteAllTextAtomic(
            Path.Combine(legacyRoot, SteamItemPageCache.IndexFileName),
            "{ this is not json");

        var cache = CreateCache(fileSystem, clock, legacyRoot);

        ChecksContext.Equal("条目页缓存：旧索引文件按空缓存处理", 0, cache.Count);
        cache.Set("after-legacy", SamplePage());
        ChecksContext.Check("条目页缓存：旧文件存在时仍可写入", cache.TryGet("after-legacy", out _));
    }

    private static SteamItemPageCache CreateCache(IFileSystem fileSystem, MutableClock clock, string root)
    {
        var database = new PdxDatabase(fileSystem, new NullLogger(), Path.Combine(root, PdxDatabase.FileName));
        return new SteamItemPageCache(fileSystem, new NullLogger(), clock, root, new SqliteWorkshopPageStore(database, new NullLogger()));
    }

    private static SteamWorkshopItemPage SamplePage() => new(
        "Author",
        "https://steamcommunity.com/profiles/1",
        "<p>hi</p>",
        ["https://images.steamusercontent.com/a.jpg", "https://images.steamusercontent.com/b.jpg"],
        ["https://images.steamusercontent.com/shot.jpg"],
        42,
        [
            new SteamWorkshopComment(
                "c1",
                "Commenter",
                null,
                new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero),
                "<b>nice</b>",
                "nice"),
        ]);

    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan delta) => UtcNow += delta;
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
