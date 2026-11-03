namespace PdxLauncher.Checks;

using System.IO.Compression;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mods.Sorting;

/// <summary>
/// 足迹扫描自检：<see cref="ModFootprintScanner.Scan"/> / <see cref="ModFootprintScanner.BuildVanillaIndex"/> 的
/// 覆盖 vs 新增判定、replace/ 折叠、垃圾剔除、包装目录探测、zip 扫描与缺失目录兜底。
/// <para>用临时目录构造真实文件（含真实 zip），用例结束清理。</para>
/// </summary>
public static class ModFootprintScannerChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-footprint-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            CheckOverrideVsAdded(fs, tempRoot);
            CheckReplaceCollapse(fs, tempRoot);
            CheckJunkExcluded(fs, tempRoot);
            CheckWrapperDirectory(fs, tempRoot);
            CheckZipScan(fs, tempRoot);
            CheckMissingSource(fs, tempRoot);
            CheckVanillaIndex(fs, tempRoot);
        }
        finally
        {
            if (fs.DirectoryExists(tempRoot))
            {
                fs.Delete(tempRoot);
            }
        }

        return ChecksContext.Failures - before;
    }

    private static void CheckOverrideVsAdded(IFileSystem fs, string tempRoot)
    {
        var modDir = Path.Combine(tempRoot, "override-mod");
        Write(fs, Path.Combine(modDir, "common", "a.txt"));
        Write(fs, Path.Combine(modDir, "common", "b.txt"));
        Write(fs, Path.Combine(modDir, "gfx", "c.dds"));
        var vanilla = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "common/a.txt" };

        var fp = ModFootprintScanner.Scan(fs, Guid.NewGuid(), "M", modDir, null, vanilla);

        ChecksContext.Equal("内容文件总数（不含垃圾）", 3, fp.TotalFiles);
        ChecksContext.Equal("玩法覆盖原版数", 1, fp.GameplayOverrides);
        ChecksContext.Equal("gfx 文件数", 1, fp.GfxFiles);
        ChecksContext.Equal("覆盖原版路径", "common/a.txt", Join(fp.OverriddenPaths));
        ChecksContext.Equal("新增路径", "common/b.txt,gfx/c.dds", Join(fp.AddedPaths));
    }

    private static void CheckReplaceCollapse(IFileSystem fs, string tempRoot)
    {
        var modDir = Path.Combine(tempRoot, "replace-mod");
        Write(fs, Path.Combine(modDir, "localization", "simp_chinese", "replace", "x.yml"));
        var vanilla = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "localization/simp_chinese/x.yml" };

        var fp = ModFootprintScanner.Scan(fs, Guid.NewGuid(), "M", modDir, null, vanilla);

        ChecksContext.Equal("replace/ 段折叠后仍算覆盖原版", "localization/simp_chinese/x.yml", Join(fp.OverriddenPaths));
        ChecksContext.Equal("replace 文件计数", 1, fp.ReplaceFiles);
        ChecksContext.Equal("翻译主导 → loc 计数", 1, fp.LocFiles);
    }

    private static void CheckJunkExcluded(IFileSystem fs, string tempRoot)
    {
        var modDir = Path.Combine(tempRoot, "junk-mod");
        Write(fs, Path.Combine(modDir, "common", "real.txt"));
        Write(fs, Path.Combine(modDir, ".git", "config"));
        Write(fs, Path.Combine(modDir, ".github", "a.yml"));
        Write(fs, Path.Combine(modDir, "README.md"));
        Write(fs, Path.Combine(modDir, "Thumbnail.png"));
        Write(fs, Path.Combine(modDir, "license.txt"));
        Write(fs, Path.Combine(modDir, "changelog.md"));
        Write(fs, Path.Combine(modDir, "credits.txt"));
        Write(fs, Path.Combine(modDir, "description.txt"));
        Write(fs, Path.Combine(modDir, "mymod.mod"));
        var vanilla = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var fp = ModFootprintScanner.Scan(fs, Guid.NewGuid(), "M", modDir, null, vanilla);

        ChecksContext.Equal("垃圾文件与隐藏目录全部剔除", 1, fp.TotalFiles);
        ChecksContext.Equal("只留真实内容", "common/real.txt", Join(fp.AddedPaths));
    }

    private static void CheckWrapperDirectory(IFileSystem fs, string tempRoot)
    {
        var modDir = Path.Combine(tempRoot, "wrapper-mod");
        Write(fs, Path.Combine(modDir, "README.md"));
        Write(fs, Path.Combine(modDir, "MyMod", "common", "a.txt"));
        Write(fs, Path.Combine(modDir, "MyMod", "gfx", "b.dds"));
        var vanilla = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "common/a.txt" };

        var fp = ModFootprintScanner.Scan(fs, Guid.NewGuid(), "M", modDir, null, vanilla);

        ChecksContext.Equal("包装目录探测到内容根", 2, fp.TotalFiles);
        ChecksContext.Equal("包装目录下仍按相对路径判覆盖", "common/a.txt", Join(fp.OverriddenPaths));
        ChecksContext.Equal("包装目录下新增路径", "gfx/b.dds", Join(fp.AddedPaths));
    }

    private static void CheckZipScan(IFileSystem fs, string tempRoot)
    {
        var modDir = Path.Combine(tempRoot, "dir-source");
        Write(fs, Path.Combine(modDir, "common", "from-dir.txt"));
        var zipPath = Path.Combine(tempRoot, "mod.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(zip, "common/from-zip.txt");
            AddEntry(zip, "README.md");
        }

        var fp = ModFootprintScanner.Scan(fs, Guid.NewGuid(), "M", modDir, zipPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        ChecksContext.Equal("有压缩包优先扫压缩包", "common/from-zip.txt", Join(fp.AddedPaths));
        ChecksContext.Equal("zip 内垃圾同样剔除", 1, fp.TotalFiles);
    }

    private static void CheckMissingSource(IFileSystem fs, string tempRoot)
    {
        var fp = ModFootprintScanner.Scan(
            fs,
            Guid.NewGuid(),
            "M",
            Path.Combine(tempRoot, "no-such-dir"),
            Path.Combine(tempRoot, "no-such.zip"),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        ChecksContext.Equal("目录与压缩包都缺失 → 空足迹", 0, fp.TotalFiles);
        ChecksContext.Equal("空足迹无新增路径", 0, fp.AddedPaths.Count);
    }

    private static void CheckVanillaIndex(IFileSystem fs, string tempRoot)
    {
        var contentRoot = Path.Combine(tempRoot, "game");
        Write(fs, Path.Combine(contentRoot, "common", "a.txt"));
        Write(fs, Path.Combine(contentRoot, "gfx", "b.dds"));
        Write(fs, Path.Combine(contentRoot, "settings.txt"));

        var index = ModFootprintScanner.BuildVanillaIndex(fs, contentRoot);

        ChecksContext.Equal("原版清单收录全部相对路径", "common/a.txt,gfx/b.dds,settings.txt", Join(index));
    }

    private static void Write(IFileSystem fs, string path) => fs.WriteAllTextAtomic(path, "x");

    private static void AddEntry(ZipArchive zip, string entryName)
    {
        var entry = zip.CreateEntry(entryName);
        using var stream = entry.Open();
        var bytes = System.Text.Encoding.UTF8.GetBytes("x");
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string Join(IEnumerable<string> paths) =>
        string.Join(",", paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
}
