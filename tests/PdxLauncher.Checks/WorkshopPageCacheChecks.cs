namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 创意工坊「页列表」内存缓存（<see cref="WorkshopPageCache"/>）的自检：
/// 键的区分度、写入/读回、新鲜期、覆盖、清空。
/// </summary>
public static class WorkshopPageCacheChecks
{
    /// <summary>运行本组检查。</summary>
    /// <param name="fileSystem">文件系统（本缓存只在内存，参数保留以统一入口）。</param>
    /// <returns>0 表示全部通过。</returns>
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        // 只看本组自己新增的失败：别的检查类先失败不该把这一组也拖下水。
        var before = ChecksContext.Failures;
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero));

        try
        {
            CheckKeys();
            CheckSetAndGet(clock);
            CheckFreshness(clock);
            CheckOverwrite(clock);
            CheckClear(clock);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ChecksContext.Check("页面缓存：未抛出异常", false, ex.Message);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckKeys()
    {
        var a = WorkshopPageCache.Key(WorkshopSource.Steam, "ck3", null, "Trend", 30, 0);
        var b = WorkshopPageCache.Key(WorkshopSource.Steam, "ck3", null, "Trend", 30, 1);
        var c = WorkshopPageCache.Key(WorkshopSource.Steam, "ck3", null, "TopRated", 30, 0);
        var d = WorkshopPageCache.Key(WorkshopSource.Steam, "ck3", null, "Trend", 7, 0);
        var e = WorkshopPageCache.Key(WorkshopSource.Pdx, "ck3", null, "Trend", null, 0);
        var f = WorkshopPageCache.Key(WorkshopSource.Steam, "ck3", "ui", "Trend", 30, 0);
        var g = WorkshopPageCache.Key(WorkshopSource.Steam, "stellaris", null, "Trend", 30, 0);

        ChecksContext.Equal("页面缓存键：基本形态", "steam|ck3||Trend|30|0|30|-|-|-|-|0|0", a);
        ChecksContext.Equal("页面缓存键：PDX 无时间范围", "pdx|ck3||Trend|-|0|30|-|-|-|-|0|0", e);
        ChecksContext.Check("页面缓存键：页码区分", a != b);
        ChecksContext.Check("页面缓存键：排序区分", a != c);
        ChecksContext.Check("页面缓存键：时间范围区分", a != d);
        ChecksContext.Check("页面缓存键：来源区分", a != e);
        ChecksContext.Check("页面缓存键：关键词区分", a != f);
        ChecksContext.Check("页面缓存键：游戏区分", a != g);

        // 左侧筛选栏与每页条数都是新的查询维度：任一不同都必须生成不同的键。
        var pageSize = WorkshopPageCache.Key(WorkshopSource.Steam, "ck3", null, "Trend", 30, 0, 50);
        var required = WorkshopPageCache.Key(
            WorkshopSource.Steam, "ck3", null, "Trend", 30, 0, 30, ["Gameplay"]);
        var excluded = WorkshopPageCache.Key(
            WorkshopSource.Steam, "ck3", null, "Trend", 30, 0, 30, null, ["Gameplay"]);
        var dlc = WorkshopPageCache.Key(
            WorkshopSource.Steam, "ck3", null, "Trend", 30, 0, 30, null, null, [2071471]);
        var incompatible = WorkshopPageCache.Key(
            WorkshopSource.Steam, "ck3", null, "Trend", 30, 0, 30, null, null, null, null, true);
        var special = WorkshopPageCache.Key(
            WorkshopSource.Steam, "ck3", null, "Trend", 30, 0, 30, null, null, null, null, false,
            SteamWorkshopSpecialFilter.InCollections);

        ChecksContext.Check("页面缓存键：每页条数区分", a != pageSize);
        ChecksContext.Check("页面缓存键：必须包含标签区分", a != required);
        ChecksContext.Check("页面缓存键：必须排除标签区分", a != excluded && required != excluded);
        ChecksContext.Check("页面缓存键：所需 DLC 区分", a != dlc);
        ChecksContext.Check("页面缓存键：不兼容开关区分", a != incompatible);
        ChecksContext.Check("页面缓存键：特别筛选区分", a != special && incompatible != special);

        // 标签顺序与重复不影响键（URL 里也是排序去重后的）。
        var orderedFirst = WorkshopPageCache.Key(
            WorkshopSource.Steam, "ck3", null, "Trend", 30, 0, 30, ["Gameplay", "Flags"]);
        var orderedSecond = WorkshopPageCache.Key(
            WorkshopSource.Steam, "ck3", null, "Trend", 30, 0, 30, ["Flags", "Gameplay", "Flags"]);
        ChecksContext.Equal("页面缓存键：标签排序去重", orderedFirst, orderedSecond);
    }

    private static void CheckSetAndGet(MutableClock clock)
    {
        var cache = new WorkshopPageCache(clock);

        ChecksContext.Check(
            "页面缓存：未存过时不命中",
            !cache.TryGet(WorkshopPageCache.Key(WorkshopSource.Steam, "ck3", null, "Trend", 30, 0), out _));

        var key = WorkshopPageCache.Key(WorkshopSource.Steam, "ck3", null, "Trend", 30, 0);
        cache.Set(key, [MakeEntry("2216623537", "Renovato")], totalCount: 17718, totalPages: 591);

        ChecksContext.Check("页面缓存：存过即命中", cache.TryGet(key, out var page));
        ChecksContext.Equal("页面缓存：条目保留", 1, page.Items.Count);
        ChecksContext.Equal("页面缓存：条目标题", "Renovato", page.Items[0].Name);
        ChecksContext.Equal("页面缓存：总数保留", 17718, page.TotalCount);
        ChecksContext.Equal("页面缓存：总页数保留", 591, page.TotalPages);
        ChecksContext.Equal("页面缓存：抓取时刻", clock.UtcNow, page.FetchedUtc);
        ChecksContext.Equal("页面缓存：只影响自己那条", 1, cache.Count);
    }

    private static void CheckFreshness(MutableClock clock)
    {
        var cache = new WorkshopPageCache(clock);
        var key = WorkshopPageCache.Key(WorkshopSource.Pdx, "cities_skylines_2", "road", "Trend", null, 0);
        cache.Set(key, [MakeEntry("9001", "Road Fix")], totalCount: 27379, totalPages: 1141);

        ChecksContext.Check("页面缓存：刚存算新鲜", cache.TryGet(key, out var fresh) && cache.IsFresh(fresh));

        // 断言按 DefaultTtl 现算，而不是把「5 分钟」写死：这个常量本来就会按实测调整
        // （5 分钟 → 15 分钟那次改动就曾让写死的断言红掉），写死等于给每次调参埋一颗雷。
        clock.Advance(WorkshopPageCache.DefaultTtl - TimeSpan.FromMinutes(1));
        ChecksContext.Check(
            "页面缓存：TTL 内仍新鲜",
            cache.TryGet(key, out var later) && cache.IsFresh(later));

        clock.Advance(TimeSpan.FromMinutes(2));
        ChecksContext.Check(
            "页面缓存：超过 TTL 失效",
            cache.TryGet(key, out var stale) && !cache.IsFresh(stale));

        ChecksContext.Check(
            "页面缓存：自定义新鲜期覆盖默认值",
            cache.TryGet(key, out var custom)
            && cache.IsFresh(custom, WorkshopPageCache.DefaultTtl * 2)
            && !cache.IsFresh(custom));
    }

    private static void CheckOverwrite(MutableClock clock)
    {
        var cache = new WorkshopPageCache(clock);
        var key = WorkshopPageCache.Key(WorkshopSource.Steam, "ck3", null, "Trend", 30, 0);

        cache.Set(key, [MakeEntry("1", "A")], totalCount: 10, totalPages: 1);
        cache.Set(key, [MakeEntry("2", "B")], totalCount: 20, totalPages: 1);

        ChecksContext.Equal("页面缓存：同键覆盖不增条目", 1, cache.Count);
        ChecksContext.Check(
            "页面缓存：同键覆盖取最新值",
            cache.TryGet(key, out var page) && page.TotalCount == 20 && page.Items[0].Name == "B");
    }

    private static void CheckClear(MutableClock clock)
    {
        var cache = new WorkshopPageCache(clock);
        var key = WorkshopPageCache.Key(WorkshopSource.Steam, "ck3", null, "Trend", 30, 0);
        cache.Set(key, [MakeEntry("1", "A")], totalCount: 1, totalPages: 1);
        ChecksContext.Equal("页面缓存：清空前有条目", 1, cache.Count);

        cache.Clear();
        ChecksContext.Equal("页面缓存：清空后为 0", 0, cache.Count);
        ChecksContext.Check("页面缓存：清空后不命中", !cache.TryGet(key, out _));
    }

    private static WorkshopInfoCacheEntry MakeEntry(string id, string name) =>
        new()
        {
            Source = "steam",
            Id = id,
            Name = name,
            Author = "7656119",
            Rating = 4.5,
            RatingsTotal = 10,
            Subscriptions = 100,
            UpdatedUtc = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
            SizeBytes = 1024,
            CoverUrl = "https://images.steamusercontent.com/" + id + ".jpg",
        };

    private sealed class MutableClock(DateTimeOffset start) : IClock
    {
        private DateTimeOffset _now = start;

        public DateTimeOffset UtcNow => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
