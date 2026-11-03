namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 工坊封面目录配额（<see cref="CoverDiskQuota"/>）的自检：超限时按「最久没写过」删到目标量、
/// 刚下载的文件不删、没超限什么都不做。
/// <para>
/// 配额错了不会崩，只会在真机上表现为「封面目录又涨回 200 MB」或「图刚下载完就被删掉」，
/// 编译器看不见，所以口径必须钉住。
/// </para>
/// </summary>
public static class CoverQuotaChecks
{
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var before = ChecksContext.Failures;

        CheckSelectVictims();
        CheckEnforce(fileSystem);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckSelectVictims()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var age = TimeSpan.FromMinutes(30);

        var files = new List<CoverFile>
        {
            new(@"C:\c\oldest.png", 1024, now - TimeSpan.FromHours(5)),
            new(@"C:\c\older.png", 1024, now - TimeSpan.FromHours(3)),
            new(@"C:\c\old.png", 1024, now - TimeSpan.FromHours(1)),
            new(@"C:\c\fresh.png", 1024, now - TimeSpan.FromMinutes(2)),
        };

        ChecksContext.Check(
            "封面配额：没超目标量不删",
            CoverDiskQuota.SelectVictims(files, 4 * 1024, 4 * 1024, now, age).Count == 0);

        var victims = CoverDiskQuota.SelectVictims(files, 4 * 1024, 2 * 1024, now, age);
        ChecksContext.Equal("封面配额：按最旧优先删", 2, victims.Count);
        ChecksContext.Equal("封面配额：先删最旧的", @"C:\c\oldest.png", victims[0].Path);
        ChecksContext.Equal("封面配额：再删次旧的", @"C:\c\older.png", victims[1].Path);

        var allRecent = files.Select(file => file with { LastWriteUtc = now - TimeSpan.FromMinutes(1) }).ToList();
        ChecksContext.Check(
            "封面配额：刚写过的文件不删",
            CoverDiskQuota.SelectVictims(allRecent, 4 * 1024, 0, now, age).Count == 0);
    }

    private static void CheckEnforce(IFileSystem fs)
    {
        var root = Path.Combine(Path.GetTempPath(), "pdx-cover-quota-" + Guid.NewGuid().ToString("N"));
        fs.EnsureDirectory(root);

        try
        {
            var now = DateTimeOffset.UtcNow;
            var paths = new List<string>();
            for (var i = 0; i < 6; i++)
            {
                var path = Path.Combine(root, "steam", "cover" + i + ".png");
                fs.EnsureDirectory(Path.GetDirectoryName(path) ?? root);
                fs.WriteAllBytesAtomic(path, new byte[1024]);
                File.SetLastWriteTimeUtc(path, (now - TimeSpan.FromHours(6 - i)).UtcDateTime);
                paths.Add(path);
            }

            var clock = new FixedClock(now);
            var quota = new CoverDiskQuota(fs, new SilentLogger(), clock, root, maxBytes: 4 * 1024, targetBytes: 2 * 1024);

            ChecksContext.Equal("封面配额：Measure 汇总目录字节", 6L * 1024, quota.Measure());

            var result = quota.Enforce();
            ChecksContext.Equal("封面配额：超限删到目标量（删 4 个）", 4, result.DeletedFiles);
            ChecksContext.Equal("封面配额：剩余量不超过目标", true, result.TotalBytes <= 2 * 1024);
            ChecksContext.Check("封面配额：最旧的先被删", !fs.FileExists(paths[0]));
            ChecksContext.Check("封面配额：最新的还留着", fs.FileExists(paths[5]));

            var second = quota.Enforce();
            ChecksContext.Equal("封面配额：已达标时再扫不删", 0, second.DeletedFiles);
        }
        finally
        {
            try
            {
                foreach (var file in fs.EnumerateFiles(root, "*", recursive: true).ToList())
                {
                    fs.Delete(file);
                }

                foreach (var directory in fs.EnumerateDirectories(root).ToList())
                {
                    foreach (var file in fs.EnumerateFiles(directory, "*", recursive: true).ToList())
                    {
                        fs.Delete(file);
                    }
                }
            }
            catch (Exception)
            {
                // 临时目录清理失败不影响断言结果。
            }
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class SilentLogger : IAppLogger
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
