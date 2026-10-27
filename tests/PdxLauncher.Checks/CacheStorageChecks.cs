namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Storage;

/// <summary>缓存占用统计与整目录清理的自检（设置页「缓存管理」卡片依赖它）。</summary>
public static class CacheStorageChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-cache-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            // --- 缺失路径：占用为空且不抛
            var missing = Path.Combine(tempRoot, "missing");
            var empty = CacheStorage.Measure(fs, missing);
            ChecksContext.Check("缓存: 缺失目录测得 0 字节 0 文件", empty.Bytes == 0 && empty.Files == 0);
            ChecksContext.Equal("缓存: 缺失文件大小按 0", 0L, fs.GetFileSize(missing));

            // --- 单文件
            var single = Path.Combine(tempRoot, "single.json");
            fs.WriteAllTextAtomic(single, "hello");
            var singleUsage = CacheStorage.Measure(fs, single);
            ChecksContext.Equal("缓存: 单文件 1 个文件", 1, singleUsage.Files);
            ChecksContext.Equal("缓存: 单文件字节数", 5L, singleUsage.Bytes);
            ChecksContext.Equal("缓存: GetFileSize 与内容一致", 5L, fs.GetFileSize(single));

            // --- 目录递归求和
            fs.WriteAllTextAtomic(Path.Combine(tempRoot, "root", "a.txt"), "12345");
            fs.WriteAllTextAtomic(Path.Combine(tempRoot, "root", "nested", "b.txt"), "1234567");
            fs.WriteAllTextAtomic(Path.Combine(tempRoot, "root", "nested", "deeper", "c.txt"), "123");
            var dirUsage = CacheStorage.Measure(fs, Path.Combine(tempRoot, "root"));
            ChecksContext.Equal("缓存: 目录递归 3 个文件", 3, dirUsage.Files);
            ChecksContext.Equal("缓存: 目录递归字节数为 15", 15L, dirUsage.Bytes);

            // --- 清理目录是递归删除
            CacheStorage.Clear(fs, Path.Combine(tempRoot, "root"));
            ChecksContext.Check("缓存: 清理后目录不存在", !fs.DirectoryExists(Path.Combine(tempRoot, "root")));
            var afterClear = CacheStorage.Measure(fs, Path.Combine(tempRoot, "root"));
            ChecksContext.Check("缓存: 清理后占用归零", afterClear.Bytes == 0 && afterClear.Files == 0);

            // --- 清理单文件与不存在的路径都不抛
            CacheStorage.Clear(fs, single);
            ChecksContext.Check("缓存: 清理后文件不存在", !fs.FileExists(single));
            CacheStorage.Clear(fs, Path.Combine(tempRoot, "never-existed"));
            ChecksContext.Check("缓存: 清理不存在的路径不抛异常", true);
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
            catch (IOException)
            {
                // 清理失败不影响断言结果
            }
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }
}
