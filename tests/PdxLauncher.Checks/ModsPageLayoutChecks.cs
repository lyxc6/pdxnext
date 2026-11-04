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
        "splitTwoColumns",
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
        CheckSplitterKept(xaml);
        CheckColumnsNotOverConstrained(xaml);
        CheckHintRemoved(xaml);
        CheckCodeBehind(code);
        CheckSplitterUsesEventSize(root);
        CheckIncrementalUpdates(root);
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

    /// <summary>
    /// 分隔条：三列布局（左栏命名列 + 12 DIP 分隔条 + 右栏星号列），控件本身带自动化 id。
    /// 分隔条被删掉或列定义退回两列时，两栏就再也调不动宽度了。
    /// </summary>
    private static void CheckSplitterKept(string xaml)
    {
        ChecksContext.Check(
            "播放集页: 双列有命名左栏列",
            xaml.Contains("x:Name=\"TwoColumnLeftColumn\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "播放集页: 双列放了分隔条控件",
            xaml.Contains("<controls:PdxColumnSplitter", StringComparison.Ordinal));
        ChecksContext.Check(
            "播放集页: 分隔条接线在代码后置",
            xaml.Contains("x:Name=\"ColumnSplitter\"", StringComparison.Ordinal)
                && xaml.Contains("AutomationId=\"splitTwoColumns\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// 双列的两个列定义不许带 <c>MinWidth</c>：窗口拉窄时左栏必须能低于 280 让位给右栏，
    /// 而 XAML 的 MinWidth 会把代码算好的让位宽度顶回去，整张 Grid 超出可用宽度，
    /// 右栏就被窗口边缘裁掉（用户反馈的「左右列状态」就是这么来的）。
    /// 上下限的唯一真源是 <c>Services/ColumnResize.cs</c>，这里钉住它别再长回来。
    /// </summary>
    private static void CheckColumnsNotOverConstrained(string xaml)
    {
        var start = xaml.IndexOf("x:Name=\"TwoColumnLeftColumn\"", StringComparison.Ordinal);
        if (!ChecksContext.Check("播放集页: 找到双列左栏列定义", start >= 0, "TwoColumnLeftColumn"))
        {
            return;
        }

        // 从右栏列定义开始到 Grid.ColumnDefinitions 结束，覆盖两栏的完整定义区间。
        var leftLineStart = xaml.LastIndexOf('<', start);
        var end = xaml.IndexOf("</Grid.ColumnDefinitions>", start, StringComparison.Ordinal);
        var columns = end > start ? xaml[leftLineStart..end] : xaml[leftLineStart..];

        ChecksContext.Check(
            "播放集页: 双列左栏列定义不写 MinWidth",
            !columns.Contains("MinWidth=", StringComparison.Ordinal),
            columns.Trim());
        ChecksContext.Check(
            "播放集页: 双列左栏仍有硬上限 MaxWidth",
            columns.Contains("MaxWidth=\"900\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// 窗口尺寸变化必须用事件自带的 <c>e.NewSize.Width</c>：在 SizeChanged 处理里读
    /// <c>TwoColumnGrid.ActualWidth</c> 拿到的是上一次 arrange 的旧宽度，钳制会算回原值、
    /// 命中「值没变」早退，左栏不让位，右栏被挤成负宽（用户缩小窗口的那次反馈）。
    /// </summary>
    private static void CheckSplitterUsesEventSize(string root)
    {
        var path = Path.Combine(root, "src", "PdxLauncher.App", "Views", "ModsPage.xaml.cs");
        if (!ChecksContext.Check("播放集页: ModsPage.xaml.cs 存在", File.Exists(path), path))
        {
            return;
        }

        var source = File.ReadAllText(path);
        var body = SliceMethod(source, "private void OnTwoColumnGridSizeChanged(");
        ChecksContext.Check(
            "播放集页: 尺寸变化用 e.NewSize.Width",
            body.Contains("e.NewSize.Width", StringComparison.Ordinal),
            body.Trim());
        ChecksContext.Check(
            "播放集页: 尺寸变化不再读 ActualWidth",
            !body.Contains("ActualWidth", StringComparison.Ordinal),
            body.Trim());

        var apply = SliceMethod(source, "private void ApplyLeftColumnWidth(");
        ChecksContext.Check(
            "播放集页: 渲染宽度按意图宽度钳制",
            apply.Contains("ColumnResize.Clamp(_preferredLeftColumnWidth", StringComparison.Ordinal)
                && apply.Contains("TwoColumnLeftColumn.Width", StringComparison.Ordinal),
            apply.Trim());

        var dragged = SliceMethod(source, "private void OnSplitterDragged(");
        ChecksContext.Check(
            "播放集页: 拖动按「起点宽度 + 总位移」算，不累加增量",
            dragged.Contains("_dragStartLeftColumnWidth + offset", StringComparison.Ordinal)
                && dragged.Contains("ColumnResize.Clamp(", StringComparison.Ordinal),
            dragged.Trim());

        var dragStarted = SliceMethod(source, "private void OnSplitterDragStarted(");
        ChecksContext.Check(
            "播放集页: 按下时记住起点宽度",
            dragStarted.Contains("_dragStartLeftColumnWidth = _preferredLeftColumnWidth", StringComparison.Ordinal),
            dragStarted.Trim());
    }

    /// <summary>
    /// 左栏底部的说明文字已按用户反馈删掉：占着顶部 400 DIP 里最宝贵的一行，
    /// 说的还是「勾选即加入」这种一眼就能看出来的事。留着就是重复噪音。
    /// </summary>
    private static void CheckHintRemoved(string xaml)
    {
        ChecksContext.Check(
            "播放集页: 左栏底部说明文字已移除",
            !xaml.Contains("ModsTwoColumnHint", StringComparison.Ordinal)
                && !xaml.Contains("mods.twoColumn.hint", StringComparison.Ordinal));
    }

    /// <summary>
    /// 增删 / 换位必须走增量更新：`AddMods` 与 `RemoveMod` 里再出现 `RebuildVisibleMods()` /
    /// `RebuildCandidates()` 就会退回「清空重造两栏所有行」——缩略图重解码、滚动条跳回顶部、
    /// 开关动画重放，用户看到的就是整页闪一下。
    /// </summary>
    private static void CheckIncrementalUpdates(string root)
    {
        var path = Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "ModsViewModel.cs");
        if (!ChecksContext.Check("播放集页: ModsViewModel.cs 存在", File.Exists(path), path))
        {
            return;
        }

        var source = File.ReadAllText(path);
        (string Signature, string Label)[] incremental =
        [
            ("public void AddMods(", "AddMods"),
            ("public void RemoveMod(", "RemoveMod"),
            ("public void MoveMod(", "MoveMod"),
            ("public PositionEditResult TryMoveModToPosition(", "TryMoveModToPosition"),
        ];

        foreach (var (signature, label) in incremental)
        {
            var body = SliceMethod(source, signature);
            ChecksContext.Check("播放集页: " + label + " 不再全量重建", body.Length > 0, signature);
            if (body.Length == 0)
            {
                continue;
            }

            ChecksContext.Check(
                "播放集页: " + label + " 不调 RebuildVisibleMods",
                !body.Contains("RebuildVisibleMods(", StringComparison.Ordinal));
            ChecksContext.Check(
                "播放集页: " + label + " 不调 RebuildCandidates",
                !body.Contains("RebuildCandidates(", StringComparison.Ordinal));
            ChecksContext.Check(
                "播放集页: " + label + " 刷新位次",
                body.Contains("RefreshRowPositions(", StringComparison.Ordinal));
        }

        ChecksContext.Check(
            "播放集页: 换位走 Mods.Move",
            SliceMethod(source, "public void MoveMod(").Contains("Mods.Move(", StringComparison.Ordinal));
    }

    /// <summary>切出一个方法体（从签名后的第一个 <c>{</c> 到配对的 <c>}</c>）；找不到返回空串。</summary>
    private static string SliceMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var open = source.IndexOf('{', start);
        if (open < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[open..(i + 1)];
                }
            }
        }

        return source[open..];
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
