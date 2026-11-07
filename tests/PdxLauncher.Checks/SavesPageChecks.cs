namespace PdxLauncher.Checks;

using System.Text.Json;
using System.Text.RegularExpressions;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Saves;

/// <summary>
/// 存档页（T3a，只读浏览）自检。这一片最容易悄悄退化的地方：
/// <list type="bullet">
///   <item>导航三处（Tag 常量 / TypeNames / 菜单项）只改了两处，菜单点了没反应；</item>
///   <item>把「实验性」「元数据不可读」写成硬编码中文，切英文后页面露馅；</item>
///   <item>游戏内日期缺失时不再回退到文件修改时间，行里只剩空白；</item>
///   <item>目录探测或扫描被挪回 UI 线程，冷启动/点刷新时卡界面；</item>
///   <item>切游戏时不取消旧扫描，旧结果覆盖新游戏的列表。</item>
/// </list>
/// 所以这里读源码文本 + i18n JSON 钉住这些行为，全部断言都能随对应变异变红。
/// </summary>
public static class SavesPageChecks
{
    private const string PageClass = "PdxLauncher.App.Views.SavesPage";
    private const string SavesTag = "saves";
    private const string PageTypeName = "PdxLauncher.App.Views.SavesPage";

    /// <summary>三种「没东西可显示」的原因必须各自有标题与说明，不能合并成一句「暂无数据」。</summary>
    private static readonly string[] EmptyStateKeys =
    [
        "saves.empty.noDirectory.title",
        "saves.empty.noDirectory.message",
        "saves.empty.empty.title",
        "saves.empty.empty.message",
        "saves.empty.failed.title",
        "saves.empty.failed.message",
        "saves.empty.noGame.title",
        "saves.empty.noGame.message",
    ];

    /// <summary>页面/ViewModel 必须走生成键，而不是写死中文。</summary>
    private static readonly string[] RequiredKeys =
    [
        "saves.title",
        "saves.refresh",
        "saves.listName",
        "saves.noGame",
        "saves.status.scanning",
        "saves.status.reading",
        "saves.status.lastScan",
        "saves.status.failed",
        "saves.status.noGame",
        "saves.dateFallback",
        "saves.metadataUnreadable",
        "saves.experimental",
        "saves.experimentalTooltip",
        "saves.version",
        "saves.saveName",
        "saves.country",
        "saves.city",
        "saves.rowNamePair",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var appRoot = Path.Combine(root, "src", "PdxLauncher.App");
        var xamlPath = Path.Combine(appRoot, "Views", "SavesPage.xaml");
        var pagePath = Path.Combine(appRoot, "Views", "SavesPage.xaml.cs");
        var viewModelPath = Path.Combine(appRoot, "ViewModels", "SavesViewModel.cs");
        var rowViewModelPath = Path.Combine(appRoot, "ViewModels", "SaveGameRowViewModel.cs");
        var automationWatcherPath = Path.Combine(appRoot, "Services", "SaveRowAutomationWatcher.cs");
        var navigationPath = Path.Combine(appRoot, "Services", "NavigationService.cs");
        var mainWindowPath = Path.Combine(appRoot, "MainWindow.xaml");
        var mainWindowCodePath = Path.Combine(appRoot, "MainWindow.xaml.cs");

        CheckPage(xamlPath);
        CheckViewModel(viewModelPath, rowViewModelPath, pagePath, automationWatcherPath);
        CheckGenerationGuard();
        CheckRowText();
        CheckNavigation(navigationPath, mainWindowPath, mainWindowCodePath);
        CheckI18n(root);

        return ChecksContext.Failures - before;
    }

    // ------------------------------------------------------------ 页面

