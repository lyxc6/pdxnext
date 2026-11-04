namespace PdxLauncher.Checks;

using System.Text;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 日志尾部读取与统计的自检：只取尾部、截断不产生乱码/半行、写入方持有句柄时仍可读、
/// 级别过滤保留堆栈续行、归档数量统计正确。这些错了不会崩，只会在性能页显示乱码或漏掉报错，
/// 所以边界口径由这里钉住。
/// </summary>
public static class LogTailReaderChecks
{
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;

        CheckTail();
        CheckTruncation();
        CheckSharedHandle();
        CheckFilter();
        CheckMeasure();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckTail()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "pdxnext.log");
            File.WriteAllText(path, BuildLog(5), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            string tail = LogTailReader.ReadTail(path, 3);
            string[] lines = tail.Split('\n');

            ChecksContext.Equal("日志尾部：只取最后三行", 3, lines.Length);
            ChecksContext.Check(
                "日志尾部：最后一行是第 5 条",
                lines.Length == 3 && lines[2].Contains("消息 004", StringComparison.Ordinal),
                lines.Length == 3 ? lines[2] : tail);
            ChecksContext.Equal("日志尾部：缺失文件返回空", string.Empty, LogTailReader.ReadTail(Path.Combine(directory, "missing.log")));
            ChecksContext.Equal("日志尾部：空路径返回空", string.Empty, LogTailReader.ReadTail(null));
            ChecksContext.Equal("日志尾部：零行返回空", string.Empty, LogTailReader.ReadTail(path, 0));
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static void CheckTruncation()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "pdxnext.log");
            File.WriteAllText(path, BuildLog(400), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            string tail = LogTailReader.ReadTail(path, 20, 128);
            string[] lines = tail.Split('\n');

            ChecksContext.Check(
                "日志尾部：按字节截断后不出现替换字符",
                !tail.Contains('\uFFFD'),
                tail.Length > 60 ? tail[..60] : tail);
            ChecksContext.Check(
                "日志尾部：截断后每行都是完整日志行",
                lines.Length > 0 && lines.All(IsCompleteLine),
                string.Join(" | ", lines.Select(line => line.Length > 24 ? line[..24] : line)));
            ChecksContext.Check("日志尾部：截断后仍不超过行数上限", lines.Length <= 20, lines.Length.ToString());
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static void CheckSharedHandle()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "pdxnext.log");
            byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(BuildLog(3));

            using (var writer = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete))
            {
                writer.Write(bytes);
                writer.Flush();

                string tail = LogTailReader.ReadTail(path, 10);
                ChecksContext.Check(
                    "日志尾部：写入方持有句柄时仍可读",
                    tail.Contains("消息 002", StringComparison.Ordinal),
                    tail);
            }
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static void CheckFilter()
    {
        const string Text =
            "2026-11-04 12:00:00.000 [INFO] 启动\n" +
            "2026-11-04 12:00:01.000 [WARN] 网络有点慢\n" +
            "   at Foo.Bar()\n" +
            "2026-11-04 12:00:02.000 [ERROR] 请求失败\n" +
            "   at Boom.Baz()\n";

        ChecksContext.Equal(
            "日志过滤：全部级别原样返回",
            Text,
            LogTailReader.Filter(Text, LogLevelFilter.All));
        ChecksContext.Equal("日志过滤：空文本返回空", string.Empty, LogTailReader.Filter(null, LogLevelFilter.Warning));

        string warnings = LogTailReader.Filter(Text, LogLevelFilter.Warning);
        ChecksContext.Check(
            "日志过滤：警告视图保留 WARN 与 ERROR",
            warnings.Contains("[WARN]", StringComparison.Ordinal) && warnings.Contains("[ERROR]", StringComparison.Ordinal),
            warnings);
        ChecksContext.Check("日志过滤：警告视图不含 INFO", !warnings.Contains("[INFO]", StringComparison.Ordinal), warnings);
        ChecksContext.Check(
            "日志过滤：异常堆栈跟随所属块",
            warnings.Contains("   at Foo.Bar()", StringComparison.Ordinal)
                && warnings.Contains("   at Boom.Baz()", StringComparison.Ordinal),
            warnings);

        string errors = LogTailReader.Filter(Text, LogLevelFilter.Error);
        ChecksContext.Check(
            "日志过滤：错误视图只有 ERROR",
            errors.Contains("[ERROR]", StringComparison.Ordinal) && !errors.Contains("[WARN]", StringComparison.Ordinal),
            errors);
        ChecksContext.Check(
            "日志过滤：错误视图保留自己的堆栈",
            errors.Contains("   at Boom.Baz()", StringComparison.Ordinal) && !errors.Contains("   at Foo.Bar()", StringComparison.Ordinal),
            errors);
    }

    private static void CheckMeasure()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "pdxnext.log");
            File.WriteAllBytes(path, new byte[10]);
            File.WriteAllBytes(path + ".1", new byte[20]);
            File.WriteAllBytes(path + ".2", new byte[30]);
            File.WriteAllBytes(path + ".3", new byte[40]);
            File.WriteAllBytes(path + ".4", new byte[50]);

            var metrics = LogTailReader.Measure(path);

            ChecksContext.Equal("日志统计：当前文件字节", 10L, metrics.CurrentBytes);
            ChecksContext.Equal("日志统计：归档只算 .1-.3", 3, metrics.ArchiveCount);
            ChecksContext.Equal("日志统计：合计不含 .4", 100L, metrics.TotalBytes);
            ChecksContext.Check("日志统计：主文件有最后写入时间", metrics.LastWriteUtc is not null);
            ChecksContext.Equal("日志统计：缺失目录返回空统计", LogMetrics.Empty, LogTailReader.Measure(Path.Combine(directory, "nope", "pdxnext.log")));
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static string BuildLog(int count)
    {
        var builder = new StringBuilder();
        var start = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Local);
        for (int index = 0; index < count; index++)
        {
            builder.Append(start.AddMilliseconds(index * 10).ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" [INFO] 消息 ")
                .Append(index.ToString("000", System.Globalization.CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return builder.ToString();
    }

    private static bool IsCompleteLine(string line) =>
        line.Length >= 25 && char.IsAsciiDigit(line[0]) && line[4] == '-' && line[10] == ' ' && line[23] == ' ' && line[24] == '[';

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "pdxnext-tail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响检查结论。
        }
    }
}
