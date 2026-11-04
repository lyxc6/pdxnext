namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;

/// <summary>
/// 播放集页「双列」布局自检：把这次改造的结论钉成源码断言。
/// <para>
/// 三种显示方式是同一份 XAML 的互斥分支，重排一次就可能把某一支删掉、把双列专属的控件
/// AutomationId 改掉（UIA 冒烟脚本与 HANDOFF 走查都按 id 定位），或者在合并时把
/// 原版启动器的「添加模组」子页顺手丢掉。这些错误编译器都看不见，所以这里读 XAML / 源码文本断言。
/// </para>
/// 同类先例见 <see cref="AccountPageLayoutChecks"/>（读 XAML 源文本钉版式）与
/// <see cref="ConfirmOverlayChecks"/>（钉控件结构与调用点）。
/// </summary>
public static class ModsPageLayoutChecks
{
    /// <summary>双列布局新增的 AutomationId：冒烟脚本 scripts/verify-playset-twocolumn.ps1 按它们定位。</summary>
    private static readonly string[] TwoColumnAutomationIds =
    [
        "lstModCandidates",
        "lstModsTwoColumn",
        "txtCandidateSearch",
        "txtModSearchTwoCol",
        "txtCandidateNoPlaySet",
        "txtCandidateEmpty",
        "txtCandidateNoMatch",
        "txtTwoColumnEmpty",
        "txtTwoColumnNoMatch",
        "txtTwoColumnNoPlaySet",
    ];

    /// <summary>原版启动器布局的 AutomationId 一个都不能少（改造不允许动它）。</summary>
    private static readonly string[] OfficialAutomationIds =
    [
        "cmbPlaySets",
        "btnNewPlaySet",
        "btnRenamePlaySet",
        "btnDeletePlaySet",
        "btnActivatePlaySet",
        "btnEnableAllMods",
        "btnDisableAllMods",
        "btnRepairModPaths",
        "btnAutoSort",
        "txtModSearch",
        "btnAddMods",
        "lstMods",
        "txtModListEmpty",
        "txtModsStatus",
        "ringModsBusy",
        "txtPlaySetPlaceholder",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var xamlPath = Path.Combine(root, "src", "PdxLauncher.App", "Views", "ModsPage.xaml");
        var codePath = Path.Combine(root, "src", "PdxLauncher.App", "Views", "ModsPage.xaml.cs");

        if (!ChecksContext.Check("播放集页: ModsPage.xaml 存在", File.Exists(xamlPath), xamlPath))
        {
            return ChecksContext.Failures - before;
        }

        var xaml = File.ReadAllText(xamlPath);
        var code = File.ReadAllText(codePath);

        CheckAutomationIds(xaml);
        CheckLayoutBranches(xaml);
        CheckSharedRowTemplate(xaml);
        CheckCandidateIsCheckToAdd(xaml);
        CheckHeaderSearchFillsColumn(xaml);
        CheckCodeBehind(code);
        CheckLegacySubpageKept(root);
        CheckDisplayModeValue(root);

        return ChecksContext.Failures - before;
    }

    private static void CheckAutomationIds(string xaml)
    {
        foreach (var id in TwoColumnAutomationIds)
        {
            ChecksContext.Check(
                "播放集页: 双列保留 AutomationId " + id,
                xaml.Contains("AutomationId=\"" + id + "\"", StringComparison.Ordinal));
        }

        foreach (var id in OfficialAutomationIds)
        {
            ChecksContext.Check(
                "播放集页: 原版布局保留 AutomationId " + id,
                xaml.Contains("AutomationId=\"" + id + "\"", StringComparison.Ordinal));
        }
    }

