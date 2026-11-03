namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>
/// 工坊分页真表 <see cref="SqliteWorkshopPageStore"/> 的自检：v1 文档 JSON 搬迁、payload 往返、
/// 按抓取时间淘汰、清空。条目页缓存是详情页最重的一份数据，搬错会让每次进详情都重新抓 HTML。
/// </summary>
public static class WorkshopPageStoreChecks
{
    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();

        CheckMigration(fs, logger);
        CheckRoundtrip(fs, logger);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>旧 documents 里的分页索引会导进 workshop_pages，导完删旧行。</summary>
    private static void CheckMigration(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[WorkshopPageStoreChecks] 旧索引迁移");
        var root = PlaysetChecks.CreateTempRoot(fs, "workshop-pages-migrate");
        try
        {
            var path = Path.Combine(root, PdxDatabase.FileName);
            var legacyJson =
                "{\"version\":1,\"entries\":{" +
                "\"456\":{\"fetchedUtc\":\"2025-02-03T04:05:06+00:00\",\"authorName\":\"甲\"," +
                "\"descriptionHtml\":\"<p>hi</p>\",\"imageUrls\":[\"i.png\"]," +
                "\"screenshotUrls\":[],\"totalComments\":2," +
                "\"comments\":[{\"id\":\"c1\",\"authorName\":\"乙\",\"authorProfileUrl\":null," +
                "\"postedUtc\":\"2025-02-03T04:05:06+00:00\",\"textHtml\":\"好\",\"textPlain\":\"好\"}]}}}";

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
                    JsonDocumentKeys.WorkshopPages,
                    legacyJson);
            }

            var database = new PdxDatabase(fs, logger, path);
            using (database.Open())
            {
                // 打开即迁移。
            }

            var store = new SqliteWorkshopPageStore(database, logger);
            ChecksContext.Equal("迁移后分页数", 1, store.Count);
            var page = store.Find("456");
            ChecksContext.Equal("迁移保留作者", "甲", page?.AuthorName ?? "<null>");
            ChecksContext.Equal("迁移保留描述", "<p>hi</p>", page?.DescriptionHtml ?? "<null>");
            ChecksContext.Equal("迁移保留评论数", 1, page?.Comments?.Count ?? -1);
            ChecksContext.Equal(
                "迁移保留抓取时间",
                new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero),
                page?.FetchedUtc ?? default);

            using var probe = SqliteConnection.OpenReadOnly(path);
            ChecksContext.Equal(
                "旧 documents 行已删除",
                0L,
                probe.ExecuteScalar("SELECT COUNT(*) FROM documents WHERE key = ?", JsonDocumentKeys.WorkshopPages) as long? ?? -1L);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>写入 / 回读 / 淘汰 / 清空。</summary>
    private static void CheckRoundtrip(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[WorkshopPageStoreChecks] 往返与淘汰");
        var root = PlaysetChecks.CreateTempRoot(fs, "workshop-pages-roundtrip");
        try
        {
            var database = new PdxDatabase(fs, logger, Path.Combine(root, PdxDatabase.FileName));
            var store = new SqliteWorkshopPageStore(database, logger);
            var now = DateTimeOffset.UtcNow;

            store.Set("111", new CachedSteamItemPage
            {
                FetchedUtc = now,
                AuthorName = "作者",
                DescriptionHtml = "<p>正文</p>",
                ImageUrls = ["a.png"],
                ScreenshotUrls = ["b.png"],
                TotalComments = 3,
                Comments = [new SteamWorkshopComment("c1", "某人", null, now, "<p>不错</p>", "不错")],
            });

            var page = store.Find("111");
            ChecksContext.Equal("回读作者", "作者", page?.AuthorName ?? "<null>");
            ChecksContext.Equal("回读图片数", 1, page?.ImageUrls.Count ?? -1);
            ChecksContext.Equal("回读评论数", 1, page?.Comments.Count ?? -1);

            store.Set("222", new CachedSteamItemPage { FetchedUtc = now.AddHours(-1) });
            store.Trim(1);
            ChecksContext.Equal("淘汰后剩一页", 1, store.Count);
            ChecksContext.Check("留下最新一页", store.Find("111") is not null);
            ChecksContext.Check("最旧一页被删", store.Find("222") is null);

            store.Clear();
            ChecksContext.Equal("清空后为 0", 0, store.Count);
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
