namespace PdxLauncher.Checks;

using System.IO;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Infrastructure;

/// <summary>
/// 文件日志的自检：轮转、归档份数、追加，以及「目录不可写时不抛异常」。
/// 日志写坏了不会崩，只会在用户报问题时拿不到现场，所以口径要钉住。
/// </summary>
public static class FileLoggerChecks
{
    /// <summary>运行全部日志检查。</summary>
    public static int Run(IFileSystem fs)
    {
        CheckRotation(fs);
        CheckAppend(fs);
        CheckUnwritablePath();
        return 0;
    }

    private static void CheckRotation(IFileSystem fs)
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "pdxnext.log");
        try
        {
            // 预置三份旧归档：轮转后应变成 .2/.3，最老的一份被丢弃。
            File.WriteAllText(path + ".1", "old-1");
            File.WriteAllText(path + ".2", "old-2");
            File.WriteAllText(path + ".3", "old-3");

            using (var logger = new FileLogger(path))
            {
                var line = new string('x', 200);
                for (int index = 0; index < 40000; index++)
                {
                    logger.Info(line);
                }
            }

            ChecksContext.Check("日志: 超限后生成 .1", File.Exists(path + ".1"));
            ChecksContext.Check("日志: 旧 .1 后移为 .2", File.Exists(path + ".2"));
            ChecksContext.Check("日志: 旧 .2 后移为 .3", File.Exists(path + ".3"));
            ChecksContext.Check("日志: 最多保留三份归档", !File.Exists(path + ".4"));

            long length = new FileInfo(path).Length;
            ChecksContext.Check("日志: 当前文件已回落到上限内", length <= 5L * 1024 * 1024);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static void CheckAppend(IFileSystem fs)
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "append.log");
        try
        {
            using (var logger = new FileLogger(path))
            {
                logger.Info("first");
            }

            using (var logger = new FileLogger(path))
            {
                logger.Warn("second");
            }

            string text = File.ReadAllText(path);
            ChecksContext.Check("日志: 重启后追加而不是覆盖", text.Contains("first", StringComparison.Ordinal));
            ChecksContext.Check("日志: 新内容写入", text.Contains("second", StringComparison.Ordinal));
            ChecksContext.Check("日志: 级别标记", text.Contains("[WARN]", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static void CheckUnwritablePath()
    {
        // 不存在的盘符：构造与写入都必须静默退化，不能把异常抛给业务。
        var logger = new FileLogger(@"Z:\pdxnext-checks\does-not-exist\log.txt");
        bool threw = false;
        try
        {
            logger.Info("should not throw");
            logger.Error("still not throw", new InvalidOperationException("boom"));
            logger.Dispose();
        }
        catch (Exception)
        {
            threw = true;
        }

        ChecksContext.Check("日志: 不可写路径不抛异常", !threw);
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "pdxnext-log-" + Guid.NewGuid().ToString("N"));
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