    /// <summary>三支布局各绑自己的开关，且工具条 / 状态栏由两种内容布局共用。</summary>
    private static void CheckLayoutBranches(string xaml)
    {
        string[] required =
        [
            "ViewModel.IsOfficialLayout",
            "ViewModel.IsTwoColumnLayout",
            "ViewModel.IsPlaceholderLayout",
            "ViewModel.ShowsPlaysetChrome",
        ];

        foreach (var binding in required)
        {
            ChecksContext.Check(
                "播放集页: 绑定了 " + binding,
                xaml.Contains(binding, StringComparison.Ordinal));
        }

        // 双列两栏各有自己的搜索框：左栏过滤候选，右栏沿用播放集内的 SearchText。
        ChecksContext.Check(
            "播放集页: 左栏搜索绑 CandidateSearchText",
            xaml.Contains("{x:Bind ViewModel.CandidateSearchText, Mode=OneWay}", StringComparison.Ordinal));
        ChecksContext.Check(
            "播放集页: 右栏搜索绑 SearchText",
            xaml.Contains("{x:Bind ViewModel.SearchText, Mode=OneWay}", StringComparison.Ordinal));
        ChecksContext.Check(
            "播放集页: 左栏候选绑 Candidates 集合",
            xaml.Contains("{x:Bind ViewModel.Candidates, Mode=OneWay}", StringComparison.Ordinal));
    }

