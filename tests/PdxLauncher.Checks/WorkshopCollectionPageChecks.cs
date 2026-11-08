namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 合集详情页布局的源码断言。
/// <para>
/// 合集不是 mod：页面只保留页头、合集概览（封面/统计/订阅全部）、描述、项目列表与评论，
/// 不能再回退成「截图画廊 + mod 信息卡 + mod 订阅卡」的普通详情页版式。
/// 这些结论只在真机上看得出来，编译器不会管，所以读源文本钉住；先例见
/// <see cref="WorkshopDetailSubscribeCardChecks"/>。
/// </para>
/// </summary>
public static class WorkshopCollectionPageChecks
{
    /// <summary>合集页必须出现在 XAML 里的关键控件 / 绑定。</summary>
    private static readonly string[] RequiredXamlTokens =
    [
        "pnlWorkshopCollectionHero",
        "ShowCollectionHero",
        "btnWorkshopCollectionSubscribe",
        "CollectionSubscribeActionText",
        "txtWorkshopCollectionSubscribed",
        "lstWorkshopCollectionItems",
        "ShortDescription",
        "SteamSubscribeActionText",
        "ShowSteamStateBadge",
        "OnCollectionItemSubscribeClick",
        "OnCollectionItemOpenBrowserClick",
        "OnCollectionItemOpenSteamClick",
    ];

    /// <summary>两种语言都必须补齐的合集订阅文案键。</summary>
    private static readonly string[] RequiredReswKeys =
    [
        "WorkshopDetailCollectionSubscribeAll",
        "WorkshopDetailCollectionUnsubscribeAll",
        "WorkshopDetailCollectionSubscribingAll",
        "WorkshopDetailCollectionSubscribed",
        "WorkshopDetailCollectionSubscribeConfirm",
        "WorkshopDetailCollectionUnsubscribeConfirm",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var xamlPath = Path.Combine(root, "src", "PdxLauncher.App", "Views", "WorkshopDetailPage.xaml");
        var vmPath = Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "WorkshopDetailViewModel.cs");

        if (!ChecksContext.Check("合集详情页: WorkshopDetailPage.xaml 存在", File.Exists(xamlPath), xamlPath)
            || !ChecksContext.Check("合集详情页: WorkshopDetailViewModel.cs 存在", File.Exists(vmPath), vmPath))
        {
            return ChecksContext.Failures - before;
        }

        var xaml = File.ReadAllText(xamlPath);
        var vm = File.ReadAllText(vmPath);

        CheckXamlTokens(xaml);
        CheckModSectionsGated(vm);
        CheckCommentsKept(vm);
        CheckCollectionActions(vm);
        CheckReswKeys(root);

        return ChecksContext.Failures - before;
    }

    private static void CheckXamlTokens(string xaml)
    {
        foreach (var token in RequiredXamlTokens)
        {
            ChecksContext.Check(
                "合集详情页: XAML 含 " + token,
                xaml.Contains(token, StringComparison.Ordinal),
                token);
        }

        ChecksContext.Check(
            "合集详情页: mod 信息卡由 ShowInfoCard 控制",
            xaml.Contains("ViewModel.ShowInfoCard", StringComparison.Ordinal));
    }

    private static void CheckModSectionsGated(string vm)
    {
        ChecksContext.Check(
            "合集详情页: 截图画廊在合集下隐藏",
            vm.Contains("public bool ShowGalleryCard => !IsCollection && HasScreenshots", StringComparison.Ordinal));
        ChecksContext.Check(
            "合集详情页: mod 订阅卡在合集下隐藏",
            vm.Contains("public bool ShowSubscribeCard => Row is not null && !IsCollection", StringComparison.Ordinal));
        ChecksContext.Check(
            "合集详情页: mod 信息卡在合集下隐藏",
            vm.Contains("public bool ShowInfoCard => Row is not null && !IsCollection", StringComparison.Ordinal));
        ChecksContext.Check(
            "合集详情页: 合集概览卡按 IsCollection 显示",
            vm.Contains("public bool ShowCollectionHero => IsCollection", StringComparison.Ordinal));

        // 列表页已经标过合集的，首帧就按合集布局，避免先闪一下 mod 页面。
        ChecksContext.Check(
            "合集详情页: ResetForLoad 沿用行上的 IsCollection",
            vm.Contains("IsCollection = row.IsCollection", StringComparison.Ordinal));
    }

    private static void CheckCommentsKept(string vm)
    {
        var start = vm.IndexOf("public bool ShowCommentsSection =>", StringComparison.Ordinal);
        if (!ChecksContext.Check("合集详情页: 找到 ShowCommentsSection", start >= 0))
        {
            return;
        }

        var end = vm.IndexOf(';', start);
        var line = end > start ? vm[start..end] : vm[start..];
        ChecksContext.Check(
            "合集详情页: 评论区没有被 IsCollection 隐藏",
            !line.Contains("IsCollection", StringComparison.Ordinal),
            line);
    }

    private static void CheckCollectionActions(string vm)
    {
        foreach (var token in new[]
                 {
                     "SetCollectionItemSubscribedAsync",
                     "ToggleCollectionSubscriptionAsync",
                     "ApplyCollectionStatesAsync",
                     "DownloadCollectionCoversAsync",
                     "BuildOptimisticState",
                 })
        {
            ChecksContext.Check(
                "合集详情页: ViewModel 含 " + token,
                vm.Contains(token, StringComparison.Ordinal),
                token);
        }

        // 合集不铺截图墙，只下载描述内嵌图。
        ChecksContext.Check(
            "合集详情页: 合集分支跳过画廊下载",
            vm.Contains("if (IsCollection)", StringComparison.Ordinal)
            && vm.Contains("DownloadInlineImagesAsync(row, inlineUrls, cancellationToken)", StringComparison.Ordinal));
    }

    private static void CheckReswKeys(string root)
    {
        foreach (var lang in new[] { "zh-CN", "en-US" })
        {
            var path = Path.Combine(root, "src", "PdxLauncher.App", "Strings", lang, "Resources.resw");
            if (!ChecksContext.Check("合集详情页: " + lang + " Resources.resw 存在", File.Exists(path), path))
            {
                continue;
            }

            var resw = File.ReadAllText(path);
            foreach (var key in RequiredReswKeys)
            {
                ChecksContext.Check(
                    "合集详情页: " + lang + " 有 " + key,
                    resw.Contains("name=\"" + key + "\"", StringComparison.Ordinal),
                    key);
            }
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
