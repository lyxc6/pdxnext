namespace PdxLauncher.Checks;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;

/// <summary>
/// <see cref="PdxDataRoot"/> 与 <see cref="DataRootMigrator"/> 自检：数据目录候选顺序、
/// data-root.txt 覆盖、写不进去时的回退，以及旧 C 盘数据的一次性只读迁移。
/// 全部在临时目录里构造，绝不触碰真实的 %LOCALAPPDATA%\PdxNext。
/// </summary>
public static class DataRootChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-dataroot-" + Guid.NewGuid().ToString("N"));

        try
        {
            fs.EnsureDirectory(tempRoot);
            CheckOverrideFile(fs, tempRoot);
            CheckChooseWritable(fs, tempRoot);
            CheckBlockedDataDirectory(fs, tempRoot);
            CheckMigration(fs, logger, tempRoot);
        }
        finally
        {
            TryDelete(fs, tempRoot);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckOverrideFile(IFileSystem fs, string tempRoot)
    {
        Console.WriteLine("[DataRootChecks] data-root.txt 逃生口");
        var baseDirectory = Path.Combine(tempRoot, "app-override");
        fs.EnsureDirectory(baseDirectory);

        var missing = PdxDataRoot.TryReadOverride(baseDirectory, out var noDirectory);
        ChecksContext.Check("没有 data-root.txt 时返回 false", !missing && noDirectory is null);

        var overridePath = Path.Combine(baseDirectory, PdxDataRoot.OverrideFileName);
        var target = Path.Combine(tempRoot, "custom-data");
        fs.WriteAllTextAtomic(overridePath, target);

        var found = PdxDataRoot.TryReadOverride(baseDirectory, out var directory);
        ChecksContext.Check("绝对路径被采纳", found && directory is not null);
        ChecksContext.Equal("路径被规范化为绝对路径", Path.GetFullPath(target), directory);

        fs.WriteAllTextAtomic(overridePath, "relative-data");
        var relative = PdxDataRoot.TryReadOverride(baseDirectory, out var relativeDirectory);
        ChecksContext.Check("相对路径被拒绝", !relative && relativeDirectory is null);
    }

    private static void CheckChooseWritable(IFileSystem fs, string tempRoot)
    {
        Console.WriteLine("[DataRootChecks] 数据目录候选顺序");
        var baseA = Path.Combine(tempRoot, "app-a");
        fs.EnsureDirectory(baseA);
        var chosen = PdxDataRoot.ChooseWritable(baseA, overrideDirectory: null);
        ChecksContext.Equal(
            "无覆盖时选 <程序目录>\\data",
            Path.Combine(Path.GetFullPath(baseA), PdxDataRoot.DataFolderName),
            chosen);
        ChecksContext.Check("选中的目录已经可写", fs.DirectoryExists(chosen), chosen);

        var baseB = Path.Combine(tempRoot, "app-b");
        fs.EnsureDirectory(baseB);
        var overrideDirectory = Path.Combine(tempRoot, "elsewhere-data");
        ChecksContext.Equal(
            "绝对覆盖路径优先于 data",
            Path.GetFullPath(overrideDirectory),
            PdxDataRoot.ChooseWritable(baseB, overrideDirectory));

        ChecksContext.Equal(
            "相对覆盖路径被忽略",
            Path.Combine(Path.GetFullPath(baseB), PdxDataRoot.DataFolderName),
            PdxDataRoot.ChooseWritable(baseB, "relative-data"));

        var hasOverride = PdxDataRoot.TryReadOverride(AppContext.BaseDirectory, out var appOverride);
        var expected = PdxDataRoot.ChooseWritable(
            AppContext.BaseDirectory,
            hasOverride ? appOverride : null);
        ChecksContext.Equal("进程内 DataRoot 与候选规则一致", expected, PdxDataRoot.Directory);
    }

    private static void CheckBlockedDataDirectory(IFileSystem fs, string tempRoot)
    {
        Console.WriteLine("[DataRootChecks] data 被同名文件占用时回退");
        var baseBlocked = Path.Combine(tempRoot, "app-blocked");
        fs.EnsureDirectory(baseBlocked);
        fs.WriteAllTextAtomic(Path.Combine(baseBlocked, PdxDataRoot.DataFolderName), "occupied");

        var chosen = PdxDataRoot.ChooseWritable(baseBlocked, overrideDirectory: null);
        ChecksContext.Equal(
            "回退到同级 PdxNext-data",
            Path.GetFullPath(Path.Combine(baseBlocked, "..", PdxDataRoot.FallbackFolderName)),
            chosen);
        ChecksContext.Check("回退目录可写", fs.DirectoryExists(chosen), chosen);
    }

    private static void CheckMigration(IFileSystem fs, IAppLogger logger, string tempRoot)
    {
        Console.WriteLine("[DataRootChecks] 旧 C 盘数据迁移");
        var legacy = Path.Combine(tempRoot, "legacy");
        var target = Path.Combine(tempRoot, "data-new");
        fs.WriteAllTextAtomic(Path.Combine(legacy, "settings.json"), "{\"alwaysOnTop\":true}");
        fs.WriteAllTextAtomic(Path.Combine(legacy, "dlc-descriptions.json"), "{\"version\":1}");
        fs.WriteAllTextAtomic(Path.Combine(legacy, "theme-cache", "ck3.assets.json"), "{}");
        fs.WriteAllTextAtomic(Path.Combine(legacy, "theme-cache", "nested", "bg.png"), "png");
        fs.WriteAllTextAtomic(Path.Combine(legacy, "news-cache", "cards.json"), "[]");
        fs.WriteAllTextAtomic(Path.Combine(legacy, "logs", "pdxnext.log"), "log");

        var migrator = new DataRootMigrator(fs, logger, legacy, target);
        var result = migrator.Migrate();
        ChecksContext.Check("首次迁移成功", result.Migrated && result.Failure is null, result.Failure);
        ChecksContext.Equal("迁移文件数（不含日志）", 5, result.FilesCopied);
        ChecksContext.Check("迁移字节数大于 0", result.BytesCopied > 0, result.BytesCopied.ToString(CultureInfo.InvariantCulture));
        ChecksContext.Check("settings.json 已复制", fs.FileExists(Path.Combine(target, "settings.json")));
        ChecksContext.Check("dlc-descriptions.json 已复制", fs.FileExists(Path.Combine(target, "dlc-descriptions.json")));
        ChecksContext.Check("主题缓存子目录结构保留", fs.FileExists(Path.Combine(target, "theme-cache", "nested", "bg.png")));
        ChecksContext.Check("公告缓存已复制", fs.FileExists(Path.Combine(target, "news-cache", "cards.json")));
        ChecksContext.Check("日志不迁移", !fs.FileExists(Path.Combine(target, "logs", "pdxnext.log")));
        ChecksContext.Check("写入迁移标记", fs.FileExists(Path.Combine(target, DataRootMigrator.MarkerFileName)));
        ChecksContext.Check("旧目录原文件保留（只读复制）", fs.FileExists(Path.Combine(legacy, "settings.json")));

        var second = migrator.Migrate();
        ChecksContext.Check("重复迁移幂等", !second.Migrated && second.FilesCopied == 0);

        var legacy2 = Path.Combine(tempRoot, "legacy-2");
        var target2 = Path.Combine(tempRoot, "data-2");
        fs.WriteAllTextAtomic(Path.Combine(legacy2, "settings.json"), "{}");
        fs.WriteAllTextAtomic(Path.Combine(target2, "settings.json"), "{}");
        var skipped = new DataRootMigrator(fs, logger, legacy2, target2).Migrate();
        ChecksContext.Check("目标已有 settings.json 时不覆盖", !skipped.Migrated && skipped.FilesCopied == 0);

        var absent = new DataRootMigrator(
            fs,
            logger,
            Path.Combine(tempRoot, "no-legacy"),
            Path.Combine(tempRoot, "no-target")).Migrate();
        ChecksContext.Check("旧目录不存在时静默返回", !absent.Migrated && absent.Failure is null);

        var sameRoot = new DataRootMigrator(fs, logger, legacy, legacy).Migrate();
        ChecksContext.Check("源与目标相同时不迁移", !sameRoot.Migrated);
    }

    private static void TryDelete(IFileSystem fs, string path)
    {
        try
        {
            if (fs.DirectoryExists(path))
            {
                fs.Delete(path);
            }
        }
        catch (IOException exception)
        {
            Console.WriteLine("  warn 清理临时目录失败：" + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.WriteLine("  warn 清理临时目录无权限：" + exception.Message);
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
