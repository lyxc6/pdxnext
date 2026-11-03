namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Steam;

/// <summary>
/// 工坊状态内存缓存（<see cref="WorkshopStateCache"/>）的自检：
/// 键、写读、新鲜期、过期回退、失效、清空。
/// </summary>
public static class WorkshopStateCacheChecks
{
    /// <summary>运行本组检查。</summary>
    /// <param name="fileSystem">文件系统（本缓存只在内存，参数保留以统一入口）。</param>
    /// <returns>0 表示全部通过。</returns>
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;
        var now = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        var ttl = TimeSpan.FromSeconds(60);

        try
        {
            CheckKey();
            CheckSetAndGet(now, ttl);
            CheckExpiry(now, ttl);
            CheckInvalidate(now, ttl);
            CheckPerAppIsolation(now, ttl);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ChecksContext.Check("状态缓存：未抛出异常", false, ex.Message);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckKey()
    {
        ChecksContext.Equal("状态缓存键：appid 拼在前缀后", "steam/1158310", WorkshopStateCache.Key(1158310));
        ChecksContext.Check(
            "状态缓存键：不同 appid 不同键",
            WorkshopStateCache.Key(1158310) != WorkshopStateCache.Key(281990));
    }

    private static void CheckSetAndGet(DateTimeOffset now, TimeSpan ttl)
    {
        var cache = new WorkshopStateCache(ttl, () => now);

        ChecksContext.Check("状态缓存：初始未命中", !cache.TryGet(1, out _));

        cache.Set(1, new Dictionary<ulong, WorkshopInstallState>
        {
            [100] = new(true, true, false, null),
            [200] = new(true, false, true, null),
        });

        ChecksContext.Check("状态缓存：写入后命中", cache.TryGet(1, out var states));
        ChecksContext.Equal("状态缓存：条数", 2, states.Count);
        ChecksContext.Equal("状态缓存：第一条已安装", true, states[100].IsInstalled);
        ChecksContext.Equal("状态缓存：第二条在下载", true, states[200].IsDownloading);

        // 写进去之后再改原字典，不该影响缓存里的副本。
        var source = new Dictionary<ulong, WorkshopInstallState> { [300] = new(true, true, false, null) };
        cache.Set(2, source);
        source[300] = WorkshopInstallState.Unknown;
        ChecksContext.Check("状态缓存：写入时做副本", cache.TryGet(2, out var copied));
        ChecksContext.Equal("状态缓存：副本未被外部改动污染", true, copied[300].IsInstalled);
    }

    private static void CheckExpiry(DateTimeOffset now, TimeSpan ttl)
    {
        var clock = now;
        var cache = new WorkshopStateCache(ttl, () => clock);
        cache.Set(1, new Dictionary<ulong, WorkshopInstallState> { [100] = new(true, true, false, null) });

        clock = clock.AddSeconds(30);
        ChecksContext.Check("状态缓存：30 秒内仍新鲜", cache.TryGet(1, out _));

        clock = clock.AddSeconds(31);
        ChecksContext.Check("状态缓存：超过 TTL 不再新鲜", !cache.TryGet(1, out _));
        ChecksContext.Check("状态缓存：过期后仍可回退取旧值", cache.TryGetStale(1, out var stale));
        ChecksContext.Equal("状态缓存：旧值仍是上次写的内容", true, stale[100].IsInstalled);
    }

    private static void CheckInvalidate(DateTimeOffset now, TimeSpan ttl)
    {
        var cache = new WorkshopStateCache(ttl, () => now);
        cache.Set(1, new Dictionary<ulong, WorkshopInstallState> { [100] = new(true, true, false, null) });

        cache.Invalidate(1);

        ChecksContext.Check("状态缓存：Invalidate 后不新鲜", !cache.TryGet(1, out _));
        ChecksContext.Check("状态缓存：Invalidate 后也没有旧值", !cache.TryGetStale(1, out _));

        cache.Set(1, new Dictionary<ulong, WorkshopInstallState>());
        cache.Clear();
        ChecksContext.Check("状态缓存：Clear 清空全部", !cache.TryGetStale(1, out _));
    }

    private static void CheckPerAppIsolation(DateTimeOffset now, TimeSpan ttl)
    {
        var cache = new WorkshopStateCache(ttl, () => now);
        cache.Set(1, new Dictionary<ulong, WorkshopInstallState> { [100] = new(true, true, false, null) });
        cache.Set(2, new Dictionary<ulong, WorkshopInstallState> { [200] = new(true, false, true, null) });

        var hasOne = cache.TryGet(1, out var one);
        var hasTwo = cache.TryGet(2, out var two);

        ChecksContext.Check("状态缓存：appid 各自独立", hasOne && hasTwo);
        ChecksContext.Equal("状态缓存：appid=1 只有自己的条目", 1, one.Count);
        ChecksContext.Equal("状态缓存：appid=2 只有自己的条目", 1, two.Count);

        cache.Invalidate(1);
        ChecksContext.Check("状态缓存：失效一个不影响另一个", !cache.TryGet(1, out _) && cache.TryGet(2, out _));
    }
}
