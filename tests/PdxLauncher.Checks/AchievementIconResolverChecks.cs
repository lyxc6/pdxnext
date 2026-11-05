namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Achievements;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 成就图标解析与快照缓存的自检。图标是纯磁盘查找，缓存是「坏文件必须降级」，
/// 两者都不能抛异常、不能串号（appid 之间互不污染），所以用真实临时目录 + 真实文件系统钉住。
/// 另外用源码文本断言页面给悬浮滚动条留了右侧空白、并启用了页面缓存，防止 UI 修好又回退。
/// </summary>
public static class AchievementIconResolverChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-achicon-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            CheckResolver(fs, tempRoot);
            CheckCache(fs, Path.Combine(tempRoot, "cache"));
            CheckPageSource();
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

    // ------------------------------------------------------------ 图标解析

    private static void CheckResolver(IFileSystem fs, string tempRoot)
    {
        var gameRoot = Path.Combine(tempRoot, "vic3");
        var gameIcons = Path.Combine(gameRoot, "game", "gfx", "interface", "icons", "achievements");
        fs.WriteAllBytesAtomic(Path.Combine(gameIcons, "ACH_A.jpg"), [1, 2, 3]);
        fs.WriteAllBytesAtomic(Path.Combine(gameIcons, "ACH_A_notachieved.jpg"), [4, 5, 6]);
        fs.WriteAllBytesAtomic(Path.Combine(gameIcons, "ACH_B.jpg"), [7, 8, 9]);
        fs.WriteAllBytesAtomic(Path.Combine(gameIcons, "ACH_OLD.jpg"), [1]);
        fs.WriteAllBytesAtomic(Path.Combine(gameIcons, "ACH_PNG.png"), [2]);

        var unlocked = AchievementIconResolver.Resolve(gameRoot, "ACH_A", unlocked: true);
        ChecksContext.Check(
            "成就图标: 已解锁命中原图",
            unlocked is not null && unlocked.EndsWith("ACH_A.jpg", StringComparison.Ordinal),
            unlocked ?? "null");

        var locked = AchievementIconResolver.Resolve(gameRoot, "ACH_A", unlocked: false);
        ChecksContext.Check(
            "成就图标: 未解锁优先取 _notachieved",
            locked is not null && locked.EndsWith("ACH_A_notachieved.jpg", StringComparison.Ordinal),
            locked ?? "null");

        var noLocked = AchievementIconResolver.Resolve(gameRoot, "ACH_B", unlocked: false);
        ChecksContext.Check(
            "成就图标: 缺 _notachieved 时退回原图",
            noLocked is not null && noLocked.EndsWith("ACH_B.jpg", StringComparison.Ordinal),
            noLocked ?? "null");

        var png = AchievementIconResolver.Resolve(gameRoot, "ACH_PNG", unlocked: true);
        ChecksContext.Check(
            "成就图标: 支持 .png",
            png is not null && png.EndsWith("ACH_PNG.png", StringComparison.Ordinal),
            png ?? "null");

        // 第二种根布局：<install>\gfx\interface\icons\achievements
        var flatRoot = Path.Combine(tempRoot, "flat");
        var flatIcons = Path.Combine(flatRoot, "gfx", "interface", "icons", "achievements");
        fs.WriteAllBytesAtomic(Path.Combine(flatIcons, "ACH_FLAT.jpg"), [3, 3, 3]);
        var flat = AchievementIconResolver.Resolve(flatRoot, "ACH_FLAT", unlocked: true);
        ChecksContext.Check(
            "成就图标: 兼容根目录直挂的 gfx 布局",
            flat is not null && flat.EndsWith("ACH_FLAT.jpg", StringComparison.Ordinal),
            flat ?? "null");

        // 两种布局都存在时 game/ 优先（VIC3 的实际布局）。
        var mixedRoot = Path.Combine(tempRoot, "mixed");
        fs.WriteAllBytesAtomic(Path.Combine(mixedRoot, "game", "gfx", "interface", "icons", "achievements", "ACH_M.jpg"), [1]);
        fs.WriteAllBytesAtomic(Path.Combine(mixedRoot, "gfx", "interface", "icons", "achievements", "ACH_M.jpg"), [2]);
        var mixed = AchievementIconResolver.Resolve(mixedRoot, "ACH_M", unlocked: true);
        ChecksContext.Check(
            "成就图标: 两种布局共存时取 game/ 下的图",
            mixed is not null && mixed.Contains(Path.Combine("game", "gfx"), StringComparison.OrdinalIgnoreCase),
            mixed ?? "null");

        ChecksContext.Check("成就图标: 目录不存在返回 null", AchievementIconResolver.Resolve(tempRoot, "NOPE", true) is null);
        ChecksContext.Check("成就图标: 文件名不存在返回 null", AchievementIconResolver.Resolve(gameRoot, "ACH_MISSING", true) is null);
        ChecksContext.Check("成就图标: null 根目录返回 null", AchievementIconResolver.Resolve(null, "ACH_A", true) is null);
        ChecksContext.Check("成就图标: 空白 apiName 返回 null", AchievementIconResolver.Resolve(gameRoot, "  ", true) is null);
        ChecksContext.Check(
            "成就图标: 拒绝路径穿越",
            AchievementIconResolver.Resolve(gameRoot, "../ACH_A", true) is null
                && AchievementIconResolver.Resolve(gameRoot, "..\\ACH_A", true) is null);
        ChecksContext.Check(
            "成就图标: 拒绝非 jpg/png 扩展名（ACH_OLD 之外的 .txt 不作数）",
            !fs.FileExists(Path.Combine(gameIcons, "ACH_OLD.txt"))
                && AchievementIconResolver.Resolve(gameRoot, "ACH_OLD", true) is not null);
    }

    // ------------------------------------------------------------ 快照缓存

    private static void CheckCache(IFileSystem fs, string cacheDir)
    {
        var cache = new AchievementSnapshotCache(fs, cacheDir);
        var fetched = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var snapshot = Snapshot(fetched, "ACH_A", unlocked: true);

        ChecksContext.Check("成就缓存: 没有文件时读取返回 null", cache.TryLoad("730") is null);
        ChecksContext.Check("成就缓存: 首次写入成功", cache.TrySave("730", snapshot));

        var loaded = cache.TryLoad("730");
        ChecksContext.Check("成就缓存: 往返后能读到", loaded is not null);
        ChecksContext.Equal("成就缓存: 保留取数时间", fetched, loaded?.FetchedAtUtc);
        ChecksContext.Equal("成就缓存: 保留条目数", 1, loaded?.Entries.Count ?? -1);
        ChecksContext.Equal("成就缓存: 保留条目 id", "ACH_A", loaded?.Entries[0].Id);
        ChecksContext.Check("成就缓存: 保留解锁状态", loaded?.Entries[0].IsUnlocked == true);

        // 不同 appid 各写各的，互不污染。
        var other = Snapshot(fetched.AddHours(1), "ACH_VIC3", unlocked: false);
        ChecksContext.Check("成就缓存: 第二个 appid 写入成功", cache.TrySave("236850", other));
        var first = cache.TryLoad("730");
        var second = cache.TryLoad("236850");
        ChecksContext.Equal("成就缓存: appid 切换后第一条仍是自己的条目", "ACH_A", first?.Entries[0].Id);
        ChecksContext.Equal("成就缓存: 第二个 appid 读到自己的条目", "ACH_VIC3", second?.Entries[0].Id);
        ChecksContext.Check(
            "成就缓存: 两个 appid 的文件路径不同",
            !string.Equals(cache.PathFor("730"), cache.PathFor("236850"), StringComparison.OrdinalIgnoreCase));

        // 坏 JSON：返回 null，页面退回正常取数，而不是抛异常。
        var brokenPath = cache.PathFor("999");
        if (brokenPath is not null)
        {
            fs.WriteAllTextAtomic(brokenPath, "{ this is not json");
            ChecksContext.Check("成就缓存: 坏 JSON 降级为 null", cache.TryLoad("999") is null);
        }

        // 版本不符同样视为未命中。
        var oldVersionPath = cache.PathFor("998");
        if (oldVersionPath is not null)
        {
            fs.WriteAllTextAtomic(
                oldVersionPath,
                "{\"version\":0,\"snapshot\":{\"availability\":\"Available\",\"fetchedAtUtc\":\"2026-01-01T00:00:00+00:00\"}}");
            ChecksContext.Check("成就缓存: 旧版本缓存视为未命中", cache.TryLoad("998") is null);
        }

        // 非法 key 不生成路径，也不读写。
        ChecksContext.Check("成就缓存: 非法 key 不产生路径", cache.PathFor("../evil") is null);
        ChecksContext.Check("成就缓存: 非法 key 读取返回 null", cache.TryLoad("../evil") is null);
        ChecksContext.Check("成就缓存: 非法 key 写入返回 false", !cache.TrySave("../evil", snapshot));

        // 写入失败不能抛（目录路径被占用成文件）。
        var blocked = Path.Combine(cacheDir, "blocked");
        fs.WriteAllTextAtomic(blocked, "x");
        var broken = new AchievementSnapshotCache(fs, blocked);
        ChecksContext.Check("成就缓存: 目录不可用时写入返回 false 且不抛", !broken.TrySave("730", snapshot));
    }

    private static AchievementSnapshot Snapshot(DateTimeOffset fetched, string id, bool unlocked) => new()
    {
        Availability = AchievementAvailability.Available,
        FetchedAtUtc = fetched,
        Entries =
        [
            new AchievementEntry(
                id,
                id,
                "说明",
                IsHidden: false,
                IsUnlocked: unlocked,
                UnlockTimeUtc: unlocked ? fetched : null,
                GlobalPercent: 12.5,
                IconUrl: null),
        ],
    };

    // ------------------------------------------------------------ 页面源码回归

    private static void CheckPageSource()
    {
        var root = FindWorkspaceRoot();
        var appRoot = Path.Combine(root, "src", "PdxLauncher.App");
        var xamlPath = Path.Combine(appRoot, "Views", "AchievementsPage.xaml");
        var viewModelPath = Path.Combine(appRoot, "ViewModels", "AchievementsViewModel.cs");
        var pageCodePath = Path.Combine(appRoot, "Views", "AchievementsPage.xaml.cs");

        if (!ChecksContext.Check("成就图标: AchievementsPage.xaml 存在", File.Exists(xamlPath), xamlPath))
        {
            return;
        }

        var xaml = File.ReadAllText(xamlPath);
        ChecksContext.Check(
            "成就页: 启用 NavigationCacheMode=Required（来回切页不重起子进程）",
            xaml.Contains("NavigationCacheMode=\"Required\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "成就页: ListView 右侧给悬浮滚动条留白（Padding 右 >= 16）",
            System.Text.RegularExpressions.Regex.IsMatch(xaml, "<ListView[\\s\\S]{0,400}?Padding=\"0,0,(1[6-9]|[2-9][0-9]),0\""));
        ChecksContext.Check(
            "成就页: 行模板右侧留白（Grid Padding 右 >= 16）",
            System.Text.RegularExpressions.Regex.IsMatch(xaml, "<Grid Padding=\"0,8,(1[6-9]|[2-9][0-9]),8\""));
        ChecksContext.Check(
            "成就页: 页头显示上次更新时间",
            xaml.Contains("ViewModel.LastUpdatedText", StringComparison.Ordinal));

        if (ChecksContext.Check("成就图标: AchievementsViewModel.cs 存在", File.Exists(viewModelPath), viewModelPath))
        {
            var viewModel = File.ReadAllText(viewModelPath);
            ChecksContext.Check(
                "成就图标: ViewModel 走 AchievementIconResolver.ResolveSource",
                viewModel.Contains("AchievementIconResolver.ResolveSource(", StringComparison.Ordinal));
            ChecksContext.Check(
                "成就图标: ViewModel 对 DDS 走 DdsPngCache 解码",
                viewModel.Contains("DdsPngCache", StringComparison.Ordinal)
                    && viewModel.Contains("EndsWith(\".dds\"", StringComparison.Ordinal));
        }

        if (ChecksContext.Check("成就图标: AchievementsPage.xaml.cs 存在", File.Exists(pageCodePath), pageCodePath))
        {
            var pageCode = File.ReadAllText(pageCodePath);
            ChecksContext.Check(
                "成就图标: 页面把 DdsPngCache 指向 data\\achievement-icons",
                pageCode.Contains("\"achievement-icons\"", StringComparison.Ordinal)
                    && pageCode.Contains("DdsPngCache", StringComparison.Ordinal));
        }
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
