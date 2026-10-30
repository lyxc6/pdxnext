namespace PdxLauncher.Checks;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>
/// 自有库文档存储 <see cref="SqliteJsonDocumentStore"/> 的自检：settings / session 走单行表、
/// 其余走 documents 键、前缀列表与占用统计、删除幂等、损坏 JSON 原样返回。
/// <para>
/// 这一层是「数据全部入库」的地基：settings、登录态与 6 份缓存索引都变成库里的一行 JSON。
/// 它出问题不会抛异常（读不到就当没有），但会让用户设置或登录态静默丢失，所以必须钉住。
/// </para>
/// </summary>
public static class JsonDocumentStoreChecks
{
    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();
        var clock = SystemClock.Instance;

        CheckSingletonTables(fs, logger, clock);
        CheckDocumentKeys(fs, logger, clock);
        CheckMeasureAndList(fs, logger, clock);
        CheckMissingAndDelete(fs, logger, clock);
        CheckSeedFromFiles(fs, logger, clock);
        CheckKeyCatalog();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>settings / session 落在各自的单行表里，同一键反复写只更新那一行。</summary>
    private static void CheckSingletonTables(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[JsonDocumentStoreChecks] 单行表");
        var root = PlaysetChecks.CreateTempRoot(fs, "jsonstore-singleton");
        try
        {
            var database = new PdxDatabase(fs, logger, Path.Combine(root, PdxDatabase.FileName));
            var store = new SqliteJsonDocumentStore(database, logger, clock);

            ChecksContext.Check(
                "写 settings",
                store.Write(JsonDocumentKeys.Settings, "{\"a\":1}"));
            ChecksContext.Check(
                "写 session",
                store.Write(JsonDocumentKeys.Session, "{\"b\":2}"));

            ChecksContext.Check("读回 settings", store.TryRead(JsonDocumentKeys.Settings, out var settings));
            ChecksContext.Equal("settings 内容", "{\"a\":1}", settings ?? "<null>");
            ChecksContext.Check("读回 session", store.TryRead(JsonDocumentKeys.Session, out var session));
            ChecksContext.Equal("session 内容", "{\"b\":2}", session ?? "<null>");

            store.Write(JsonDocumentKeys.Settings, "{\"a\":9}");
            ChecksContext.Check("覆盖 settings", store.TryRead(JsonDocumentKeys.Settings, out var updated));
            ChecksContext.Equal("settings 覆盖后的内容", "{\"a\":9}", updated ?? "<null>");

            using var probe = SqliteConnection.OpenReadOnly(database.Path);
            ChecksContext.Equal(
                "settings 表只有一行",
                1L,
                probe.ExecuteScalar("SELECT COUNT(*) FROM settings") as long? ?? -1L);
            ChecksContext.Equal(
                "session 表只有一行",
                1L,
                probe.ExecuteScalar("SELECT COUNT(*) FROM session") as long? ?? -1L);
            ChecksContext.Equal(
                "documents 表没有被单行键污染",
                0L,
                probe.ExecuteScalar("SELECT COUNT(*) FROM documents") as long? ?? -1L);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>其余键一律落在 documents 表，读写为整份 JSON 往返。</summary>
    private static void CheckDocumentKeys(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[JsonDocumentStoreChecks] documents 键");
        var root = PlaysetChecks.CreateTempRoot(fs, "jsonstore-doc");
        try
        {
            var database = new PdxDatabase(fs, logger, Path.Combine(root, PdxDatabase.FileName));
            var store = new SqliteJsonDocumentStore(database, logger, clock);

            var payload = "{\"封面\":\"https://example.com/a.png\",\"n\":[1,2,3]}";
            ChecksContext.Check("写缓存键", store.Write(JsonDocumentKeys.WorkshopInfo, payload));
            ChecksContext.Check("缓存键存在", store.Exists(JsonDocumentKeys.WorkshopInfo));
            ChecksContext.Check("读回缓存键", store.TryRead(JsonDocumentKeys.WorkshopInfo, out var read));
            ChecksContext.Equal("中文不转义地原样往返", payload, read ?? "<null>");

            // 主题缓存一游戏一键，必须能同时存在多份。
            store.Write(JsonDocumentKeys.ThemeAssets("ck3"), "{\"g\":\"ck3\"}");
            store.Write(JsonDocumentKeys.ThemeAssets("victoria3"), "{\"g\":\"victoria3\"}");
            ChecksContext.Check("ck3 主题键存在", store.Exists(JsonDocumentKeys.ThemeAssets("ck3")));
            ChecksContext.Check(
                "不同游戏的主题键互不覆盖",
                store.TryRead(JsonDocumentKeys.ThemeAssets("victoria3"), out var v3) && v3 == "{\"g\":\"victoria3\"}");
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>MeasureBytes 按前缀求和，List 按键名序返回。</summary>
    private static void CheckMeasureAndList(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[JsonDocumentStoreChecks] 前缀统计与列举");
        var root = PlaysetChecks.CreateTempRoot(fs, "jsonstore-measure");
        try
        {
            var database = new PdxDatabase(fs, logger, Path.Combine(root, PdxDatabase.FileName));
            var store = new SqliteJsonDocumentStore(database, logger, clock);

            store.Write(JsonDocumentKeys.ThemeAssets("ck3"), "{}");
            store.Write(JsonDocumentKeys.ThemeAssets("victoria3"), "{}");
            store.Write(JsonDocumentKeys.NewsCards, "[1,2,3]");

            var keys = store.List(JsonDocumentKeys.CachePrefix + "theme-assets:");
            ChecksContext.Equal("按前缀列出 2 个主题键", 2, keys.Count);
            ChecksContext.Equal("按键名序第一个", JsonDocumentKeys.ThemeAssets("ck3"), keys[0]);
            ChecksContext.Equal("按键名序第二个", JsonDocumentKeys.ThemeAssets("victoria3"), keys[1]);

            var themeBytes = store.MeasureBytes(JsonDocumentKeys.CachePrefix + "theme-assets:");
            ChecksContext.Check("主题键占用等于两份空对象", themeBytes == 4, themeBytes.ToString(CultureInfo.InvariantCulture));
            ChecksContext.Check("无关键不计入主题前缀", themeBytes < store.MeasureBytes(JsonDocumentKeys.CachePrefix));
            ChecksContext.Equal("不存在的前缀占用为 0", 0L, store.MeasureBytes("cache:no-such-prefix"));

            // 键名里带 % 或 _ 时不能当通配符用。
            ChecksContext.Equal("通配符不当前缀用", 0L, store.MeasureBytes("cache:%"));
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>缺键读出 false、删除幂等、坏 JSON 原样返回（解析交给调用方）。</summary>
    private static void CheckMissingAndDelete(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[JsonDocumentStoreChecks] 缺键与删除");
        var root = PlaysetChecks.CreateTempRoot(fs, "jsonstore-missing");
        try
        {
            var database = new PdxDatabase(fs, logger, Path.Combine(root, PdxDatabase.FileName));
            var store = new SqliteJsonDocumentStore(database, logger, clock);

            ChecksContext.Check("缺键读出 false", !store.TryRead(JsonDocumentKeys.Settings, out var missing));
            ChecksContext.Check("缺键时 json 为 null", missing is null);
            ChecksContext.Check("缺键 Exists 为 false", !store.Exists(JsonDocumentKeys.Settings));
            ChecksContext.Check("删不存在的键也成功", store.Delete(JsonDocumentKeys.Settings));

            store.Write(JsonDocumentKeys.WorkshopPages, "{坏掉的 JSON");
            ChecksContext.Check("坏 JSON 也照样存下来", store.TryRead(JsonDocumentKeys.WorkshopPages, out var broken));
            ChecksContext.Equal("坏 JSON 原样返回", "{坏掉的 JSON", broken ?? "<null>");

            ChecksContext.Check("删存在的键", store.Delete(JsonDocumentKeys.WorkshopPages));
            ChecksContext.Check("删后 Exists 为 false", !store.Exists(JsonDocumentKeys.WorkshopPages));
            ChecksContext.Check("重复删同一个键也成功", store.Delete(JsonDocumentKeys.WorkshopPages));
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>用真实文件版本对比键版本：同一份 JSON 两边读出来必须一样。</summary>
    private static void CheckSeedFromFiles(IFileSystem fs, NullLogger logger, IClock clock)
    {
        Console.WriteLine("[JsonDocumentStoreChecks] 文件版与库版一致");
        var root = PlaysetChecks.CreateTempRoot(fs, "jsonstore-parity");
        try
        {
            var path = Path.Combine(root, "settings.json");
            var json = "{\"language\":\"zh-CN\",\"主题\":\"dark\"}";
            fs.WriteAllTextAtomic(path, json);

            var fileDocument = new FileDocumentFile(fs, path);
            ChecksContext.Check("文件版 Exists", fileDocument.Exists());
            ChecksContext.Equal("文件版读回原文", json, fileDocument.ReadText() ?? "<null>");

            var database = new PdxDatabase(fs, logger, Path.Combine(root, PdxDatabase.FileName));
            var store = new SqliteJsonDocumentStore(database, logger, clock);
            var storeDocument = new StoreDocumentFile(store, JsonDocumentKeys.Settings);

            ChecksContext.Check("库版落点的名字是 db: 前缀", storeDocument.DisplayPath.StartsWith("db:", StringComparison.Ordinal), storeDocument.DisplayPath);
            ChecksContext.Check("库版写入", storeDocument.WriteText(fileDocument.ReadText() ?? "{}"));
            ChecksContext.Equal("两边内容一致", fileDocument.ReadText() ?? "<null>", storeDocument.ReadText() ?? "<null>");

            fileDocument.WriteText(json);
            ChecksContext.Equal("文件版覆盖写不被库版影响", json, fileDocument.ReadText() ?? "<null>");
            ChecksContext.Check("文件版删除", fileDocument.Delete());
            ChecksContext.Check("文件版删后 Exists 为 false", !fileDocument.Exists());
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>键常量本身不能重复，也不能被当成普通 documents 键。</summary>
    private static void CheckKeyCatalog()
    {
        Console.WriteLine("[JsonDocumentStoreChecks] 键常量");
        var all = new[]
        {
            JsonDocumentKeys.Settings,
            JsonDocumentKeys.Session,
            JsonDocumentKeys.WorkshopInfo,
            JsonDocumentKeys.WorkshopPages,
            JsonDocumentKeys.NewsCards,
            JsonDocumentKeys.DlcDescriptions,
            JsonDocumentKeys.ModThumbnails,
        };

        ChecksContext.Equal("键常量互不重复", all.Length, all.Distinct(StringComparer.Ordinal).Count());
        ChecksContext.Check("settings 是单行键", JsonDocumentKeys.IsSingleton(JsonDocumentKeys.Settings));
        ChecksContext.Check("session 是单行键", JsonDocumentKeys.IsSingleton(JsonDocumentKeys.Session));
        ChecksContext.Check("缓存键不是单行键", !JsonDocumentKeys.IsSingleton(JsonDocumentKeys.WorkshopInfo));
        ChecksContext.Check(
            "主题键带 cache: 前缀",
            JsonDocumentKeys.ThemeAssets("ck3").StartsWith(JsonDocumentKeys.CachePrefix, StringComparison.Ordinal));
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
