namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>
/// 工坊条目真表 <see cref="SqliteWorkshopItemStore"/> 的自检：v1 文档 JSON 的一次性搬迁、
/// 迁移幂等、按来源 + id 的增删改查、列表刷新不抹详情、容量淘汰。
/// <para>
/// 这一层替代了原来「一整段 JSON 每次全量重写」的索引；迁移写错会静默丢缓存，
/// 所以每条路径都用真库（临时目录里的 pdxnext.sqlite）跑一遍。
/// </para>
/// </summary>
public static class WorkshopItemStoreChecks
{
    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();

        CheckMigration(fs, logger);
        CheckCrud(fs, logger);
        CheckTrim(fs, logger);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>旧 documents 里的一段索引会被导进真表，导完删旧行并写闸门；再开一次不重复导。</summary>
    private static void CheckMigration(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[WorkshopItemStoreChecks] 旧索引迁移");
        var root = PlaysetChecks.CreateTempRoot(fs, "workshop-items-migrate");
        try
        {
            var path = Path.Combine(root, PdxDatabase.FileName);
            var legacyJson =
                "{\"version\":1,\"entries\":{" +
                "\"steam:111\":{\"source\":\"steam\",\"id\":\"111\",\"gameId\":\"stellaris\"," +
                "\"name\":\"Old Mod\",\"author\":\"A\",\"rating\":4.5,\"subscriptions\":10," +
                "\"fetchedUtc\":\"2025-01-02T03:04:05+00:00\",\"detailText\":\"old detail\"," +
                "\"tags\":[\"a\",\"b\"],\"views\":9,\"lifetimeSubscriptions\":3}}}";

            using (var raw = SqliteConnection.OpenOrCreate(path))
            {
                raw.Execute("CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
                raw.Execute(
                    "CREATE TABLE IF NOT EXISTS documents (" +
                    "key TEXT PRIMARY KEY, json TEXT NOT NULL, updatedAt INTEGER NOT NULL)");
                raw.Execute(
                    "INSERT INTO meta (key, value) VALUES ('schema_version', '1') " +
                    "ON CONFLICT(key) DO UPDATE SET value = excluded.value");
                raw.Execute(
                    "INSERT INTO documents (key, json, updatedAt) VALUES (?, ?, 0)",
                    JsonDocumentKeys.WorkshopInfo,
                    legacyJson);
            }

            var database = new PdxDatabase(fs, logger, path);
            using (database.Open())
            {
                // 打开即迁移。
            }

            var store = new SqliteWorkshopItemStore(database, logger);
            ChecksContext.Equal("迁移后条目数", 1, store.Count);

            var entry = store.Find("steam:111");
            ChecksContext.Check("迁移后能按 key 找到", entry is not null);
            ChecksContext.Equal("迁移保留名称", "Old Mod", entry?.Name ?? "<null>");
            ChecksContext.Equal("迁移保留详情", "old detail", entry?.DetailText ?? "<null>");
            ChecksContext.Equal("迁移保留标签数", 2, entry?.Tags?.Count ?? -1);
            ChecksContext.Equal("迁移保留评分", 4.5, entry?.Rating ?? -1);

            using var probe = SqliteConnection.OpenReadOnly(path);
            ChecksContext.Equal(
                "旧 documents 行已删除",
                0L,
                probe.ExecuteScalar("SELECT COUNT(*) FROM documents WHERE key = ?", JsonDocumentKeys.WorkshopInfo) as long? ?? -1L);
            ChecksContext.Equal(
                "迁移闸门已写入",
                "done",
                probe.ExecuteScalar("SELECT value FROM meta WHERE key = 'workshop-import-v2'") as string ?? "<null>");

            using (database.Open())
            {
                // 再开一次：闸门挡住，不重复导入。
            }

            ChecksContext.Equal("二次打开不重复导入", 1, store.Count);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>增改查：列表刷新不抹详情，详情写入不抹元数据。</summary>
    private static void CheckCrud(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[WorkshopItemStoreChecks] 增改查");
        var root = PlaysetChecks.CreateTempRoot(fs, "workshop-items-crud");
        try
        {
            var database = new PdxDatabase(fs, logger, Path.Combine(root, PdxDatabase.FileName));
            var store = new SqliteWorkshopItemStore(database, logger);
            var now = DateTimeOffset.UtcNow;

            store.Upsert(new WorkshopInfoCacheEntry
            {
                Source = "steam",
                Id = "222",
                GameId = "ck3",
                Name = "新模组",
                Author = "作者",
                Rating = 4.2,
                Subscriptions = 12,
                FetchedUtc = now,
            });

            ChecksContext.Equal("写入后条目数", 1, store.Count);
            var found = store.Find("steam:222");
            ChecksContext.Equal("回读名称", "新模组", found?.Name ?? "<null>");
            ChecksContext.Equal("回读游戏", "ck3", found?.GameId ?? "<null>");

            store.SetDetail("steam:222", "正文", ["shot.png"], "creator-1", ["tag"], 7, 8, now);
            var detailed = store.Find("steam:222");
            ChecksContext.Equal("详情正文", "正文", detailed?.DetailText ?? "<null>");
            ChecksContext.Equal("详情截图数", 1, detailed?.DetailScreenshots?.Count ?? -1);
            ChecksContext.Equal("详情创作者", "creator-1", detailed?.CreatorSteamId ?? "<null>");
            ChecksContext.Equal("详情浏览数", 7L, detailed?.Views ?? -1L);

            store.Upsert(new WorkshopInfoCacheEntry
            {
                Source = "steam",
                Id = "222",
                Name = "改名了",
                FetchedUtc = now.AddMinutes(1),
            });
            var refreshed = store.Find("steam:222");
            ChecksContext.Equal("列表刷新更新名称", "改名了", refreshed?.Name ?? "<null>");
            ChecksContext.Equal("列表刷新保留详情", "正文", refreshed?.DetailText ?? "<null>");
            ChecksContext.Equal("列表刷新保留游戏", "ck3", refreshed?.GameId ?? "<null>");

            store.SetCover("steam:222", "steam/222.png", "https://example.com/c.png", now);
            var covered = store.Find("steam:222");
            ChecksContext.Equal("封面文件名", "steam/222.png", covered?.CoverFileName ?? "<null>");
            ChecksContext.Equal("封面地址", "https://example.com/c.png", covered?.CoverUrl ?? "<null>");

            ChecksContext.Equal("按来源列出", 1, store.List(WorkshopSource.Steam, "ck3", 10).Count);
            ChecksContext.Equal("按其它游戏列出为空", 0, store.List(WorkshopSource.Steam, "v3", 10).Count);

            store.Clear();
            ChecksContext.Equal("清空后条目数", 0, store.Count);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>容量上限按抓取时间淘汰最旧的一条。</summary>
    private static void CheckTrim(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[WorkshopItemStoreChecks] 容量淘汰");
        var root = PlaysetChecks.CreateTempRoot(fs, "workshop-items-trim");
        try
        {
            var database = new PdxDatabase(fs, logger, Path.Combine(root, PdxDatabase.FileName));
            var store = new SqliteWorkshopItemStore(database, logger);
            var now = DateTimeOffset.UtcNow;

            store.Upsert(new WorkshopInfoCacheEntry { Source = "steam", Id = "old", Name = "旧", FetchedUtc = now.AddHours(-2) });
            store.Upsert(new WorkshopInfoCacheEntry { Source = "steam", Id = "new", Name = "新", FetchedUtc = now });

            store.Trim(1);
            ChecksContext.Equal("淘汰后剩一条", 1, store.Count);
            ChecksContext.Check("留下的是最新那条", store.Find("steam:new") is not null);
            ChecksContext.Check("最旧的被删掉", store.Find("steam:old") is null);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
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