    private static void CheckPage(string xamlPath)
    {
        if (!ChecksContext.Check("存档页: SavesPage.xaml 存在", File.Exists(xamlPath), xamlPath))
        {
            return;
        }

        var xaml = File.ReadAllText(xamlPath);

        ChecksContext.Check(
            "存档页: x:Class 指向 SavesPage",
            xaml.Contains("x:Class=\"" + PageClass + "\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 列表绑定 ViewModel.Saves",
            xaml.Contains("ItemsSource=\"{x:Bind ViewModel.Saves", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 刷新按钮接到 Click 处理器",
            xaml.Contains("Click=\"OnRefreshClick\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 顶部有扫描状态文本",
            xaml.Contains("ViewModel.StatusText", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 扫描中进度环绑定 IsBusy",
            xaml.Contains("ViewModel.IsBusy", StringComparison.Ordinal)
                && xaml.Contains("XamlVisibility.FromBool(ViewModel.IsBusy)", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 空状态同时绑定标题与说明",
            xaml.Contains("ViewModel.EmptyStateTitle", StringComparison.Ordinal)
                && xaml.Contains("ViewModel.EmptyStateMessage", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 空状态面板整体受 ShowEmptyState 控制",
            xaml.Contains("XamlVisibility.FromBool(ViewModel.ShowEmptyState)", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 列表行显示日期与回退说明",
            xaml.Contains("DateText", StringComparison.Ordinal)
                && xaml.Contains("DateFallbackText", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 列表行显示版本/存档名/国家详情",
            xaml.Contains("VersionText", StringComparison.Ordinal)
                && xaml.Contains("DetailsText", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 降级档有「元数据不可读」标签",
            xaml.Contains("IsDegraded", StringComparison.Ordinal)
                && xaml.Contains("DegradedText", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 实验性格式有可见标签",
            xaml.Contains("IsExperimental", StringComparison.Ordinal)
                && xaml.Contains("ExperimentalText", StringComparison.Ordinal)
                && xaml.Contains("ExperimentalTooltip", StringComparison.Ordinal));

        // UIA 无障碍名：ListView 的 Content 是视图模型对象，名字与自动 id 必须挂在行容器上，
        // 否则读屏把整列都读成类型全名。WinUI 的 Style Setter.Value 不支持 {Binding}（实测
        // ListItem.AutomationId 仍为空），所以改由 ContainerContentChanging 写；这里断言入口没丢。
        ChecksContext.Check(
            "存档页: 列表用 ContainerContentChanging 写行容器的名字与 id",
            xaml.Contains("ContainerContentChanging=\"OnSaveContainerContentChanging\"", StringComparison.Ordinal)
                && !xaml.Contains("Value=\"{Binding AutomationName}\"", StringComparison.Ordinal)
                && !xaml.Contains("Value=\"{Binding AutomationId}\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 模板根元素也带自动化名字（{x:Bind AutomationName}）",
            Regex.IsMatch(
                xaml,
                @"<DataTemplate[\s\S]{0,400}?AutomationProperties\.Name=""\{x:Bind AutomationName",
                RegexOptions.CultureInvariant));
        ChecksContext.Check(
            "存档页: 页面没有硬编码中文「实验性」（必须走 i18n 键）",
            !xaml.Contains("实验性", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ ViewModel

    private static void CheckViewModel(
        string viewModelPath,
        string rowViewModelPath,
        string pagePath,
        string automationWatcherPath)
    {
        if (!ChecksContext.Check("存档页: SavesViewModel.cs 存在", File.Exists(viewModelPath), viewModelPath)
            || !ChecksContext.Check("存档页: SaveGameRowViewModel.cs 存在", File.Exists(rowViewModelPath), rowViewModelPath))
        {
            return;
        }

        var viewModel = File.ReadAllText(viewModelPath);
        var rowViewModel = File.ReadAllText(rowViewModelPath);

        ChecksContext.Check(
            "存档页: ViewModel 不引用 Microsoft.UI.Xaml",
            !viewModel.Contains("using Microsoft.UI.Xaml", StringComparison.Ordinal)
                && !viewModel.Contains("Microsoft.UI.Xaml.", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 行 ViewModel 不引用 Microsoft.UI.Xaml",
            !rowViewModel.Contains("using Microsoft.UI.Xaml", StringComparison.Ordinal)
                && !rowViewModel.Contains("Microsoft.UI.Xaml.", StringComparison.Ordinal));

        // 两段式：第一段只枚举（ScanFilesAsync 自己在线程池上跑，不打开文件内容）；
        // 第二段最多 4 条并行解析，再按「满一批 / 50 ms / 后台完成」回填连续前缀。
        ChecksContext.Check(
            "存档页: 第一段只调用枚举 ScanFilesAsync（不读文件内容）",
            viewModel.Contains("ScanFilesAsync", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 第二段细节解析跑在 Task.Run 里（UI 线程零文件 IO）",
            Regex.IsMatch(viewModel, @"Task\.Run[\s\S]{0,700}?Parallel\.ForEachAsync", RegexOptions.CultureInvariant));
        ChecksContext.Check(
            "存档页: 第二段按时间/条数触发回填而不是整表重建",
            viewModel.Contains("SaveScanBatching.ShouldFlush", StringComparison.Ordinal)
                && viewModel.Contains("ApplyDetailPrefix", StringComparison.Ordinal)
                && viewModel.Contains("ApplyDetail", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 进度只统计成功回填的行（失败行不推进 n）",
            Regex.IsMatch(viewModel, @"if \(Saves\[index\]\.ApplyDetail\(meta\)\)[\s\S]{0,200}?applied\+\+", RegexOptions.CultureInvariant)
                && viewModel.Contains("_processed += applied", StringComparison.Ordinal)
                && !viewModel.Contains("_processed += metas.Length", StringComparison.Ordinal));

        ChecksContext.Check(
            "存档页: 切游戏/刷新时先清空旧列表",
            viewModel.Contains("Saves.Clear()", StringComparison.Ordinal));

        // 代次守卫：旧扫描的 finally 不能关掉新扫描的忙碌态，旧结果也不能写进列表。
        // 这里只钉「调用点交还给 tracker」；判定极性本身由 CheckGenerationGuard 的行为用例钉住，
        // 因为纯文本断言挡不住 `if (!IsCurrent(...))` 被改成 `if (IsCurrent(...))`。
        ChecksContext.Check(
            "存档页: 结果提交统一走 tracker 的代次判定",
            viewModel.Contains("SaveScanRequestTracker", StringComparison.Ordinal)
                && viewModel.Contains("TryCommit(generation", StringComparison.Ordinal)
                && !viewModel.Contains("IsCurrent(generation)", StringComparison.Ordinal));

        ChecksContext.Check(
            "存档页: 扫描异常记 Warn 并落到失败空状态",
            Regex.IsMatch(viewModel, @"catch \(Exception ex\)[\s\S]{0,500}?\.Warn\(", RegexOptions.CultureInvariant)
                && viewModel.Contains("SavesState.Failed", StringComparison.Ordinal));

        ChecksContext.Check(
            "存档页: 三种空状态分别映射（无目录 / 目录空 / 失败）",
            viewModel.Contains("SavesState.NoDirectory", StringComparison.Ordinal)
                && viewModel.Contains("SavesState.Empty", StringComparison.Ordinal)
                && viewModel.Contains("SavesState.Failed", StringComparison.Ordinal));

        ChecksContext.Check(
            "存档页: 有游戏内日期时优先用它",
            rowViewModel.Contains("HasGameDate", StringComparison.Ordinal)
                && rowViewModel.Contains("Meta!.GameDate", StringComparison.Ordinal));

        // 删掉日期回退逻辑（不显示文件修改时间）这条会变红。
        ChecksContext.Check(
            "存档页: 没有游戏内日期时回退到文件修改时间",
            Regex.IsMatch(rowViewModel, @"DateText[\s\S]{0,500}?LastWriteUtc", RegexOptions.CultureInvariant)
                && rowViewModel.Contains("HasDateFallback", StringComparison.Ordinal));

        ChecksContext.Check(
            "存档页: V3/EU5/CS2 标为实验性（CK3/Stellaris 不标）",
            rowViewModel.Contains("\"victoria3\"", StringComparison.Ordinal)
                && rowViewModel.Contains("\"eu5\"", StringComparison.Ordinal)
                && rowViewModel.Contains("\"cities_skylines_2\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 实验性文案走 i18n 键，不写死中文",
            rowViewModel.Contains("Keys.SavesExperimental", StringComparison.Ordinal)
                && !rowViewModel.Contains("实验性", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 降级文案走 i18n 键",
            rowViewModel.Contains("Keys.SavesMetadataUnreadable", StringComparison.Ordinal));

        // 读屏读的是列表项的 Content（视图模型对象）：没有这个组合名，整列都是类型全名。
        ChecksContext.Check(
            "存档页: 行自动化名字由文件名 + 日期 + 详情拼成",
            Regex.IsMatch(
                rowViewModel,
                @"AutomationName[\s\S]{0,800}?Keys\.SavesRowNamePair[\s\S]{0,400}?HasDetails",
                RegexOptions.CultureInvariant)
                && !rowViewModel.Contains("实验性", StringComparison.Ordinal));

        if (!ChecksContext.Check("存档页: SavesPage.xaml.cs 存在", File.Exists(pagePath), pagePath))
        {
            return;
        }

        var page = File.ReadAllText(pagePath);
        ChecksContext.Check(
            "存档页: 页面订阅当前游戏变化",
            page.Contains("AppServices.CurrentGameChanged +=", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 页面卸载时退订当前游戏变化",
            page.Contains("AppServices.CurrentGameChanged -=", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 刷新/切游戏走 ReloadCoordinator 取消上一次",
            page.Contains("ReloadCoordinator", StringComparison.Ordinal)
                && page.Contains("_reload.RunAsync", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 后台线程的游戏切换事件派发回 UI 线程",
            page.Contains("DispatcherQueue.HasThreadAccess", StringComparison.Ordinal)
                && page.Contains("DispatcherQueue.TryEnqueue", StringComparison.Ordinal));

        // 行容器的 UIA 名字/id：只能写在这里（Style Setter 的 {Binding} 在 WinUI 里不生效）。
        ChecksContext.Check(
            "存档页: 行容器写入 save_N 的 AutomationId",
            page.Contains("AutomationProperties.SetAutomationId(container, row.AutomationId)", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 行容器写入组合后的 AutomationName",
            page.Contains("_rowAutomation.Track", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 容器复用/回收时退订读屏名变化（防泄漏）",
            page.Contains("args.InRecycleQueue", StringComparison.Ordinal)
                && page.Contains("_rowAutomation.Untrack", StringComparison.Ordinal));
        if (ChecksContext.Check(
                "存档页: 读屏名监听器存在",
                File.Exists(automationWatcherPath),
                automationWatcherPath))
        {
            var watcher = File.ReadAllText(automationWatcherPath);
            ChecksContext.Check(
                "存档页: 监听器订阅 PropertyChanged 并在 AutomationName 变化时重写",
                watcher.Contains("PropertyChanged += handler", StringComparison.Ordinal)
                    && watcher.Contains("nameof(SaveGameRowViewModel.AutomationName)", StringComparison.Ordinal)
                    && watcher.Contains("applyName(row.AutomationName)", StringComparison.Ordinal));
        }
    }

    // ------------------------------------------------------------ 代次守卫（行为级）

    /// <summary>
    /// 直接跑 <see cref="SaveScanRequestTracker"/>：旧代次提交必须被丢弃且<b>不执行</b>提交动作，
    /// 新代次必须真正落盘。文本断言只认字符串，把判定极性翻转（<c>if (!IsCurrent(...))</c> 去掉 <c>!</c>）
    /// 仍然全绿；这条用真实调用把两种方向都钉住。
    /// </summary>
    private static void CheckGenerationGuard()
    {
        var tracker = new SaveScanRequestTracker();
        var first = tracker.Begin();
        var second = tracker.Begin();

        var oldApplied = 0;
        var oldCommitted = tracker.TryCommit(first, () => oldApplied++);
        var newApplied = 0;
        var newCommitted = tracker.TryCommit(second, () => newApplied++);
        // 迟到的旧代次（旧扫描在 await 点之后才走到提交）同样不能写回列表。
        var lateCommitted = tracker.TryCommit(first, () => oldApplied++);

        ChecksContext.Check(
            "存档页: 旧代次的结果被整批丢弃（提交动作不执行）",
            !oldCommitted && oldApplied == 0 && !lateCommitted,
            "oldCommitted=" + oldCommitted + ", lateCommitted=" + lateCommitted + ", oldApplied=" + oldApplied);
        ChecksContext.Check(
            "存档页: 新代次的结果被应用（提交动作只执行一次）",
            newCommitted && newApplied == 1,
            "newCommitted=" + newCommitted + ", newApplied=" + newApplied);
    }

    /// <summary>
    /// 详情段的取字段规则：CS2 把 <c>cityName</c> 同时写进存档名与国家，两段同字就是重复；
    /// 而 CK3/Stellaris 的国家是独立信息，必须保留。改错只有肉眼能看出，所以直接断言纯函数。
    /// </summary>
    private static void CheckRowText()
    {
        ChecksContext.Check(
            "存档页: 国家/城市与存档名相同或为空时不重复显示",
            !SaveRowText.ShouldShowPlace("阿伦德尔", "阿伦德尔")
                && !SaveRowText.ShouldShowPlace("阿伦德尔", " 阿伦德尔 ")
                && !SaveRowText.ShouldShowPlace(null, "七丘城")
                && !SaveRowText.ShouldShowPlace("   ", "七丘城"));
        ChecksContext.Check(
            "存档页: 国家与存档名不同的独立信息仍然显示",
            SaveRowText.ShouldShowPlace("普鲁士", "七丘城"));
        ChecksContext.Check(
            "存档页: 只有 Colossal 容器的 country 字段按城市名显示",
            SaveRowText.IsCityPlace(SaveContainer.Colossal)
                && !SaveRowText.IsCityPlace(SaveContainer.TextMeta)
                && !SaveRowText.IsCityPlace(SaveContainer.Unknown));
        ChecksContext.Check(
            "存档页: 行详情用 i18n 的「城市」片段而不是「国家」",
            File.ReadAllText(Path.Combine(FindWorkspaceRoot(), "src", "PdxLauncher.App", "ViewModels", "SaveGameRowViewModel.cs"))
                .Contains("Keys.SavesCity", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ 导航四处

    private static void CheckNavigation(string navigationPath, string mainWindowPath, string mainWindowCodePath)
    {
        if (!ChecksContext.Check(
                "存档页: NavigationService.cs 存在",
                File.Exists(navigationPath),
                navigationPath))
        {
            return;
        }

        var navigation = File.ReadAllText(navigationPath);
        ChecksContext.Check(
            "存档页: NavigationService 定义 SavesTag 常量",
            navigation.Contains(
                "public const string SavesTag = \"" + SavesTag + "\";",
                StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: TypeNames 映射 SavesPage",
            navigation.Contains(
                "[SavesTag] = \"" + PageTypeName + "\"",
                StringComparison.Ordinal));

        if (!ChecksContext.Check(
                "存档页: MainWindow.xaml 存在",
                File.Exists(mainWindowPath),
                mainWindowPath))
        {
            return;
        }

        var mainWindow = File.ReadAllText(mainWindowPath);
        ChecksContext.Check(
            "存档页: MainWindow.xaml 有 Tag=\"saves\" 的菜单项",
            mainWindow.Contains("Tag=\"" + SavesTag + "\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 菜单项命名 NavSaves 并带导航自动化 id",
            mainWindow.Contains("x:Name=\"NavSaves\"", StringComparison.Ordinal)
                && mainWindow.Contains("AutomationProperties.AutomationId=\"navSaves\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "存档页: 菜单项文案走 shell.nav.saves",
            mainWindow.Contains("L.ShellNavSaves", StringComparison.Ordinal));

        // 插在 DLC 与成就之间：这三个菜单项相对顺序错了，左栏会读成两套分组。
        ChecksContext.Check(
            "存档页: 菜单项位于 DLC 与成就之间",
            Regex.IsMatch(
                mainWindow,
                @"x:Name=""NavDlc""[\s\S]{0,800}?Tag=""saves""[\s\S]{0,800}?x:Name=""NavAchievements""",
                RegexOptions.CultureInvariant));

        if (!ChecksContext.Check(
                "存档页: MainWindow.xaml.cs 存在",
                File.Exists(mainWindowCodePath),
                mainWindowCodePath))
        {
            return;
        }

        // 反向高亮是通用逻辑：Navigate 事件用 TagForPageType 反查 Tag，再同步左栏选中项。
        // 这条钉住的是「saves 加入 TypeNames 后无需额外代码就能高亮」的前提，删掉任一处都会红。
        var mainWindowCode = File.ReadAllText(mainWindowCodePath);
        ChecksContext.Check(
            "存档页: 反向高亮走 TagForPageType + SyncSelectedNavItem",
            mainWindowCode.Contains("TagForPageType(e.SourcePageType)", StringComparison.Ordinal)
                && mainWindowCode.Contains("SyncSelectedNavItem", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ i18n

    private static void CheckI18n(string root)
    {
        foreach (var language in new[] { "zh-CN", "en-US" })
        {
            var path = I18nResw.ResourcesPath(root, language);
            if (!ChecksContext.Check(
                    "存档页: " + language + " Resources.resw 存在",
                    File.Exists(path),
                    path))
            {
                continue;
            }

            var table = I18nResw.Read(root, language);
            foreach (var key in RequiredKeys.Concat(EmptyStateKeys))
            {
                ChecksContext.Check(
                    "存档页: " + language + " 有 " + key,
                    !string.IsNullOrWhiteSpace(I18nResw.Get(table, key)));
            }

            ChecksContext.Check(
                "存档页: " + language + " 有 shell.nav.saves",
                !string.IsNullOrWhiteSpace(I18nResw.Get(table, "shell.nav.saves")));
        }

        CheckEmptyStateMessagesAreDistinct(root);
    }

    /// <summary>
    /// 「没有目录」「目录是空的」「扫描失败」三句说明不能同文：合并成一句「暂无存档」，
    /// 用户就分不清是该进游戏存一次、还是该看日志排查。
    /// </summary>
    private static void CheckEmptyStateMessagesAreDistinct(string root)
    {
        if (!File.Exists(I18nResw.ResourcesPath(root, "zh-CN")))
        {
            return;
        }

        var table = I18nResw.Read(root, "zh-CN");
        var values = new List<string>();
        foreach (var key in new[]
                 {
                     "saves.empty.noDirectory.message",
                     "saves.empty.empty.message",
                     "saves.empty.failed.message",
                 })
        {
            if (I18nResw.Get(table, key) is { } value)
            {
                values.Add(value);
            }
        }

        ChecksContext.Check(
            "存档页: 三种空状态说明互不相同",
            values.Count == 3 && values.Distinct(StringComparer.Ordinal).Count() == values.Count,
            string.Join(" | ", values));
    }

    private static bool HasNonEmptyString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString());

    // ------------------------------------------------------------ 路径

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
