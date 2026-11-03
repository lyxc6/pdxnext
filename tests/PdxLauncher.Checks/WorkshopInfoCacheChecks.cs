namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Storage;

/// <summary>
/// 创意工坊 mod 信息缓存（<see cref="WorkshopInfoCache"/>，v2 起背后是 <c>workshop_items</c> 真表）的自检：
/// 键格式、合并语义、封面命中、详情 TTL、快照过滤、清空、旧索引文件不再被读取。
/// </summary>
public static class WorkshopInfoCacheChecks
{
    /// <summary>运行本组检查。</summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <returns>0 表示全部通过。</returns>
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        // 只看本组自己新增的失败：别的检查类先失败不该把这一组也拖下水。
        var before = ChecksContext.Failures;
        var root = Path.Combine(Path.GetTempPath(), "pdx-checks-workshop-" + Guid.NewGuid().ToString("N"));
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero));
        var logger = new NullLogger();

        try
        {
            CheckKeys();
            CheckUpsertAndMerge(fileSystem, root, clock, logger);
            CheckPersistence(fileSystem, root, clock, logger);
            CheckCover(fileSystem, root, clock, logger);
            CheckDetailTtl(fileSystem, root, clock, logger);
            CheckDetailMeta(fileSystem, root, clock, logger);
            CheckSnapshot(fileSystem, root, clock, logger);
            CheckClear(fileSystem, root, clock, logger);
            CheckLegacyIndexIgnored(fileSystem, root, clock, logger);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ChecksContext.Check("创意工坊缓存：未抛出异常", false, ex.Message);
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
                // 临时目录删不掉不影响断言结果。
            }
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckKeys()
    {
        ChecksContext.Equal("缓存键：steam", "steam:2216623537", WorkshopInfoCache.Key(WorkshopSource.Steam, "2216623537"));
        ChecksContext.Equal("缓存键：pdx", "pdx:12345", WorkshopInfoCache.Key(WorkshopSource.Pdx, "12345"));
        ChecksContext.Equal("缓存键：来源标记", "steam", WorkshopInfoCache.SourceTag(WorkshopSource.Steam));
        ChecksContext.Equal("缓存键：默认来源标记", "pdx", WorkshopInfoCache.SourceTag(WorkshopSource.None));
    }

    private static void CheckUpsertAndMerge(
        IFileSystem fileSystem,
        string root,
        IClock clock,
        IAppLogger logger)
    {
        var cache = CreateCache(fileSystem, logger, clock, root);

        cache.Upsert(new WorkshopInfoCacheEntry
        {
            Source = "steam",
            Id = "100",
            GameId = "ck3",
            Name = "Better UI",
            Author = "7656119",
            Rating = 4.5,
            RatingsTotal = 120,
            Subscriptions = 9000,
            CoverUrl = "https://images.steamusercontent.com/100.jpg",
            SizeBytes = 1024,
        });

        ChecksContext.Equal("缓存：写入后条目数", 1, cache.Count);
        ChecksContext.Check(
            "缓存：写入后可读",
            cache.TryGet("steam:100", out var entry) && entry.Name == "Better UI" && entry.Author == "7656119");
        ChecksContext.Check(
            "缓存：同一条目的来源标记等价",
            cache.TryGet(WorkshopInfoCache.Key(WorkshopSource.Steam, "100"), out var again) && again.Id == "100");

        // 列表刷新只带新订阅数，旧的名称/作者/封面不能丢。
        cache.Upsert(new WorkshopInfoCacheEntry
        {
            Source = "steam",
            Id = "100",
            Subscriptions = 9500,
        });

        ChecksContext.Check(
            "缓存：合并保留旧字段",
            cache.TryGet("steam:100", out var merged) &&
            merged.Name == "Better UI" &&
            merged.Author == "7656119" &&
            merged.CoverUrl is not null &&
            merged.Subscriptions == 9500);

        ChecksContext.Equal("缓存：合并不产生新条目", 1, cache.Count);

        // 同一 id 在不同来源下是两条，互不覆盖。
        cache.Upsert(new WorkshopInfoCacheEntry { Source = "pdx", Id = "100", Name = "PDX Mod" });
        ChecksContext.Equal("缓存：不同来源分开存", 2, cache.Count);
        ChecksContext.Check(
            "缓存：来源不同的同名 id 不串",
            cache.TryGet("steam:100", out var steamEntry) &&
            cache.TryGet("pdx:100", out var pdxEntry) &&
            steamEntry.Name == "Better UI" &&
            pdxEntry.Name == "PDX Mod");

        // 空 Id 一律忽略，避免写出 "steam:" 这种键。
        cache.Upsert(new WorkshopInfoCacheEntry { Source = "steam", Id = "  " });
        ChecksContext.Equal("缓存：空 id 被忽略", 2, cache.Count);
    }

    private static void CheckPersistence(
        IFileSystem fileSystem,
        string root,
        IClock clock,
        IAppLogger logger)
    {
        var persistRoot = Path.Combine(root, "persist");
        var writer = CreateCache(fileSystem, logger, clock, persistRoot);
        writer.Upsert(new WorkshopInfoCacheEntry
        {
            Source = "steam",
            Id = "200",
            GameId = "ck3",
            Name = "Persisted",
        });

        // v2 起每次 Upsert 直接落库，不再有「只改内存、Flush 才写盘」的中间态。
        ChecksContext.Equal("缓存：写入即落库", 1, writer.Count);

        var reader = CreateCache(fileSystem, logger, clock, persistRoot);
        ChecksContext.Check(
            "缓存：新实例能读回条目",
            reader.TryGet("steam:200", out var entry) && entry.Name == "Persisted");
    }

    private static void CheckCover(
        IFileSystem fileSystem,
        string root,
        IClock clock,
        IAppLogger logger)
    {
        var cache = CreateCache(fileSystem, logger, clock, root);
        cache.Upsert(new WorkshopInfoCacheEntry { Source = "steam", Id = "300", Name = "Cover" });

        ChecksContext.Check("缓存：没有封面文件名时不命中", !cache.TryGetCachedCover("steam:300", out _));

        var relative = Path.Combine("steam", "300.jpg");
        var absolute = Path.Combine(root, relative);
        fileSystem.EnsureDirectory(Path.Combine(root, "steam"));
        fileSystem.WriteAllTextAtomic(absolute, "fake-image");

        cache.SetCover("steam:300", relative, "https://images.steamusercontent.com/300.jpg");

        ChecksContext.Check(
            "缓存：封面文件存在时命中",
            cache.TryGetCachedCover("steam:300", out var cachedPath) &&
            string.Equals(cachedPath, absolute, StringComparison.OrdinalIgnoreCase));

        ChecksContext.Check(
            "缓存：封面文件名写进条目",
            cache.TryGet("steam:300", out var entry) &&
            string.Equals(entry.CoverFileName, relative, StringComparison.OrdinalIgnoreCase) &&
            entry.CoverFetchedUtc is not null);
    }

    private static void CheckDetailTtl(
        IFileSystem fileSystem,
        string root,
        MutableClock clock,
        IAppLogger logger)
    {
        var cache = CreateCache(fileSystem, logger, clock, root);
        cache.Upsert(new WorkshopInfoCacheEntry { Source = "steam", Id = "400", Name = "Detail" });

        ChecksContext.Check("缓存：没抓过详情时不算新鲜", !cache.TryGet("steam:400", out var empty) || !cache.IsDetailFresh(empty));

        cache.SetDetail("steam:400", "完整描述", ["https://img/1.jpg"]);
        ChecksContext.Check(
            "缓存：刚抓的详情算新鲜",
            cache.TryGet("steam:400", out var fresh) &&
            fresh.DetailText == "完整描述" &&
            fresh.DetailScreenshots is { Count: 1 } &&
            cache.IsDetailFresh(fresh));

        // 默认 TTL 7 天：8 天后必须重新抓。
        clock.Advance(TimeSpan.FromDays(8));
        ChecksContext.Check(
            "缓存：超过 TTL 不再新鲜",
            cache.TryGet("steam:400", out var stale) && !cache.IsDetailFresh(stale));

        // 显式指定更长的 TTL 时还能命中。
        ChecksContext.Check(
            "缓存：可自定义 TTL",
            cache.IsDetailFresh(stale, TimeSpan.FromDays(30)));
    }

    /// <summary>详情元数据（作者 SteamID / 标签 / 浏览数 / 累计订阅）要能落盘再读回，缺字段的写入不能把旧值洗掉。</summary>
    private static void CheckDetailMeta(
        IFileSystem fileSystem,
        string root,
        MutableClock clock,
        IAppLogger logger)
    {
        var metaRoot = Path.Combine(root, "meta");
        var cache = CreateCache(fileSystem, logger, clock, metaRoot);
        cache.Upsert(new WorkshopInfoCacheEntry { Source = "steam", Id = "500", Name = "Meta" });

        cache.SetDetail(
            "steam:500",
            "描述",
            null,
            creatorSteamId: "76561198145098971",
            tags: ["Gameplay"],
            views: 4321,
            lifetimeSubscriptions: 99);

        ChecksContext.Check(
            "缓存：详情元数据写回后能读回",
            cache.TryGet("steam:500", out var stored) &&
            stored.CreatorSteamId == "76561198145098971" &&
            stored.Tags is { Count: 1 } &&
            stored.Tags[0] == "Gameplay" &&
            stored.Views == 4321 &&
            stored.LifetimeSubscriptions == 99);

        // 之后一次没有元数据的 SetDetail（等价于 PDX 详情）不能把这些值洗掉。
        cache.SetDetail("steam:500", "描述2", null);
        ChecksContext.Check(
            "缓存：缺元数据的写入保留旧值",
            cache.TryGet("steam:500", out var kept) &&
            kept.CreatorSteamId == "76561198145098971" &&
            kept.Views == 4321);

        // 旧条目靠 Author 兜底还原出作者 id，评论翻页才不会失效。
        ChecksContext.Equal(
            "缓存：旧条目还原出作者 id",
            "76561198000000009",
            SteamWorkshopDetailCache.Restore(
                new WorkshopInfoCacheEntry { Author = "76561198000000009" },
                "600",
                "兜底").CreatorSteamId);
    }

    private static void CheckSnapshot(
        IFileSystem fileSystem,
        string root,
        IClock clock,
        IAppLogger logger)
    {
        // 用独立子目录，免得被前面几组写进同一个库的条目干扰。
        var cache = CreateCache(fileSystem, logger, clock, Path.Combine(root, "snapshot"));
        cache.Upsert(new WorkshopInfoCacheEntry { Source = "steam", Id = "501", GameId = "ck3", Name = "A", UpdatedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) });
        cache.Upsert(new WorkshopInfoCacheEntry { Source = "steam", Id = "502", GameId = "ck3", Name = "B", UpdatedUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero) });
        cache.Upsert(new WorkshopInfoCacheEntry { Source = "steam", Id = "503", GameId = "stellaris", Name = "C", UpdatedUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero) });
        cache.Upsert(new WorkshopInfoCacheEntry { Source = "pdx", Id = "601", GameId = "ck3", Name = "D", UpdatedUtc = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero) });

        var ck3 = cache.Snapshot(WorkshopSource.Steam, "ck3");
        ChecksContext.Equal("缓存：快照按游戏过滤", 2, ck3.Count);
        ChecksContext.Check("缓存：快照按更新时间降序", ck3[0].Id == "502" && ck3[1].Id == "501");

        var allSteam = cache.Snapshot(WorkshopSource.Steam, null);
        ChecksContext.Equal("缓存：不过滤游戏时给全量", 3, allSteam.Count);

        // 快照是副本：改它不影响库里的数据。
        ck3[0].Name = "changed";
        ChecksContext.Check(
            "缓存：快照返回副本",
            cache.TryGet("steam:502", out var stored) && stored.Name == "B");
    }

    private static void CheckClear(
        IFileSystem fileSystem,
        string root,
        IClock clock,
        IAppLogger logger)
    {
        var cache = CreateCache(fileSystem, logger, clock, Path.Combine(root, "clear"));
        cache.Upsert(new WorkshopInfoCacheEntry { Source = "steam", Id = "700", Name = "Clear me" });
        ChecksContext.Equal("缓存：清空前有条目", 1, cache.Count);

        cache.Clear();
        ChecksContext.Equal("缓存：清空后条目数归零", 0, cache.Count);
        ChecksContext.Check("缓存：清空后取不到", !cache.TryGet("steam:700", out _));
    }

    /// <summary>v1 的 workshop-info.json 现在只剩留档意义：放在目录里也不能被读进真表。</summary>
    private static void CheckLegacyIndexIgnored(
        IFileSystem fileSystem,
        string root,
        IClock clock,
        IAppLogger logger)
    {
        var legacyRoot = Path.Combine(root, "legacy-file");
        fileSystem.EnsureDirectory(legacyRoot);
        fileSystem.WriteAllTextAtomic(
            Path.Combine(legacyRoot, WorkshopInfoCache.IndexFileName),
            "{\"entries\":{\"steam:800\":{\"source\":\"steam\",\"id\":\"800\",\"name\":\"Old\"}}}");

        var cache = CreateCache(fileSystem, logger, clock, legacyRoot);
        ChecksContext.Equal("缓存：旧索引文件不再被读取", 0, cache.Count);

        cache.Upsert(new WorkshopInfoCacheEntry { Source = "pdx", Id = "801", Name = "Recovered" });
        ChecksContext.Check(
            "缓存：旧文件存在时仍可写",
            cache.TryGet("pdx:801", out var entry) && entry.Name == "Recovered");
    }

    private static WorkshopInfoCache CreateCache(
        IFileSystem fileSystem,
        IAppLogger logger,
        IClock clock,
        string root)
    {
        var database = new PdxDatabase(fileSystem, logger, Path.Combine(root, PdxDatabase.FileName));
        return new WorkshopInfoCache(fileSystem, logger, clock, root, new SqliteWorkshopItemStore(database, logger));
    }

    private sealed class MutableClock : IClock
    {
        private DateTimeOffset _now;

        public MutableClock(DateTimeOffset now) => _now = now;

        public DateTimeOffset UtcNow => _now;

        public void Advance(TimeSpan delta) => _now += delta;
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