    /// <summary>
    /// 两栏共用同一份行模板：原版列表与双列右栏都引用 PlaysetModRowTemplate。
    /// 复制一份行模板就会让「开关 / 上下移 / 移除」在两个布局里慢慢分叉。
    /// </summary>
    private static void CheckSharedRowTemplate(string xaml)
    {
        ChecksContext.Check(
            "播放集页: 定义了 PlaysetModRowTemplate",
            xaml.Contains("<DataTemplate x:Key=\"PlaysetModRowTemplate\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "播放集页: 定义了 PlaysetCandidateRowTemplate",
            xaml.Contains("<DataTemplate x:Key=\"PlaysetCandidateRowTemplate\"", StringComparison.Ordinal));

        var sharedUsages = CountOccurrences(xaml, "ItemTemplate=\"{StaticResource PlaysetModRowTemplate}\"");
        ChecksContext.Equal("播放集页: 行模板被两处复用", 2, sharedUsages);
        ChecksContext.Equal(
            "播放集页: 候选行模板只用于左栏",
            1,
            CountOccurrences(xaml, "ItemTemplate=\"{StaticResource PlaysetCandidateRowTemplate}\""));
    }

    /// <summary>左栏是「勾选即加入」：勾选框直接带 Click 处理器，没有确认按钮。</summary>
    private static void CheckCandidateIsCheckToAdd(string xaml)
    {
        var start = xaml.IndexOf("x:Key=\"PlaysetCandidateRowTemplate\"", StringComparison.Ordinal);
        ChecksContext.Check("播放集页: 找到候选行模板", start >= 0);
        if (start < 0)
        {
            return;
        }

        var end = xaml.IndexOf("</DataTemplate>", start, StringComparison.Ordinal);
        var template = end > start ? xaml[start..end] : xaml[start..];

        ChecksContext.Check(
            "播放集页: 候选勾选框点了就加入",
            template.Contains("Click=\"OnCandidateChecked\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "播放集页: 候选行带勾选状态双向绑定",
            template.Contains("IsChecked=\"{x:Bind IsSelected, Mode=TwoWay}\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "播放集页: 候选行 id 用 modCandidate 前缀",
            template.Contains("{x:Bind RowAutomationId, Mode=OneTime}", StringComparison.Ordinal)
                && template.Contains("{x:Bind CheckAutomationId, Mode=OneTime}", StringComparison.Ordinal));
    }

    /// <summary>
    /// 表头搜索框必须整行铺满、不写死宽度：360 DIP 的左栏里，标题 + 计数 + 定宽搜索框挤一行时
    /// 计数会被裁成「共 0 个可添加的 mc」，实测踩过一次。
    /// </summary>
    private static void CheckHeaderSearchFillsColumn(string xaml)
    {
        (string Name, string Id)[] boxes =
        [
            ("CandidateSearchBox", "txtCandidateSearch"),
            ("TwoColumnSearchBox", "txtModSearchTwoCol"),
        ];

        foreach (var (name, id) in boxes)
        {
            var start = xaml.IndexOf("x:Name=\"" + name + "\"", StringComparison.Ordinal);
            ChecksContext.Check("播放集页: 表头搜索框存在 " + name, start >= 0);
            if (start < 0)
            {
                continue;
            }

            var close = xaml.IndexOf("/>", start, StringComparison.Ordinal);
            var element = close > start ? xaml[start..close] : xaml[start..];

            ChecksContext.Check(
                "播放集页: " + name + " 不写死宽度",
                !element.Contains("Width=", StringComparison.Ordinal),
                element);
            ChecksContext.Check(
                "播放集页: " + name + " 仍按 " + id + " 暴露",
                element.Contains("AutomationId=\"" + id + "\"", StringComparison.Ordinal),
                element);
        }
    }

    /// <summary>代码后置：两个新处理器在，且位次编辑 / 滚动定位跟着当前布局走。</summary>
    private static void CheckCodeBehind(string code)
    {
        ChecksContext.Check(
            "播放集页: 有 OnCandidateChecked 处理器",
            code.Contains("private void OnCandidateChecked(", StringComparison.Ordinal));
        ChecksContext.Check(
            "播放集页: 有 OnCandidateSearchTextChanged 处理器",
            code.Contains("private void OnCandidateSearchTextChanged(", StringComparison.Ordinal));
        ChecksContext.Check(
            "播放集页: 位次 / 滚动按当前布局取列表",
            code.Contains("private ListView ActiveModList =>", StringComparison.Ordinal)
                && code.Contains("ActiveModList.ContainerFromItem(", StringComparison.Ordinal)
                && code.Contains("ActiveModList.ScrollIntoView(", StringComparison.Ordinal));
        ChecksContext.Check(
            "播放集页: 加入候选时延后一帧再改集合",
            code.Contains("_viewModel.AddMods(new[] { entry })", StringComparison.Ordinal));
    }

    /// <summary>
    /// 原版启动器的「添加模组」子页必须还在：它是 officialLauncher 布局的加 mod 通道，
    /// 两套播放集页面共存是明确需求。
    /// </summary>
    private static void CheckLegacySubpageKept(string root)
    {
        var page = Path.Combine(root, "src", "PdxLauncher.App", "Views", "AddModsPage.xaml");
        var navigation = Path.Combine(root, "src", "PdxLauncher.App", "Services", "NavigationService.cs");

        ChecksContext.Check("播放集页: 保留 AddModsPage.xaml", File.Exists(page), page);
        ChecksContext.Check(
            "播放集页: 导航仍注册 addmods",
            File.Exists(navigation)
                && File.ReadAllText(navigation).Contains("addmods", StringComparison.Ordinal),
            navigation);
    }

    /// <summary>显示方式的取值口径：新增 twoColumn，未知值仍回退原版启动器。</summary>
    private static void CheckDisplayModeValue(string root)
    {
        ChecksContext.Equal("显示方式: 常量值", "twoColumn", PlaySetPageDisplayModes.TwoColumn);
        ChecksContext.Equal(
            "显示方式: twoColumn 归一化（大小写 / 空白无关）",
            PlaySetPageDisplayModes.TwoColumn,
            PlaySetPageDisplayModes.Normalize(" TWOCOLUMN "));
        ChecksContext.Equal(
            "显示方式: twoColumn 不再回退原版",
            PlaySetPageDisplayModes.TwoColumn,
            PlaySetPageDisplayModes.Normalize(PlaySetPageDisplayModes.TwoColumn));
        ChecksContext.Check(
            "显示方式: IsTwoColumn 只认 twoColumn",
            PlaySetPageDisplayModes.IsTwoColumn(PlaySetPageDisplayModes.TwoColumn)
                && !PlaySetPageDisplayModes.IsTwoColumn(PlaySetPageDisplayModes.Placeholder)
                && !PlaySetPageDisplayModes.IsTwoColumn(PlaySetPageDisplayModes.OfficialLauncher));

        // 目录在 App 层（引用不到），读源码文本钉住「下拉里确实多了这一项」。
        var catalog = Path.Combine(root, "src", "PdxLauncher.App", "Services", "PlaySetPageDisplayModeCatalog.cs");
        var text = File.Exists(catalog) ? File.ReadAllText(catalog) : string.Empty;
        ChecksContext.Check(
            "显示方式: 设置页下拉含双列项",
            text.Contains("PlaySetPageDisplayModes.TwoColumn", StringComparison.Ordinal)
                && text.Contains("Keys.SettingsInterfacePlaysetTwoColumnTitle", StringComparison.Ordinal)
                && text.Contains("Keys.SettingsInterfacePlaysetTwoColumnDesc", StringComparison.Ordinal),
            catalog);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
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
