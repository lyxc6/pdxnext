namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 工坊筛选目录缓存（<see cref="WorkshopFilterCatalogCache"/>）与更新策略
/// （<see cref="WorkshopFilterCatalogPolicy"/>）的自检：原子写、坏文件容错、按 appid 隔离、
/// 24h TTL 与「本地版本变化提前刷新」。
/// </summary>
public static class WorkshopFilterCatalogCacheChecks
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>运行本组检查。</summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <returns>0 表示全部通过。</returns>
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;
        var root = Path.Combine(Path.GetTempPath(), "pdx-checks-workshop-filters-" + Guid.NewGuid().ToString("N"));

        try
        {
            var cache = new WorkshopFilterCatalogCache(fileSystem, root);
            CheckRoundTrip(fileSystem, cache);
            CheckRejects(fileSystem, cache);
            CheckPolicy();
            CheckSignature();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ChecksContext.Check("筛选目录缓存：未抛出异常", false, ex.Message);
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

    private static void CheckRoundTrip(IFileSystem fileSystem, WorkshopFilterCatalogCache cache)
    {
        ChecksContext.Equal(
            "筛选目录缓存：文件名是 appid",
            true,
            (cache.PathFor(529340) ?? string.Empty).EndsWith("529340.json", StringComparison.Ordinal));

        var snapshot = new WorkshopFilterCatalogSnapshot(529340, "1.13.2", Now, SampleGroups());
        ChecksContext.Check("筛选目录缓存：保存成功", cache.TrySave(snapshot));

        var loaded = cache.TryLoad(529340);
        ChecksContext.Check("筛选目录缓存：读回非空", loaded is not null);
        if (loaded is null)
        {
            return;
        }

        ChecksContext.Equal("筛选目录缓存：appid", 529340, loaded.AppId);
        ChecksContext.Equal("筛选目录缓存：游戏版本", "1.13.2", loaded.GameVersion);
        ChecksContext.Equal("筛选目录缓存：抓取时刻", Now, loaded.FetchedAtUtc);
        ChecksContext.Equal("筛选目录缓存：分组数", 3, loaded.Groups.Count);
        ChecksContext.Equal("筛选目录缓存：第一组键", "category", loaded.Groups[0].Key);
        ChecksContext.Equal("筛选目录缓存：第一组标签数", 2, loaded.Groups[0].Tags.Count);
        ChecksContext.Equal("筛选目录缓存：标签值", "Gameplay", loaded.Groups[0].Tags[0].Tag);
        ChecksContext.Equal("筛选目录缓存：DLC 组", true, loaded.Groups[2].IsDlc);
        ChecksContext.Equal("筛选目录缓存：DLC 值是 appid", "2071471", loaded.Groups[2].Tags[0].Tag);

        var leftovers = fileSystem
            .EnumerateFiles(cache.DirectoryPath, "*", recursive: false)
            .Where(static path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .ToList();
        ChecksContext.Equal("筛选目录缓存：原子写没有残留 tmp", 0, leftovers.Count);
    }

    private static void CheckRejects(IFileSystem fileSystem, WorkshopFilterCatalogCache cache)
    {
        ChecksContext.Check("筛选目录缓存：没存过时未命中", cache.TryLoad(999999) is null);
        ChecksContext.Equal("筛选目录缓存：appid 0 无路径", true, cache.PathFor(0) is null);

        var empty = new WorkshopFilterCatalogSnapshot(3450310, string.Empty, Now, []);
        ChecksContext.Check("筛选目录缓存：空分组不保存", !cache.TrySave(empty));

        // 坏 JSON。
        var brokenPath = cache.PathFor(3450310)!;
        fileSystem.WriteAllTextAtomic(brokenPath, "{ not json");
        ChecksContext.Check("筛选目录缓存：坏 JSON 视为未命中", cache.TryLoad(3450310) is null);

        // 版本号不符。
        var versionPath = cache.PathFor(3450320)!;
        var validJson = "{\"version\":1,\"appId\":3450320,\"fetchedAtUtc\":\"2026-11-08T12:00:00+00:00\","
                        + "\"groups\":[{\"key\":\"category\",\"title\":\"\",\"isDlc\":false,"
                        + "\"tags\":[{\"tag\":\"Gameplay\",\"displayName\":\"Gameplay\"}]}]}";
        fileSystem.WriteAllTextAtomic(versionPath, validJson);
        ChecksContext.Check("筛选目录缓存：合法文件可读", cache.TryLoad(3450320) is not null);
        fileSystem.WriteAllTextAtomic(versionPath, validJson.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal));
        ChecksContext.Check("筛选目录缓存：schema 不符视为未命中", cache.TryLoad(3450320) is null);

        // 文件名与文件内 appid 不一致。
        var source = cache.PathFor(529340)!;
        var mismatched = cache.PathFor(3450330)!;
        fileSystem.Copy(source, mismatched, overwrite: true);
        ChecksContext.Check("筛选目录缓存：appid 不匹配视为未命中", cache.TryLoad(3450330) is null);
    }

    private static void CheckPolicy()
    {
        var fresh = new WorkshopFilterCatalogSnapshot(529340, "1.13", Now.AddHours(-1), SampleGroups());
        ChecksContext.Check(
            "筛选目录策略：没有快照要刷新",
            WorkshopFilterCatalogPolicy.NeedsRefresh(null, "1.13", Now));
        ChecksContext.Check(
            "筛选目录策略：1 小时内不刷新",
            !WorkshopFilterCatalogPolicy.NeedsRefresh(fresh, "1.13", Now));
        ChecksContext.Check(
            "筛选目录策略：正好 24 小时要刷新",
            WorkshopFilterCatalogPolicy.NeedsRefresh(fresh, "1.13", Now.AddHours(23)));
        ChecksContext.Check(
            "筛选目录策略：本地版本变化提前刷新",
            WorkshopFilterCatalogPolicy.NeedsRefresh(fresh, "1.14", Now));
        ChecksContext.Check(
            "筛选目录策略：本地版本为空时只看 TTL",
            !WorkshopFilterCatalogPolicy.NeedsRefresh(fresh, string.Empty, Now));
        ChecksContext.Check(
            "筛选目录策略：快照版本为空但本地有版本要刷新",
            WorkshopFilterCatalogPolicy.NeedsRefresh(
                new WorkshopFilterCatalogSnapshot(529340, string.Empty, Now.AddHours(-1), SampleGroups()),
                "1.14",
                Now));
        ChecksContext.Check(
            "筛选目录策略：时钟回拨也要刷新",
            WorkshopFilterCatalogPolicy.NeedsRefresh(fresh, "1.13", Now.AddHours(-2)));
    }

    private static void CheckSignature()
    {
        var groups = SampleGroups();
        ChecksContext.Equal(
            "筛选目录签名：同样数据签名一致",
            WorkshopFilterCatalogPolicy.Signature(groups),
            WorkshopFilterCatalogPolicy.Signature(SampleGroups()));
        ChecksContext.Check(
            "筛选目录签名：标签变化后不同",
            !string.Equals(
                WorkshopFilterCatalogPolicy.Signature(groups),
                WorkshopFilterCatalogPolicy.Signature(
                [
                    new("category", string.Empty, [new("Flags", "Flags")]),
                    groups[1],
                    groups[2],
                ]),
                StringComparison.Ordinal));
        ChecksContext.Equal("筛选目录签名：空分组为空串", string.Empty, WorkshopFilterCatalogPolicy.Signature([]));
    }

    private static IReadOnlyList<SteamWorkshopFilterGroup> SampleGroups() =>
    [
        new(
            "category",
            string.Empty,
            [new("Gameplay", "Gameplay"), new("Flags", "Flags")]),
        new(
            "compatibility",
            "Update Compatibility",
            [new("1.13", "1.13")]),
        new(
            "dlc",
            "Required DLC",
            [new("2071471", "American Buildings Pack")],
            IsDlc: true),
    ];
}
