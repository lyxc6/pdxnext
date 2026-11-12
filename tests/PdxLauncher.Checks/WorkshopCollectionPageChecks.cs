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
        "CanActivateSteamSubscription",
        "CanActivateCollectionSubscription",
        "TranslatedName",
        "txtWorkshopCollectionSubscribedInline",
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
        var codeBehindPath = Path.Combine(root, "src", "PdxLauncher.App", "Views", "WorkshopDetailPage.xaml.cs");
        var vmPath = Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "WorkshopDetailViewModel.cs");
        var detailSourcePath = Path.Combine(root, "src", "PdxLauncher.App", "Services", "WorkshopDetailSource.cs");
        var itemPageCachePath = Path.Combine(root, "src", "PdxLauncher.Core", "Online", "SteamItemPageCache.cs");
        var pageStorePath = Path.Combine(root, "src", "PdxLauncher.Core", "Online", "SqliteWorkshopPageStore.cs");

        if (!ChecksContext.Check("合集详情页: WorkshopDetailPage.xaml 存在", File.Exists(xamlPath), xamlPath)
            || !ChecksContext.Check("合集详情页: WorkshopDetailPage.xaml.cs 存在", File.Exists(codeBehindPath), codeBehindPath)
            || !ChecksContext.Check("合集详情页: WorkshopDetailViewModel.cs 存在", File.Exists(vmPath), vmPath)
            || !ChecksContext.Check("合集详情页: WorkshopDetailSource.cs 存在", File.Exists(detailSourcePath), detailSourcePath)
            || !ChecksContext.Check("合集详情页: SteamItemPageCache.cs 存在", File.Exists(itemPageCachePath), itemPageCachePath)
            || !ChecksContext.Check("合集详情页: SqliteWorkshopPageStore.cs 存在", File.Exists(pageStorePath), pageStorePath))
        {
            return ChecksContext.Failures - before;
        }

        var xaml = File.ReadAllText(xamlPath);
        var codeBehind = File.ReadAllText(codeBehindPath);
        var vm = File.ReadAllText(vmPath);

        CheckXamlTokens(xaml);
        CheckModSectionsGated(vm);
        CheckCommentsKept(vm);
        CheckCollectionActions(vm);
        CheckSubscribeFallbacks(xaml, codeBehind, vm);
        CheckCollectionButtonPlacement(xaml);
        CheckCollectionTranslation(xaml, vm);
        CheckCollectionCacheGuards(File.ReadAllText(detailSourcePath), File.ReadAllText(itemPageCachePath), File.ReadAllText(pageStorePath));
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

    /// <summary>只读模式下按钮可以点，页面负责回退到 Steam 客户端；主 mod 按钮同理。</summary>
    private static void CheckSubscribeFallbacks(string xaml, string codeBehind, string vm)
    {
        ChecksContext.Check(
            "合集详情页: 合集子项按钮按 CanActivate 启用（不要求写开关）",
            xaml.Contains("CanActivateSteamSubscription", StringComparison.Ordinal)
            && !xaml.Contains("IsEnabled=\"{x:Bind CanToggleSteamSubscription", StringComparison.Ordinal));
        ChecksContext.Check(
            "合集详情页: 合集级按钮按 CanActivate 启用",
            xaml.Contains("CanActivateCollectionSubscription", StringComparison.Ordinal)
            && vm.Contains("public bool CanActivateCollectionSubscription => IsCollection && !IsSteamSubscribing", StringComparison.Ordinal));
        ChecksContext.Check(
            "合集详情页: 主 mod 按钮按 CanActivate 启用",
            xaml.Contains("ViewModel.CanActivateSteamSubscription", StringComparison.Ordinal)
            && vm.Contains("public bool CanActivateSteamSubscription => ShowSteamSubscribeButton && !IsSteamSubscribing", StringComparison.Ordinal));

        ChecksContext.Check(
            "合集详情页: 子项只读时回退到 Steam 页面",
            codeBehind.Contains("!_viewModel.CanWriteSteam || !_viewModel.IsSteamDirectAvailable", StringComparison.Ordinal)
            && codeBehind.Contains("TryOpenSteam(row.SteamUrl", StringComparison.Ordinal));
        ChecksContext.Check(
            "合集详情页: 合集级只读时回退到 Steam 页面",
            codeBehind.Contains("!_viewModel.IsSteamDirectAvailable || !_viewModel.CanWriteSteam", StringComparison.Ordinal));
        ChecksContext.Check(
            "合集详情页: 主 mod 按钮只读时也走兜底",
            codeBehind.Contains("_viewModel.CanWriteSteam", StringComparison.Ordinal)
            && codeBehind.Contains("TryOpenSteam(_viewModel.SteamUrl", StringComparison.Ordinal));
    }

    /// <summary>「订阅全部」必须在「合集内容」标题与子项列表之间，不在概览卡里。</summary>
    private static void CheckCollectionButtonPlacement(string xaml)
    {
        var title = xaml.IndexOf("WorkshopDetailCollectionTitle", StringComparison.Ordinal);
        var button = xaml.IndexOf("btnWorkshopCollectionSubscribe", StringComparison.Ordinal);
        var list = xaml.IndexOf("lstWorkshopCollectionItems", StringComparison.Ordinal);
        ChecksContext.Check(
            "合集详情页: 订阅全部在合集内容标题与列表之间",
            title >= 0 && button > title && list > button,
            $"title={title} button={button} list={list}");

        var heroStart = xaml.IndexOf("pnlWorkshopCollectionHero", StringComparison.Ordinal);
        var statusStart = heroStart >= 0
            ? xaml.IndexOf("<!-- 3. 状态", heroStart, StringComparison.Ordinal)
            : -1;
        if (ChecksContext.Check("合集详情页: 找到合集概览卡", heroStart >= 0 && statusStart > heroStart))
        {
            var hero = xaml[heroStart..statusStart];
            ChecksContext.Check(
                "合集详情页: 概览卡里不再放订阅全部",
                !hero.Contains("btnWorkshopCollectionSubscribe", StringComparison.Ordinal));
        }
    }

    /// <summary>合集子项标题翻译：VM 有启动方法，模板有译文块。</summary>
    private static void CheckCollectionTranslation(string xaml, string vm)
    {
        foreach (var token in new[]
                 {
                     "StartCollectionNameTranslation",
                     "TranslateCollectionNamesAfterDebounceAsync",
                     "collection-names",
                 })
        {
            ChecksContext.Check(
                "合集详情页: 翻译链路含 " + token,
                vm.Contains(token, StringComparison.Ordinal),
                token);
        }

        ChecksContext.Check(
            "合集详情页: 合集模板绑定标题译文",
            xaml.Contains("TranslatedName", StringComparison.Ordinal)
            && xaml.Contains("txtWorkshopCollectionSubscribedInline", StringComparison.Ordinal));
    }

    /// <summary>缓存正确性：失败兜底不缺子项、合集详情不写普通缓存、超限清旧行、空列表归一。</summary>
    private static void CheckCollectionCacheGuards(string detailSource, string itemPageCache, string pageStore)
    {
        ChecksContext.Check(
            "合集详情页: 物品页失败兜底要求子项",
            detailSource.Contains("(!requireCollectionItems || cacheHasCollection)", StringComparison.Ordinal));
        ChecksContext.Check(
            "合集详情页: 合集详情不写普通详情缓存",
            detailSource.Contains("if (!detail.IsCollection)", StringComparison.Ordinal));
        ChecksContext.Check(
            "合集详情页: 超限页删除旧缓存",
            itemPageCache.Contains("_store.Remove(publishedFileId)", StringComparison.Ordinal));
        ChecksContext.Check(
            "合集详情页: 页面缓存归一 CollectionItems",
            pageStore.Contains("entry.CollectionItems ??= [];", StringComparison.Ordinal));
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
