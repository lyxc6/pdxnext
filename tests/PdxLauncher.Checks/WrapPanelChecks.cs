namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 按钮行换行自检：<see cref="WrapLayout"/> 的换行边界，以及 <c>ModStudioPage.xaml</c> 里两排按钮、
/// <c>ModsPage.xaml</c> 播放集工具条确实换成了能折行的 <c>PdxWrapPanel</c>。
/// <para>
/// 起因：详情面板那一排五个按钮在默认窗口宽下就超出可用宽度（≈660 DIP vs ≈544 DIP），
/// 而 WinUI 3 没有 WrapPanel，水平 StackPanel 不换行、外层 ScrollViewer 横向禁滚，
/// 于是最后一个按钮被裁成「绑…」。播放集工具条更糟：左侧按钮簇只增不减，把星号列里的
/// 搜索框压到 0 宽、再裁掉右侧「添加模组」。这种问题不会崩、编译器也看不见，只能在真机上
/// 表现为「按钮没了」，所以换行数学与 XAML 接线都要被钉住。
/// </para>
/// <para>纯数学用例 + 源码断言，不读写磁盘（只读仓库里的源码文本），不依赖网络与 UI 线程。</para>
/// </summary>
public static class WrapPanelChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckSingleLine();
        CheckWrapBoundary();
        CheckLineHeight();
        CheckSkippedItems();
        CheckDegenerateInputs();
        CheckXamlWiring();
        CheckPlaysetToolbarWiring();
        return ChecksContext.Failures - before;
    }

    private static void CheckSingleLine()
    {
        // 100 + 8 + 80 + 8 + 60 = 256 ≤ 400：一行，间距只在行内累加。
        var one = WrapLayout.Compute(
            new[] { Item(100, 30), Item(80, 30), Item(60, 30) },
            400,
            8);
        ChecksContext.Equal("一行：项数", 3, one.Items.Count);
        ChecksContext.Equal("一行：总宽", 256.0, one.Width);
        ChecksContext.Equal("一行：总高", 30.0, one.Height);
        ChecksContext.Equal("一行：第一项 X", 0.0, one.Items[0].X);
        ChecksContext.Equal("一行：第二项 X", 108.0, one.Items[1].X);
        ChecksContext.Equal("一行：第三项 X", 196.0, one.Items[2].X);
        ChecksContext.Equal("一行：Y 全是 0", 0.0, one.Items[2].Y);
        ChecksContext.Equal("一行：摆放宽度等于自然宽", 80.0, one.Items[1].Width);
    }

    private static void CheckWrapBoundary()
    {
        var items = new[] { Item(100, 20), Item(100, 20), Item(100, 20) };

        // 100 + 8 + 100 + 8 + 100 = 316：刚好放得下。
        var exact = WrapLayout.Compute(items, 316, 8);
        ChecksContext.Equal("刚好放得下：一行", 1, DistinctRows(exact));
        ChecksContext.Equal("刚好放得下：总宽", 316.0, exact.Width);

        // 315 差 1 DIP：第三项整项折到第二行。
        var tight = WrapLayout.Compute(items, 315, 8);
        ChecksContext.Equal("差 1 DIP：折成两行", 2, DistinctRows(tight));
        ChecksContext.Equal("差 1 DIP：第一行宽", 208.0, tight.Width);
        ChecksContext.Equal("差 1 DIP：第三项 X 回到行首", 0.0, tight.Items[2].X);
        ChecksContext.Equal("差 1 DIP：第三项 Y", 28.0, tight.Items[2].Y);
        ChecksContext.Equal("差 1 DIP：总高", 48.0, tight.Height);
    }

    private static void CheckLineHeight()
    {
        // 每项都放不下：三行，行高各取本行最高项，行间 8。
        var stacked = WrapLayout.Compute(
            new[] { Item(100, 40), Item(100, 20), Item(100, 20) },
            100,
            8);
        ChecksContext.Equal("三行：第二项 Y", 48.0, stacked.Items[1].Y);
        ChecksContext.Equal("三行：第三项 Y", 76.0, stacked.Items[2].Y);
        ChecksContext.Equal("三行：总高", 96.0, stacked.Height);
        ChecksContext.Equal("三行：总宽", 100.0, stacked.Width);

        // 同一行里的高矮项：Y 按行对齐，行高取最高。
        var mixed = WrapLayout.Compute(new[] { Item(100, 40), Item(100, 20) }, 150, 8);
        ChecksContext.Equal("行高取最高：第二项 Y", 48.0, mixed.Items[1].Y);
        ChecksContext.Equal("行高取最高：总高", 68.0, mixed.Height);
    }

    private static void CheckSkippedItems()
    {
        // 0 宽项被跳过：不占位、也不产生额外间距（否则 10+8+10 会变成 10+8+0+8+10 而折行）。
        var withZero = WrapLayout.Compute(
            new[] { Item(10, 10), Item(0, 10), Item(10, 10) },
            28,
            8);
        ChecksContext.Equal("0 宽项：只剩两项", 2, withZero.Items.Count);
        ChecksContext.Equal("0 宽项：不折行", 1, DistinctRows(withZero));
        ChecksContext.Equal("0 宽项：第二项 X 不含多余间距", 18.0, withZero.Items[1].X);
        ChecksContext.Equal("0 宽项：总宽", 28.0, withZero.Width);

        // 下标保留原始位置：跳过的项不产生 placement，但后一项的 Index 不因此前移。
        var skippedHead = WrapLayout.Compute(new[] { Item(0, 10), Item(100, 20) }, 200, 8);
        ChecksContext.Equal("跳过首项：保留原始下标", 1, skippedHead.Items[0].Index);
        ChecksContext.Equal("跳过首项：X 仍是 0", 0.0, skippedHead.Items[0].X);
    }

    private static void CheckDegenerateInputs()
    {
        // 空列表：0×0，不抛。
        var empty = WrapLayout.Compute(Array.Empty<WrapLayout.WrapItem>(), 400, 8);
        ChecksContext.Equal("空列表：无摆放项", 0, empty.Items.Count);
        ChecksContext.Equal("空列表：宽 0", 0.0, empty.Width);
        ChecksContext.Equal("空列表：高 0", 0.0, empty.Height);

        // 无限宽 / NaN 宽 / 0 宽都表示「宽度不限制」，排成一行。
        var infinite = WrapLayout.Compute(new[] { Item(300, 20), Item(300, 20) }, double.PositiveInfinity, 8);
        ChecksContext.Equal("无限宽：一行", 1, DistinctRows(infinite));
        ChecksContext.Equal("无限宽：总宽", 608.0, infinite.Width);

        var notANumber = WrapLayout.Compute(new[] { Item(300, 20), Item(300, 20) }, double.NaN, 8);
        ChecksContext.Equal("NaN 宽：一行", 1, DistinctRows(notANumber));

        var zero = WrapLayout.Compute(new[] { Item(300, 20), Item(300, 20) }, 0, 8);
        ChecksContext.Equal("0 宽：一行", 1, DistinctRows(zero));

        // 单项比可用宽度还宽：独占一行、原尺寸摆放，不能无限换行或把宽度算成负数。
        var oversized = WrapLayout.Compute(new[] { Item(500, 30) }, 100, 8);
        ChecksContext.Equal("超宽单项：仍只有一个摆放项", 1, oversized.Items.Count);
        ChecksContext.Equal("超宽单项：宽度保留原值", 500.0, oversized.Items[0].Width);
        ChecksContext.Equal("超宽单项：总宽", 500.0, oversized.Width);

        // 间距非法（NaN / 负数）按 0。
        var badSpacing = WrapLayout.Compute(new[] { Item(100, 20), Item(100, 20) }, 250, double.NaN);
        ChecksContext.Equal("NaN 间距：按 0 处理", 100.0, badSpacing.Items[1].X);
        var negativeSpacing = WrapLayout.Compute(new[] { Item(100, 20), Item(100, 20) }, 250, -8);
        ChecksContext.Equal("负间距：按 0 处理", 100.0, negativeSpacing.Items[1].X);

        // 尺寸非有限（子元素还没量出尺寸）的项跳过，不参与排版。
        var nonFinite = WrapLayout.Compute(
            new[] { Item(double.NaN, 20), Item(double.PositiveInfinity, 20), Item(80, 20) },
            400,
            8);
        ChecksContext.Equal("非有限尺寸：全部跳过", 1, nonFinite.Items.Count);
    }

    private static void CheckXamlWiring()
    {
        var root = FindWorkspaceRoot();
        var xaml = Read(root, "src", "PdxLauncher.App", "Views", "ModStudioPage.xaml");
        var panel = Read(root, "src", "PdxLauncher.App", "Controls", "PdxWrapPanel.cs");
        var layout = Read(root, "src", "PdxLauncher.App", "Services", "WrapLayout.cs");

        // 1. 详情那一排：五个按钮都在同一个换行面板里，且外层不再有水平 StackPanel。
        var detail = Section(xaml, "<ctl:PdxWrapPanel Spacing=\"8\">");
        ChecksContext.Check("详情按钮行：用的是 PdxWrapPanel", detail.Length > 0);
        ChecksContext.Equal("详情按钮行：五个按钮", 5, Occurrences(detail, "</Button>"));
        ChecksContext.Check(
            "详情按钮行：五个按钮的 AutomationId 都在",
            new[] { "modStudioOpenSteam", "modStudioEditDescriptor", "modStudioOpenFolder",
                    "modStudioOpenWorkshop", "modStudioBindWorkshop" }
                .All(id => detail.Contains(id, StringComparison.Ordinal)));
        // 五个按钮必须是折行面板的直接子元素：旧写法把整排塞进一个水平 StackPanel，窄窗口下不折行、右侧按钮被裁。
        // 按钮内部「图标 + 文字」的 StackPanel 是允许的，所以只比较「第一个按钮」与「第一个 StackPanel」的先后。
        var detailBody = detail.IndexOf('>') + 1;
        var firstButtonInDetail = detail.IndexOf("<Button", detailBody, StringComparison.Ordinal);
        var firstStackInDetail = detail.IndexOf("<StackPanel", detailBody, StringComparison.Ordinal);
        ChecksContext.Check(
            "详情按钮行：按钮直接放在折行面板里（没有水平 StackPanel 包住整排）",
            firstButtonInDetail > detailBody && (firstStackInDetail < 0 || firstStackInDetail > firstButtonInDetail));

        // 2. 上传面板的 BBCode 工具条：11 个按钮同样要能折行。
        var toolbar = Section(xaml, "<ctl:PdxWrapPanel Spacing=\"4\"");
        ChecksContext.Check("BBCode 工具条：用的是 PdxWrapPanel", toolbar.Length > 0);
        ChecksContext.Check(
            "BBCode 工具条：AutomationId 还在",
            toolbar.Contains("modStudioBbcodeToolbar", StringComparison.Ordinal));
        ChecksContext.Equal("BBCode 工具条：11 个按钮", 11, Occurrences(toolbar, "btnBbCode"));

        // 3. 面板默认间距与 XAML 里的写法一致（换行面板只有这一个间距来源）。
        ChecksContext.Check("PdxWrapPanel：继承 Panel", panel.Contains(": Panel", StringComparison.Ordinal));
        ChecksContext.Check(
            "PdxWrapPanel：默认间距 8.0",
            panel.Contains("new PropertyMetadata(8.0", StringComparison.Ordinal));
        ChecksContext.Check(
            "PdxWrapPanel：折叠子元素不参与排版",
            panel.Contains("Visibility.Collapsed", StringComparison.Ordinal));

        // 4. 算法保持「纯 C#」：一旦引用了 XAML/Windows 类型，检查工程就链接不进来，这条会先失败。
        ChecksContext.Check(
            "WrapLayout：不引用 XAML 类型",
            !layout.Contains("Microsoft.UI", StringComparison.Ordinal)
                && !layout.Contains("Windows.Foundation", StringComparison.Ordinal));
    }

    /// <summary>
    /// 播放集工具条：左侧按钮簇必须是折行面板、拿到的是有限宽度（首列 *），
    /// 搜索框与「添加模组」留在面板外并保持固定宽度——否则窄窗口下又会被挤没或裁掉。
    /// </summary>
    private static void CheckPlaysetToolbarWiring()
    {
        var root = FindWorkspaceRoot();
        var xaml = Read(root, "src", "PdxLauncher.App", "Views", "ModsPage.xaml");

        var strip = Section(xaml, "<controls:PdxWrapPanel Grid.Column=\"0\"", "</controls:PdxWrapPanel>");
        ChecksContext.Check("播放集工具条：左侧按钮簇用的是 PdxWrapPanel", strip.Length > 0);
        // 工具条按钮全部是自闭合的 <Button ... />，所以数开始标签而不是 </Button>。
        ChecksContext.Equal("播放集工具条：折行面板里 10 个按钮", 10, Occurrences(strip, "<Button"));
        ChecksContext.Check(
            "播放集工具条：11 个控件 AutomationId 都在",
            new[]
            {
                "cmbPlaySets", "btnNewPlaySet", "btnRenamePlaySet", "btnDeletePlaySet",
                "btnActivatePlaySet", "btnEnableAllMods", "btnDisableAllMods",
                "btnRepairModPaths", "btnAutoSort", "btnAutoSortCancel", "btnUndoAutoSort",
            }.All(id => strip.Contains(id, StringComparison.Ordinal)));

        // 首列必须是 *：Auto 列会把无限宽度传给折行面板，面板就永远排成一行。
        // 只匹配到属性前缀、不要求紧跟 '>'：工具条 Grid 后面可能还有别的属性（例如三种布局共用的
        // Visibility 绑定），那时开始标签会折成多行。
        var grid = xaml.IndexOf("<Grid Grid.Row=\"0\" ColumnSpacing=", StringComparison.Ordinal);
        var firstDef = grid < 0 ? -1 : xaml.IndexOf("<ColumnDefinition", grid, StringComparison.Ordinal);
        var firstDefEnd = firstDef < 0 ? -1 : xaml.IndexOf("/>", firstDef, StringComparison.Ordinal);
        ChecksContext.Check(
            "播放集工具条：首列是星号列（折行面板拿到有限宽度）",
            firstDefEnd > firstDef
                && xaml[firstDef..firstDefEnd].Contains("Width=\"*\"", StringComparison.Ordinal));

        // 搜索框与添加模组在折行面板之外：窄窗口下不能被卷进第二行，也不能被按钮簇压没。
        var panelEnd = xaml.IndexOf("</controls:PdxWrapPanel>", StringComparison.Ordinal);
        var searchName = xaml.IndexOf("x:Name=\"SearchBox\"", StringComparison.Ordinal);
        var addButton = xaml.IndexOf("btnAddMods", StringComparison.Ordinal);
        ChecksContext.Check(
            "播放集工具条：搜索框在折行面板之后",
            panelEnd >= 0 && searchName > panelEnd);
        ChecksContext.Check(
            "播放集工具条：添加模组在搜索框之后",
            searchName >= 0 && addButton > searchName);

        var searchBlock = ElementBlock(xaml, "x:Name=\"SearchBox\"");
        ChecksContext.Check(
            "播放集工具条：搜索框固定 200 宽",
            searchBlock.Contains("Width=\"200\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "播放集工具条：搜索框不再靠 MinWidth 兜底",
            !searchBlock.Contains("MinWidth", StringComparison.Ordinal)
                && !searchBlock.Contains("MaxWidth", StringComparison.Ordinal));
    }

    private static WrapLayout.WrapItem Item(double width, double height) => new(width, height);

    /// <summary>数出内容占了几行（同一行里的项 Y 相同）。</summary>
    private static int DistinctRows(WrapLayout.WrapLayoutResult result)
        => result.Items.Select(x => x.Y).Distinct().Count();

    private static int Occurrences(string text, string needle)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>取从 <paramref name="startMarker"/> 起、到 <paramref name="endMarker"/> 为止的片段。</summary>
    private static string Section(
        string text,
        string startMarker,
        string endMarker = "</ctl:PdxWrapPanel>")
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        return end > start ? text[start..end] : string.Empty;
    }

    /// <summary>取包含 <paramref name="marker"/> 的那个自闭合元素（从行首的 &lt; 到结尾的 /&gt;）。</summary>
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
