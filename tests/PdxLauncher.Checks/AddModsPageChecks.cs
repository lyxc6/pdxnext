namespace PdxLauncher.Checks;

using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 「添加模组」子页的版式与搜索稳定性自检：页头合并成两行、列表四列（24/40/*/96）、
/// 勾选框压掉默认最小宽度、新增版本列，以及搜索改成整表一次性替换。
/// <para>
/// 起因：这一页原来把标题与「加入播放集「xxx」」各占一行，看起来像 5 行；行首 CheckBox 没写
/// <c>MinWidth="0"</c>，被 WinUI 的 120 DIP 默认最小宽度撑出一大段空白，行高被顶到 90 多；
/// 名称与徽标用横向 StackPanel，长名字把「订阅」徽标挤出可视区；搜索每次按键
/// <c>Rows.Clear()</c> + 逐条 Add，列表整块重建、滚动位置和行高都跳。
/// 这些问题编译器都看不见，只能在真机上表现为「歪了 / 抖一下」，所以钉成源码断言。
/// </para>
/// <para>只读仓库源码文本，不依赖网络与 UI 线程。</para>
/// </summary>
public static class AddModsPageChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        var root = FindWorkspaceRoot();
        CheckHeader(root);
        CheckListColumns(root);
        CheckVersionColumn(root);
        CheckSort(root);
        CheckNoChineseLiterals(root);
        CheckSearchNoRebuild(root);
        return ChecksContext.Failures - before;
    }

    /// <summary>页头必须是两行：48（返回 + 标题 + 「→ 播放集名」同一行 + 计数固定 96）、32（搜索 320）。</summary>
    private static void CheckHeader(string root)
    {
        var xaml = Read(root, "src", "PdxLauncher.App", "Views", "AddModsPage.xaml");
        if (!ChecksContext.Check("添加模组页: AddModsPage.xaml 存在", xaml.Length > 0, "AddModsPage.xaml"))
        {
            return;
        }

        var rows = Section(xaml, "<Grid.RowDefinitions>", "</Grid.RowDefinitions>");
        ChecksContext.Check(
            "添加模组页: 页头行高 48",
            rows.Contains("<RowDefinition Height=\"48\" />", StringComparison.Ordinal),
            rows);
        ChecksContext.Check(
            "添加模组页: 搜索行高 32",
            rows.Contains("<RowDefinition Height=\"32\" />", StringComparison.Ordinal),
            rows);
        ChecksContext.Check(
            "添加模组页: 行高 48 在 32 之前（页头在上、搜索在下）",
            rows.IndexOf("Height=\"48\"", StringComparison.Ordinal)
                < rows.IndexOf("Height=\"32\"", StringComparison.Ordinal),
            rows);

        // 播放集名不再单独占一行：它必须排在标题之后、搜索框之前（同一行、caption 小字）。
        var title = xaml.IndexOf("txtAddModsTitle", StringComparison.Ordinal);
        var target = xaml.IndexOf("txtAddModsTarget", StringComparison.Ordinal);
        var search = xaml.IndexOf("txtAddModsSearch", StringComparison.Ordinal);
        ChecksContext.Check(
            "添加模组页: 播放集名与标题同一行（标题 < 播放集名 < 搜索框）",
            title >= 0 && target > title && search > target,
            "title=" + title + " target=" + target + " search=" + search);

        var targetBlock = ElementBlock(xaml, "txtAddModsTarget");
        ChecksContext.Check(
            "添加模组页: 播放集名用 caption 级小字",
            targetBlock.Contains("CaptionTextBlockStyle", StringComparison.Ordinal),
            targetBlock);

        // 计数固定 96 列、右对齐：数字位数变化不能把标题挤动。
        var countBlock = ElementBlock(xaml, "txtAddModsCandidateCount");
        ChecksContext.Check(
            "添加模组页: 计数右对齐",
            countBlock.Contains("HorizontalAlignment=\"Right\"", StringComparison.Ordinal),
            countBlock);
        ChecksContext.Check(
            "添加模组页: 计数列固定 96",
            xaml.Contains("<ColumnDefinition Width=\"96\" />", StringComparison.Ordinal)
                && xaml.IndexOf("<ColumnDefinition Width=\"96\" />", StringComparison.Ordinal)
                    < xaml.IndexOf("txtAddModsCandidateCount", StringComparison.Ordinal),
            countBlock);

        var searchBlock = ElementBlock(xaml, "txtAddModsSearch");
        ChecksContext.Check(
            "添加模组页: 搜索框定宽 320",
            searchBlock.Contains("Width=\"320\"", StringComparison.Ordinal),
            searchBlock);
    }

    /// <summary>列表行四列 24/40/*/96，勾选框 MinWidth=0 且自带右内边距，行高 56。</summary>
    private static void CheckListColumns(string root)
    {
        var xaml = Read(root, "src", "PdxLauncher.App", "Views", "AddModsPage.xaml");
        if (xaml.Length == 0)
        {
            ChecksContext.Check("添加模组页: 列表能读", false, "AddModsPage.xaml");
            return;
        }

        var template = Section(
            xaml,
            "<DataTemplate x:DataType=\"vm:AddModRowViewModel\">",
            "</DataTemplate>");
        if (!ChecksContext.Check("添加模组页: 候选行模板存在", template.Length > 0, "DataTemplate"))
        {
            return;
        }

        var c24 = template.IndexOf("<ColumnDefinition Width=\"24\" />", StringComparison.Ordinal);
        var c40 = template.IndexOf("<ColumnDefinition Width=\"40\" />", StringComparison.Ordinal);
        var cStar = template.IndexOf("<ColumnDefinition Width=\"*\" />", StringComparison.Ordinal);
        var cAuto = template.IndexOf("<ColumnDefinition Width=\"Auto\" />", StringComparison.Ordinal);

        // 版本列必须按内容取宽（Auto），不能写死 96：一行有「mod 版本 2025.10.10」+「支持 1.15.*」两段字，
        // 远超 96 DIP，写死之后内容溢出被列表边缘裁掉，那一列里的版本支持三角也跟着看不见。
        ChecksContext.Check(
            "添加模组页: 列 24/40/*/Auto 且顺序正确",
            c24 >= 0 && c40 > c24 && cStar > c40 && cAuto > cStar,
            "24=" + c24 + " 40=" + c40 + " *=" + cStar + " Auto=" + cAuto);
        ChecksContext.Check(
            "添加模组页: 版本列宽度下限由内容 MinWidth=96 保证（不写死列宽）",
            template.Contains("MinWidth=\"96\"", StringComparison.Ordinal),
            template);
        ChecksContext.Check(
            "添加模组页: 版本列两行字各自 MaxWidth 收窄（省略号才会生效）",
            template.Contains("MaxWidth=\"200\"", StringComparison.Ordinal)
                && template.Contains("TextTrimming=\"CharacterEllipsis\"", StringComparison.Ordinal),
            "MaxWidth200=" + template.Contains("MaxWidth=\"200\"", StringComparison.Ordinal));

        // 勾选框不写 MinWidth=0 就会吃到 CheckBox 的 120 DIP 默认最小宽度，行首白留 100 多 DIP。
        var checkBlock = ElementBlock(template, "CheckAutomationId");
        ChecksContext.Check(
            "添加模组页: 勾选框 MinWidth=0",
            checkBlock.Contains("MinWidth=\"0\"", StringComparison.Ordinal),
            checkBlock);
        ChecksContext.Check(
            "添加模组页: 勾选框右内边距 8",
            checkBlock.Contains("Padding=\"0,0,8,0\"", StringComparison.Ordinal),
            checkBlock);

        var rowStart = template.IndexOf("<Grid ColumnSpacing=\"8\"", StringComparison.Ordinal);
        var rowTagEnd = rowStart >= 0 ? template.IndexOf('>', rowStart) : -1;
        var rowBlock = rowStart >= 0 && rowTagEnd > rowStart ? template[rowStart..(rowTagEnd + 1)] : string.Empty;
        ChecksContext.Check(
            "添加模组页: 行高 56",
            rowBlock.Contains("MinHeight=\"56\"", StringComparison.Ordinal),
            rowBlock);
        ChecksContext.Check(
            "添加模组页: 行内边距 8,4",
            rowBlock.Contains("Padding=\"8,4\"", StringComparison.Ordinal),
            rowBlock);

        // 缩略图列里只有它自己：40 见方，行高 56 里居中。
        var thumbStart = template.IndexOf("Grid.Column=\"1\"", StringComparison.Ordinal);
        var thumbEnd = template.IndexOf("Grid.Column=\"2\"", StringComparison.Ordinal);
        var thumbBlock = thumbStart >= 0 && thumbEnd > thumbStart ? template[thumbStart..thumbEnd] : string.Empty;
        ChecksContext.Check(
            "添加模组页: 缩略图 40",
            thumbBlock.Contains("Width=\"40\"", StringComparison.Ordinal)
                && thumbBlock.Contains("Height=\"40\"", StringComparison.Ordinal),
            thumbBlock);
    }

    /// <summary>
    /// 版本列：第一行 mod 作者版本、第二行 RequiredVersionText；偏低时显示警告图标；
    /// 判定只用 ModLibraryWarnings。
    /// </summary>
    private static void CheckVersionColumn(string root)
    {
        var xaml = Read(root, "src", "PdxLauncher.App", "Views", "AddModsPage.xaml");
        var rowVm = Read(root, "src", "PdxLauncher.App", "ViewModels", "AddModRowViewModel.cs");
        if (!ChecksContext.Check(
                "添加模组页: 版本列两个源文件都在",
                xaml.Length > 0 && rowVm.Length > 0,
                "AddModsPage.xaml / AddModRowViewModel.cs"))
        {
            return;
        }

        ChecksContext.Check(
            "添加模组页: 版本列显示 RequiredVersionText",
            xaml.Contains("Text=\"{x:Bind RequiredVersionText", StringComparison.Ordinal),
            xaml);
        ChecksContext.Check(
            "添加模组页: 版本列有稳定的 AutomationId（不挂在只在警告时可见的图标上）",
            xaml.Contains("AutomationProperties.AutomationId=\"{x:Bind VersionAutomationId", StringComparison.Ordinal)
                && ElementBlock(xaml, "Bind VersionAutomationId").Contains("Text=\"{x:Bind RequiredVersionText", StringComparison.Ordinal),
            ElementBlock(xaml, "Bind VersionAutomationId"));
        ChecksContext.Check(
            "添加模组页: 版本偏低时显示警告图标",
            xaml.Contains("HasVersionWarning", StringComparison.Ordinal)
                && xaml.Contains("SystemFillColorCautionBrush", StringComparison.Ordinal),
            xaml);

        // 三角的提示必须挂在「包住字形的命中容器」上：FontIcon 只有字形本身可命中，
        // 提示绑在它身上时鼠标落在旁边空白处不会弹（真机踩过）。容器还负责 HelpText 与 UIA 定位。
        var warningBlock = PairedElementBlock(xaml, "WarningAutomationId");
        ChecksContext.Check(
            "添加模组页: 三角提示挂在透明命中容器上（不挂字形）",
            warningBlock.Contains("Background=\"Transparent\"", StringComparison.Ordinal)
                && warningBlock.Contains("ToolTipService.ToolTip=\"{x:Bind VersionWarningText", StringComparison.Ordinal)
                && warningBlock.Contains("AutomationProperties.HelpText=\"{x:Bind VersionWarningText", StringComparison.Ordinal),
            warningBlock);
        ChecksContext.Check(
            "添加模组页: 三角容器最小命中区 24（读屏与鼠标都能点到）",
            warningBlock.Contains("MinWidth=\"24\"", StringComparison.Ordinal)
                && warningBlock.Contains("MinHeight=\"24\"", StringComparison.Ordinal)
                && warningBlock.Contains("Padding=\"4\"", StringComparison.Ordinal),
            warningBlock);
        ChecksContext.Check(
            "添加模组页: 三角的无障碍名复用 Mod 库的「版本支持提示」",
            warningBlock.Contains("L.ModlibWarningName", StringComparison.Ordinal),
            warningBlock);

        // 译名：添加模组页此前完全不翻译，用户看不到原名的中文意思；现在与 Mod 库同一张表回填。
        // 译名：添加模组页此前完全不翻译，用户看不到原名的中文意思；现在与 Mod 库同一张表回填。
        // AutomationId 挂在里层 TextBlock 上（Border 没有 automation peer，挂 Border 上 UIA 查不到），
        // 所以断言取「包含这个 Id 的 Border 整块」来核对框的样式。
        var translatedAnchor = xaml.IndexOf("TranslatedAutomationId", StringComparison.Ordinal);
        var translatedStart = translatedAnchor >= 0 ? xaml.LastIndexOf("<Border", translatedAnchor, StringComparison.Ordinal) : -1;
        var translatedEnd = translatedStart >= 0 ? xaml.IndexOf("</Border>", translatedAnchor, StringComparison.Ordinal) : -1;
        var translatedBlock = translatedStart >= 0 && translatedEnd > translatedStart
            ? xaml[translatedStart..(translatedEnd + "</Border>".Length)]
            : string.Empty;
        ChecksContext.Check(
            "添加模组页: 行内有译名框（与 Mod 库同款描边框）",
            translatedBlock.Contains("Text=\"{x:Bind TranslatedName", StringComparison.Ordinal)
                && translatedBlock.Contains("Visibility=\"{x:Bind svc:XamlVisibility.FromString(TranslatedName)", StringComparison.Ordinal)
                && translatedBlock.Contains("Padding=\"8,4\"", StringComparison.Ordinal)
                && translatedBlock.Contains("HorizontalAlignment=\"Left\"", StringComparison.Ordinal)
                && translatedBlock.Contains("CornerRadius=\"8\"", StringComparison.Ordinal),
            translatedBlock);
        ChecksContext.Check(
            "添加模组页: 译名 AutomationId 挂在 TextBlock 上（Border 没有 automation peer）",
            translatedBlock.Contains("AutomationProperties.AutomationId=\"{x:Bind TranslatedAutomationId", StringComparison.Ordinal)
                && translatedBlock.Contains("<TextBlock Text=\"{x:Bind TranslatedName", StringComparison.Ordinal),
            translatedBlock);
        ChecksContext.Check(
            "添加模组页: 行 ViewModel 暴露 TranslatedName / HasTranslatedName",
            rowVm.Contains("public string? TranslatedName", StringComparison.Ordinal)
                && rowVm.Contains("public bool HasTranslatedName", StringComparison.Ordinal)
                && rowVm.Contains("TranslatedAutomationId", StringComparison.Ordinal),
            rowVm);

        // 同一个行 VM 被两个页面共用（本页与播放集页左栏）。改一处漏一处的话，另一处界面就静默丢功能，
        // 所以这里把「两边都绑了同一批属性」钉死。
        var modsPage = Read(root, "src", "PdxLauncher.App", "Views", "ModsPage.xaml");
        ChecksContext.Check(
            "添加模组页: 候选行 VM 与播放集左栏模板共用同一批绑定（译名 + 三角）",
            modsPage.Contains("<DataTemplate x:Key=\"PlaysetCandidateRowTemplate\" x:DataType=\"vm:AddModRowViewModel\">", StringComparison.Ordinal)
                && modsPage.Contains("{x:Bind TranslatedName", StringComparison.Ordinal)
                && modsPage.Contains("FromBool(HasVersionWarning)", StringComparison.Ordinal)
                && modsPage.Contains("{x:Bind WarningAutomationId", StringComparison.Ordinal),
            modsPage);

        ChecksContext.Check(
            "添加模组页: 行 ViewModel 暴露 RequiredVersionText / HasVersionWarning",
            rowVm.Contains("public string RequiredVersionText", StringComparison.Ordinal)
                && rowVm.Contains("public bool HasVersionWarning", StringComparison.Ordinal),
            rowVm);
        ChecksContext.Check(
            "添加模组页: 版本比较复用 ModLibraryWarnings（不自己比版本号）",
            rowVm.Contains("ModLibraryWarnings.ClassifyVersionSupport", StringComparison.Ordinal)
                && !rowVm.Contains("CompareMajorMinor", StringComparison.Ordinal),
            rowVm);
        ChecksContext.Check(
            "添加模组页: 没有声明版本时给短横占位",
            rowVm.Contains("MissingVersionText", StringComparison.Ordinal),
            rowVm);

        // Vic3 有近一半 mod 只写 metadata.json 的 version（作者版本）、不写 supported_game_version：
        // 版本列必须单独显示这一项，否则这些行只剩一个短横，看起来像「没读到版本」。
        ChecksContext.Check(
            "添加模组页: 版本列显示 mod 作者版本（ModVersionText + HasModVersion）",
            xaml.Contains("Text=\"{x:Bind ModVersionText", StringComparison.Ordinal)
                && xaml.Contains("HasModVersion", StringComparison.Ordinal)
                && xaml.Contains("AutomationProperties.AutomationId=\"{x:Bind ModVersionAutomationId", StringComparison.Ordinal),
            ElementBlock(xaml, "ModVersionAutomationId"));
        ChecksContext.Check(
            "添加模组页: 行 ViewModel 暴露 ModVersionText / HasModVersion",
            rowVm.Contains("public string ModVersionText", StringComparison.Ordinal)
                && rowVm.Contains("public bool HasModVersion", StringComparison.Ordinal)
                && rowVm.Contains("ModsAddPageModVersion", StringComparison.Ordinal),
            rowVm);
    }

    /// <summary>排序：搜索行里的 PdxDropDown、三个排序键、过滤后再排序、排序选择随页面状态恢复。</summary>
    private static void CheckSort(string root)
    {
        var xaml = Read(root, "src", "PdxLauncher.App", "Views", "AddModsPage.xaml");
        var vm = Read(root, "src", "PdxLauncher.App", "ViewModels", "AddModsViewModel.cs");
        var page = Read(root, "src", "PdxLauncher.App", "Views", "AddModsPage.xaml.cs");
        if (!ChecksContext.Check(
                "添加模组页: 排序相关源文件都在",
                xaml.Length > 0 && vm.Length > 0 && page.Length > 0,
                "AddModsPage.xaml / AddModsViewModel.cs / AddModsPage.xaml.cs"))
        {
            return;
        }

        var sortBlock = ElementBlock(xaml, "cmbAddModsSort");
        ChecksContext.Check(
            "添加模组页: 排序用 PdxDropDown",
            sortBlock.Contains("controls:PdxDropDown", StringComparison.Ordinal),
            sortBlock);
        ChecksContext.Check(
            "添加模组页: 排序选项绑定 SortModes",
            sortBlock.Contains("ItemsSource=\"{x:Bind ViewModel.SortModes, Mode=OneWay}\"", StringComparison.Ordinal),
            sortBlock);
        ChecksContext.Check(
            "添加模组页: 选中项绑定 SelectedSortMode",
            sortBlock.Contains("SelectedItem=\"{x:Bind ViewModel.SelectedSortMode, Mode=OneWay}\"", StringComparison.Ordinal),
            sortBlock);
        ChecksContext.Check(
            "添加模组页: 排序变化写回 ViewModel",
            xaml.Contains("SelectionChanged=\"OnSortSelectionChanged\"", StringComparison.Ordinal)
                && page.Contains("OnSortSelectionChanged", StringComparison.Ordinal)
                && page.Contains("_viewModel.SelectedSortMode = mode", StringComparison.Ordinal),
            page);

        ChecksContext.Check(
            "添加模组页: ViewModel 有三个排序键（名称 / 大小 / 订阅时间）",
            vm.Contains("Keys.ModsAddPageSortByName", StringComparison.Ordinal)
                && vm.Contains("Keys.ModsAddPageSortBySize", StringComparison.Ordinal)
                && vm.Contains("Keys.ModsAddPageSortBySubscribed", StringComparison.Ordinal),
            vm);
        ChecksContext.Check(
            "添加模组页: 默认排序是第一个键（名称 A-Z）",
            vm.Contains("_selectedSortMode = _sortModes[0]", StringComparison.Ordinal),
            vm);

        // 排序必须在全部筛选之后做：先叠加搜索 / 来源 / 版本三道条件缩小集合再排序，
        // 结果才与「搜索 + 筛选 + 排序」的直觉一致（排序蓝图里只剩 ApplySort(matches)）。
        ChecksContext.Check(
            "添加模组页: 排序在搜索 / 筛选之后应用",
            vm.Contains("Rows = ApplySort(matches).ToArray();", StringComparison.Ordinal)
                && vm.Contains("&& MatchesSourceFilter(row)", StringComparison.Ordinal)
                && vm.Contains("&& MatchesVersionFilter(row)", StringComparison.Ordinal)
                && vm.Contains("private IEnumerable<AddModRowViewModel> ApplySort", StringComparison.Ordinal),
            vm);

        ChecksContext.Check(
            "添加模组页: 排序选择随页面状态恢复",
            page.Contains("PageStateStore.AddMods", StringComparison.Ordinal)
                && page.Contains("RestoreSortState", StringComparison.Ordinal)
                && page.Contains("SaveSortState", StringComparison.Ordinal),
            page);
    }

    /// <summary>XAML 里不许出现硬编码中文（注释除外）：所有文案走 Localizer。</summary>
    private static void CheckNoChineseLiterals(string root)
    {
        var xaml = Read(root, "src", "PdxLauncher.App", "Views", "AddModsPage.xaml");
        if (xaml.Length == 0)
        {
            ChecksContext.Check("添加模组页: 中文文案能检查", false, "AddModsPage.xaml");
            return;
        }

        var withoutComments = Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
        var match = Regex.Match(withoutComments, "[\u4e00-\u9fff]");
        ChecksContext.Check(
            "添加模组页: XAML 没有硬编码中文（走 Localizer）",
            !match.Success,
            match.Success ? withoutComments[Math.Max(0, match.Index - 60)..Math.Min(withoutComments.Length, match.Index + 60)] : string.Empty);
    }

    /// <summary>搜索改成整表一次性替换：ViewModel 不再 Clear()+逐条 Add，绑定改 OneTime，建议只在文本变化时刷新。</summary>
    private static void CheckSearchNoRebuild(string root)
    {
        var vm = Read(root, "src", "PdxLauncher.App", "ViewModels", "AddModsViewModel.cs");
        var page = Read(root, "src", "PdxLauncher.App", "Views", "AddModsPage.xaml.cs");
        var xaml = Read(root, "src", "PdxLauncher.App", "Views", "AddModsPage.xaml");
        var glue = Read(root, "src", "PdxLauncher.App", "Services", "SearchSuggestBox.cs");
        if (!ChecksContext.Check(
                "添加模组页: 搜索相关源文件都在",
                vm.Length > 0 && page.Length > 0 && xaml.Length > 0 && glue.Length > 0,
                "AddModsViewModel.cs / AddModsPage.xaml.cs / AddModsPage.xaml / SearchSuggestBox.cs"))
        {
            return;
        }

        // 注意断言的是 Rows 本身而不是 _allRows：后者是筛选前的全量快照，Clear + 重建是正常的。
        ChecksContext.Check(
            "添加模组页: 可见行是整表替换（不留 Clear()+逐条 Add）",
            !Regex.IsMatch(vm, @"(?<!_all)Rows\.(Clear|Add)\(")
                && vm.Contains("Rows = ApplySort(matches).ToArray();", StringComparison.Ordinal),
            vm);

        ChecksContext.Check(
            "添加模组页: 搜索框 Text 是 OneTime（绑定不反写控件）",
            xaml.Contains("Text=\"{x:Bind ViewModel.SearchText, Mode=OneTime}\"", StringComparison.Ordinal),
            xaml);

        ChecksContext.Check(
            "添加模组页: 建议只在查询文本变化时刷新",
            page.Contains("_lastSuggestQuery", StringComparison.Ordinal),
            page);

        ChecksContext.Check(
            "添加模组页: 建议列表内容没变就不重设 ItemsSource",
            glue.Contains("IsSameSequence", StringComparison.Ordinal),
            glue);
    }

    /// <summary>取从 <paramref name="startMarker"/> 到 <paramref name="endMarker"/> 的片段。</summary>
    private static string Section(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        return end > start ? text[start..end] : string.Empty;
    }

    /// <summary>取包含 <paramref name="marker"/> 的那个自闭合元素（从最近的 &lt; 到下一个 /&gt;）。</summary>
    private static string ElementBlock(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return string.Empty;
        }

        var start = text.LastIndexOf('<', index);
        var end = text.IndexOf("/>", index, StringComparison.Ordinal);
        return start >= 0 && end > start ? text[start..(end + 2)] : string.Empty;
    }

    /// <summary>
    /// 取包含 <paramref name="marker"/> 的那个**成对标签**元素（从最近的 &lt; 到匹配的 &lt;/Tag&gt;）。
    /// 三角与译名都是包着子元素的 <c>Border</c>，自闭合版 <see cref="ElementBlock"/> 取不到它们。
    /// </summary>
    private static string PairedElementBlock(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return string.Empty;
        }

        var start = text.LastIndexOf('<', index);
        if (start < 0)
        {
            return string.Empty;
        }

        var tagEnd = text.IndexOf('>', index);
        if (tagEnd < 0)
        {
            return string.Empty;
        }

        // 自闭合就没有配对标签：原样返回这一小段。
        if (text[tagEnd - 1] == '/')
        {
            return text[start..(tagEnd + 1)];
        }

        // 标签名（去掉属性与命名空间前缀后取第一个词）。
        var openTag = text[start..(tagEnd + 1)];
        var nameStart = 1;
        while (nameStart < openTag.Length && char.IsWhiteSpace(openTag[nameStart]))
        {
            nameStart++;
        }

        var nameEnd = nameStart;
        while (nameEnd < openTag.Length && !char.IsWhiteSpace(openTag[nameEnd]) && openTag[nameEnd] != '>')
        {
            nameEnd++;
        }

        var tagName = openTag[nameStart..nameEnd];
        var closeTag = "</" + tagName + ">";
        var close = text.IndexOf(closeTag, tagEnd, StringComparison.Ordinal);
        return close > tagEnd ? text[start..(close + closeTag.Length)] : text[start..(tagEnd + 1)];
    }

    private static string Read(string root, params string[] parts)
    {
        var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tools", "i18n")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
