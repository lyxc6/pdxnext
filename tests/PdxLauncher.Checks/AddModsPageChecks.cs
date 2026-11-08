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
        var c96 = template.IndexOf("<ColumnDefinition Width=\"96\" />", StringComparison.Ordinal);
        ChecksContext.Check(
            "添加模组页: 列 24/40/*/96 且顺序正确",
            c24 >= 0 && c40 > c24 && cStar > c40 && c96 > cStar,
            "24=" + c24 + " 40=" + c40 + " *=" + cStar + " 96=" + c96);

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

        // 排序必须在搜索过滤之后做：先缩小集合再排序，结果才与「搜索 + 排序」的直觉一致。
        ChecksContext.Check(
            "添加模组页: 排序在搜索过滤之后应用",
            vm.Contains("ApplySort(_allRows.Where(row => row.Matches(query)))", StringComparison.Ordinal)
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

        // 注意断言的是 Rows 本身而不是 _allRows：后者是搜索前的全量快照，Clear + 重建是正常的。
        ChecksContext.Check(
            "添加模组页: 可见行是整表替换（不留 Clear()+逐条 Add）",
            !Regex.IsMatch(vm, @"(?<!_all)Rows\.(Clear|Add)\(")
                && vm.Contains("Rows = query.Length == 0", StringComparison.Ordinal),
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
