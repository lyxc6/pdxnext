namespace PdxLauncher.Checks;

using System.Text.Json;
using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;

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
        var navigationPath = Path.Combine(appRoot, "Services", "NavigationService.cs");
        var mainWindowPath = Path.Combine(appRoot, "MainWindow.xaml");
        var mainWindowCodePath = Path.Combine(appRoot, "MainWindow.xaml.cs");

        CheckPage(xamlPath);
        CheckViewModel(viewModelPath, rowViewModelPath, pagePath);
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
        ChecksContext.Check(
            "存档页: 页面没有硬编码中文「实验性」（必须走 i18n 键）",
            !xaml.Contains("实验性", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ ViewModel

    private static void CheckViewModel(string viewModelPath, string rowViewModelPath, string pagePath)
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

        // 目录探测（GetLocations 内部有 DirectoryExists）与扫描必须一起待在线程池上。
        // 把 GetLocations 挪出 Task.Run 会让页面在 UI 线程上碰磁盘，这条断言随即变红。
        ChecksContext.Check(
            "存档页: 目录探测跑在 Task.Run 里（UI 线程零文件 IO）",
            Regex.IsMatch(viewModel, @"Task\.Run[\s\S]{0,600}?GetLocations", RegexOptions.CultureInvariant));
        ChecksContext.Check(
            "存档页: 扫描调用位于 Task.Run 内",
            Regex.IsMatch(viewModel, @"Task\.Run[\s\S]{0,600}?ScanAsync", RegexOptions.CultureInvariant));

        ChecksContext.Check(
            "存档页: 切游戏/刷新时先清空旧列表",
            viewModel.Contains("Saves.Clear()", StringComparison.Ordinal));

        // 代次守卫：旧扫描的 finally 不能关掉新扫描的忙碌态，旧结果也不能写进列表。
        ChecksContext.Check(
            "存档页: 用代次守卫判断结果是否仍然有效",
            viewModel.Contains("SaveScanRequestTracker", StringComparison.Ordinal)
                && viewModel.Contains("IsCurrent(generation)", StringComparison.Ordinal));

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
                && rowViewModel.Contains("_meta.GameDate", StringComparison.Ordinal));

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
            var savesPath = Path.Combine(root, "tools", "i18n", language, "saves.json");
            var shellPath = Path.Combine(root, "tools", "i18n", language, "shell.json");

            if (!ChecksContext.Check(
                    "存档页: " + language + " saves.json 存在",
                    File.Exists(savesPath),
                    savesPath))
            {
                continue;
            }

            using var saves = JsonDocument.Parse(File.ReadAllText(savesPath));
            foreach (var key in RequiredKeys.Concat(EmptyStateKeys))
            {
                ChecksContext.Check(
                    "存档页: " + language + " 有 " + key,
                    HasNonEmptyString(saves.RootElement, key));
            }

            if (!ChecksContext.Check(
                    "存档页: " + language + " shell.json 存在",
                    File.Exists(shellPath),
                    shellPath))
            {
                continue;
            }

            using var shell = JsonDocument.Parse(File.ReadAllText(shellPath));
            ChecksContext.Check(
                "存档页: " + language + " 有 shell.nav.saves",
                HasNonEmptyString(shell.RootElement, "shell.nav.saves"));
        }

        CheckEmptyStateMessagesAreDistinct(root);
    }

    /// <summary>
    /// 「没有目录」「目录是空的」「扫描失败」三句说明不能同文：合并成一句「暂无存档」，
    /// 用户就分不清是该进游戏存一次、还是该看日志排查。
    /// </summary>
    private static void CheckEmptyStateMessagesAreDistinct(string root)
    {
        var path = Path.Combine(root, "tools", "i18n", "zh-CN", "saves.json");
        if (!File.Exists(path))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var values = new List<string>();
        foreach (var key in new[]
                 {
                     "saves.empty.noDirectory.message",
                     "saves.empty.empty.message",
                     "saves.empty.failed.message",
                 })
        {
            if (document.RootElement.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                values.Add(value.GetString() ?? string.Empty);
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
