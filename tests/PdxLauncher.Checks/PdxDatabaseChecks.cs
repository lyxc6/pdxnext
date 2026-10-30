namespace PdxLauncher.Checks;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>
/// 自有库 <see cref="PdxDatabase"/> 的自检：建表幂等、schema 版本落盘、写事务回滚、
/// 损坏库改名重建。
/// <para>
/// 自有库是这个软件唯一的事实源，它一旦打不开等于启动器没有任何数据。所以这里钉三件事：
/// 同一次运行里反复 <see cref="PdxDatabase.Open"/> 不能把表建坏、不能重复备份；
/// 写事务中途抛异常必须整段回滚（不留半截数据）；
/// 文件被写坏时必须改名留档并重建出可用的空库，而不是让整个软件起不来。
/// </para>
/// </summary>
public static class PdxDatabaseChecks
{
    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();

        CheckSchemaIsIdempotent(fs, logger);
        CheckSchemaVersionWritten(fs, logger);
        CheckWriteRollsBack(fs, logger);
        CheckCorruptDatabaseRecovers(fs, logger);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>反复打开不报错，表集合始终齐全，且不产生多余的 .bak。</summary>
    private static void CheckSchemaIsIdempotent(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[PdxDatabaseChecks] 建表幂等");
        var root = PlaysetChecks.CreateTempRoot(fs, "pdxdb-idempotent");
        try
        {
            var path = Path.Combine(root, PdxDatabase.FileName);
            var database = new PdxDatabase(fs, logger, path);

            for (var i = 0; i < 3; i++)
            {
                using var connection = database.Open();
                ChecksContext.Check("第 " + (i + 1).ToString(CultureInfo.InvariantCulture) + " 次打开库成功", connection is not null);
            }

            using var probe = SqliteConnection.OpenReadOnly(path);
            foreach (var table in ExpectedTables)
            {
                ChecksContext.Check("表已建立：" + table, probe.TableExists(table));
            }

            // 首次建库时 current 版本是 0，没有可备份的内容，因此不该留下 .bak。
            ChecksContext.Check("首次建库不产生 .bak", !fs.FileExists(PdxDatabase.GetBackupPath(path)));
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>meta.schema_version 落盘且等于当前版本。</summary>
    private static void CheckSchemaVersionWritten(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[PdxDatabaseChecks] schema 版本落盘");
        var root = PlaysetChecks.CreateTempRoot(fs, "pdxdb-version");
        try
        {
            var database = new PdxDatabase(fs, logger, Path.Combine(root, PdxDatabase.FileName));
            using (database.Open())
            {
            }

            ChecksContext.Equal(
                "meta 里的 schema_version",
                PdxDatabase.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                database.ReadMeta(PdxDatabase.SchemaVersionKey) ?? "<null>");
            ChecksContext.Check("HasMeta 认 schema_version", database.HasMeta(PdxDatabase.SchemaVersionKey));

            database.WriteMeta("probe", "1");
            ChecksContext.Equal("写 meta", "1", database.ReadMeta("probe") ?? "<null>");
            database.WriteMeta("probe", "2");
            ChecksContext.Equal("覆盖 meta", "2", database.ReadMeta("probe") ?? "<null>");
            ChecksContext.Check("删 meta 成功", database.DeleteMeta("probe"));
            ChecksContext.Check("删后读不到", database.ReadMeta("probe") is null);
            ChecksContext.Check("重复删 meta 也算成功", database.DeleteMeta("probe"));
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>写事务里抛异常必须整段回滚，库内不留半截行。</summary>
    private static void CheckWriteRollsBack(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[PdxDatabaseChecks] 写事务回滚");
        var root = PlaysetChecks.CreateTempRoot(fs, "pdxdb-rollback");
        try
        {
            var database = new PdxDatabase(fs, logger, Path.Combine(root, PdxDatabase.FileName));
            using (database.Open())
            {
            }

            var thrown = false;
            try
            {
                database.Write(connection =>
                {
                    connection.Execute("INSERT INTO playsets (gameId, id, name) VALUES (?, ?, ?)", "ck3", "id-1", "会被回滚");
                    throw new InvalidOperationException("故意失败");
                });
            }
            catch (InvalidOperationException)
            {
                thrown = true;
            }

            ChecksContext.Check("写事务里的异常会向上抛", thrown);

            using var probe = SqliteConnection.OpenReadOnly(database.Path);
            var rows = probe.ExecuteScalar("SELECT COUNT(*) FROM playsets") as long? ?? -1L;
            ChecksContext.Equal("回滚后没有留下半截行", 0L, rows);

            // 事务外的正常写入仍然要能落盘。
            database.Write(connection =>
                connection.Execute("INSERT INTO playsets (gameId, id, name) VALUES (?, ?, ?)", "ck3", "id-2", "正常"));
            using var probe2 = SqliteConnection.OpenReadOnly(database.Path);
            ChecksContext.Equal("正常写入落盘", 1L, probe2.ExecuteScalar("SELECT COUNT(*) FROM playsets") as long? ?? -1L);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>库文件不是 SQLite 时必须改名留档并重建可用空库。</summary>
    private static void CheckCorruptDatabaseRecovers(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[PdxDatabaseChecks] 损坏库自愈");
        var root = PlaysetChecks.CreateTempRoot(fs, "pdxdb-corrupt");
        try
        {
            var path = Path.Combine(root, PdxDatabase.FileName);
            fs.WriteAllTextAtomic(path, "这不是一个 SQLite 文件，只是一段普通文本。");

            var database = new PdxDatabase(fs, logger, path);
            using (database.Open())
            {
            }

            using var probe = SqliteConnection.OpenReadOnly(path);
            ChecksContext.Check("重建后的库能打开", probe.TableExists("mods"));
            ChecksContext.Equal(
                "重建后的库 schema_version 正确",
                PdxDatabase.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                database.ReadMeta(PdxDatabase.SchemaVersionKey) ?? "<null>");

            var corruptFiles = fs.EnumerateFiles(root, PdxDatabase.FileName + ".corrupt-*", recursive: false).ToList();
            ChecksContext.Equal("损坏文件被改名留档 1 份", 1, corruptFiles.Count);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static readonly string[] ExpectedTables =
    [
        "meta",
        "settings",
        "session",
        "documents",
        "mods",
        "playsets",
        "playsets_mods",
        "playsets_dlcs",
    ];

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
