namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Storage;

/// <summary>
/// <see cref="LegacyCacheCleaner"/> 的自检：只删「已废弃路径 + 旧命名截图」，
/// 现行缓存一个都不能碰；没有可删的东西时不写盘、不抛异常。
/// </summary>
public static class LegacyCacheCleanerChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-legacy-checks-" + Guid.NewGuid().ToString("N"));

        try
        {
            var dataRoot = Path.Combine(tempRoot, "data");
            var workshopRoot = Path.Combine(dataRoot, "workshop-info");
            var logger = new NullLogger();
            var cleaner = new LegacyCacheCleaner(fs, logger, dataRoot, workshopRoot);

            CheckEmptyIsNoOp(fs, cleaner);
            CheckLegacyDirectories(fs, dataRoot, workshopRoot, logger);
            CheckShotNames(fs, dataRoot, workshopRoot, logger);
            CheckCurrentCachesUntouched(fs, dataRoot, workshopRoot, logger);
        }
        finally
        {
            try
            {
                if (fs.DirectoryExists(tempRoot))
                {
                    fs.Delete(tempRoot);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 临时目录删不掉不影响断言结果。
            }
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckEmptyIsNoOp(IFileSystem fs, LegacyCacheCleaner cleaner)
    {
        var result = cleaner.Run();
        ChecksContext.Check("遗留清理：无货可删时报告 Nothing", result == LegacyCacheCleanupResult.Nothing);
        ChecksContext.Check("遗留清理：无货可删时 HasWork 为 false", !result.HasWork);
    }

    private static void CheckLegacyDirectories(
        IFileSystem fs,
        string dataRoot,
        string workshopRoot,
        IAppLogger logger)
    {
        fs.WriteAllTextAtomic(Path.Combine(dataRoot, "workshop-covers", "steam", "111.png"), "cover-111");
        fs.WriteAllTextAtomic(Path.Combine(dataRoot, "workshop-covers", "pdx", "222.jpg"), "cover-222");
        fs.WriteAllTextAtomic(Path.Combine(dataRoot, "online-mod-covers", "333.jpg"), "cover-333");

        var cleaner = new LegacyCacheCleaner(fs, logger, dataRoot, workshopRoot);
        var result = cleaner.Run();

        ChecksContext.Equal("遗留清理：删掉 2 个废弃目录", 2, result.Directories);
        ChecksContext.Equal("遗留清理：删掉 3 个文件", 3, result.Files);
        ChecksContext.Check("遗留清理：workshop-covers 已不存在", !fs.DirectoryExists(Path.Combine(dataRoot, "workshop-covers")));
        ChecksContext.Check("遗留清理：online-mod-covers 已不存在", !fs.DirectoryExists(Path.Combine(dataRoot, "online-mod-covers")));
        ChecksContext.Check("遗留清理：HasWork 为 true", result.HasWork);

        var again = cleaner.Run();
        ChecksContext.Check("遗留清理：重复执行不再删任何东西", !again.HasWork);
    }

    private static void CheckShotNames(
        IFileSystem fs,
        string dataRoot,
        string workshopRoot,
        IAppLogger logger)
    {
        var shots = Path.Combine(workshopRoot, "shots", "steam_123");
        fs.WriteAllTextAtomic(Path.Combine(shots, "shot_00.png"), "legacy");
        fs.WriteAllTextAtomic(Path.Combine(shots, "shot_07.jpg"), "legacy");
        fs.WriteAllTextAtomic(Path.Combine(shots, "shot_0c5d0dbc.png"), "current");
        fs.WriteAllTextAtomic(Path.Combine(shots, "shot_63fb92a1.gif"), "current");

        var cleaner = new LegacyCacheCleaner(fs, logger, dataRoot, workshopRoot);
        var result = cleaner.Run();

        ChecksContext.Equal("遗留清理：只删 2 个旧命名截图", 2, result.Files);
        ChecksContext.Check("遗留清理：旧命名 shot_00 已删除", !fs.FileExists(Path.Combine(shots, "shot_00.png")));
        ChecksContext.Check("遗留清理：旧命名 shot_07 已删除", !fs.FileExists(Path.Combine(shots, "shot_07.jpg")));
        ChecksContext.Check(
            "遗留清理：URL 哈希命名的截图保留",
            fs.FileExists(Path.Combine(shots, "shot_0c5d0dbc.png")) &&
            fs.FileExists(Path.Combine(shots, "shot_63fb92a1.gif")));
    }

    private static void CheckCurrentCachesUntouched(
        IFileSystem fs,
        string dataRoot,
        string workshopRoot,
        IAppLogger logger)
    {
        var current = Path.Combine(workshopRoot, "steam", "444.png");
        fs.WriteAllTextAtomic(current, "current-cover");
        fs.WriteAllTextAtomic(Path.Combine(dataRoot, "theme-cache", "ck3-background.png"), "theme");
        fs.WriteAllTextAtomic(Path.Combine(dataRoot, "mod-cache", "mod-thumbnails.json"), "{}");

        var cleaner = new LegacyCacheCleaner(fs, logger, dataRoot, workshopRoot);
        var result = cleaner.Run();

        ChecksContext.Check("遗留清理：现行缓存不会被误删（无删除动作）", !result.HasWork);
        ChecksContext.Check("遗留清理：workshop-info 封面保留", fs.FileExists(current));
        ChecksContext.Check("遗留清理：theme-cache 保留", fs.FileExists(Path.Combine(dataRoot, "theme-cache", "ck3-background.png")));
        ChecksContext.Check("遗留清理：mod-cache 保留", fs.FileExists(Path.Combine(dataRoot, "mod-cache", "mod-thumbnails.json")));
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
