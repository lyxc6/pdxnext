namespace PdxLauncher.Checks;

using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 「性能与日志」页的静态自检：页面文件、导航注册、分区与自动化 id、启动埋点、文案不走硬编码。
/// 这些断言只读源码，不需要启动 WinUI，因此能在自检控制台里跑。
/// </summary>
public static class DiagnosticsPageChecks
{
    private static readonly string[] MetricAutomationIds =
    [
        "pageDiagnostics",
        "lstDiagnosticsSections",
        "navDiagOverview",
        "navDiagCache",
        "navDiagLog",
        "navDiagNetwork",
        "navDiagStartup",
        "btnDiagRefresh",
        "btnDiagRefreshCache",
        "btnDiagRefreshCacheOverview",
        "btnDiagClearAllCaches",
        "btnDiagRefreshLog",
        "btnDiagOpenLogFolder",
        "cmbDiagLogFilter",
        "tglDiagAutoRefresh",
        "tglDiagLogWrap",
        "tglDiagLive",
        "diagGauges",
        "txtDiagLogTail",
        "txtDiagStatus",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        Console.WriteLine("[DiagnosticsPageChecks] 性能与日志页");

        var root = FindRepositoryRoot();
        if (root is null)
        {
            ChecksContext.Check("找到仓库根目录（version.props）", false);
            return 1;
        }

        var xaml = Read(root, Path.Combine("src", "PdxLauncher.App", "Views", "DiagnosticsPage.xaml"));
        var codeBehind = Read(root, Path.Combine("src", "PdxLauncher.App", "Views", "DiagnosticsPage.xaml.cs"));
        var viewModel = Read(root, Path.Combine("src", "PdxLauncher.App", "ViewModels", "DiagnosticsViewModel.cs"));
        var rows = Read(root, Path.Combine("src", "PdxLauncher.App", "ViewModels", "DiagnosticsRows.cs"));
        var gaugeControl = Read(root, Path.Combine("src", "PdxLauncher.App", "Controls", "PdxGaugeRing.cs"));
        var gaugeStyle = Read(root, Path.Combine("src", "PdxLauncher.App", "Controls", "PdxGaugeRing.xaml"));
        var catalog = Read(root, Path.Combine("src", "PdxLauncher.App", "ViewModels", "CacheRowCatalog.cs"));
        var navigation = Read(root, Path.Combine("src", "PdxLauncher.App", "Services", "NavigationService.cs"));
        var mainWindowXaml = Read(root, Path.Combine("src", "PdxLauncher.App", "MainWindow.xaml"));
        var mainWindowCode = Read(root, Path.Combine("src", "PdxLauncher.App", "MainWindow.xaml.cs"));
        var appCode = Read(root, Path.Combine("src", "PdxLauncher.App", "App.xaml.cs"));
        var program = Read(root, Path.Combine("src", "PdxLauncher.App", "Program.cs"));
        var settingsViewModel = Read(root, Path.Combine("src", "PdxLauncher.App", "ViewModels", "SettingsViewModel.cs"));
        var settings = Read(root, Path.Combine("src", "PdxLauncher.Core", "Configuration", "AppSettings.cs"));
        var zhTable = I18nResw.Read(root, "zh-CN");
        var enTable = I18nResw.Read(root, "en-US");

        ChecksContext.Check("页面 XAML 存在", xaml.Length > 0);
        ChecksContext.Check("页面代码后置存在", codeBehind.Length > 0);
        ChecksContext.Check("视图模型存在", viewModel.Length > 0);
        ChecksContext.Check("缓存行目录存在（设置页与性能页共用）", catalog.Length > 0);

        // 导航注册：标签、类型映射、页脚入口。
        ChecksContext.Check("NavigationService 定义 diagnostics 标签",
            navigation.Contains("DiagnosticsTag = \"diagnostics\"", StringComparison.Ordinal));
        ChecksContext.Check("NavigationService 映射到 DiagnosticsPage",
            navigation.Contains("PdxLauncher.App.Views.DiagnosticsPage", StringComparison.Ordinal));
        ChecksContext.Check("页脚有性能与日志入口",
            mainWindowXaml.Contains("AutomationProperties.AutomationId=\"navDiagnostics\"", StringComparison.Ordinal));
        ChecksContext.Check("页脚入口用 Tag=\"diagnostics\"",
            mainWindowXaml.Contains("Tag=\"diagnostics\"", StringComparison.Ordinal));

        var diagnosticsIndex = mainWindowXaml.IndexOf("navDiagnostics", StringComparison.Ordinal);
        var settingsIndex = mainWindowXaml.IndexOf("AutomationProperties.AutomationId=\"navSettings\"", StringComparison.Ordinal);
        ChecksContext.Check("性能与日志排在设置之前", diagnosticsIndex >= 0 && settingsIndex > diagnosticsIndex);

        // 页面骨架与自动化 id。
        foreach (var automationId in MetricAutomationIds)
        {
            ChecksContext.Check(
                "页面包含自动化 id " + automationId,
                xaml.Contains("AutomationId=\"" + automationId + "\"", StringComparison.Ordinal));
        }

        string[] sections = ["secOverview", "secCache", "secLog", "secNetwork", "secStartup"];
        foreach (var section in sections)
        {
            ChecksContext.Check("页面包含分区 " + section,
                xaml.Contains("x:Name=\"" + section + "\"", StringComparison.Ordinal));
        }

        var tagOrder = new[] { "Tag=\"0\"", "Tag=\"1\"", "Tag=\"2\"", "Tag=\"3\"", "Tag=\"4\"" }
            .Select(tag => xaml.IndexOf(tag, StringComparison.Ordinal))
            .ToArray();
        ChecksContext.Check("五个导航项按 0..4 排列",
            tagOrder.All(index => index >= 0) && tagOrder.SequenceEqual(tagOrder.Order()));


        // 概览实时仪表：四个环 + 实时开关 + 每秒分级刷新定时器。
        ChecksContext.Check("概览用 PdxGaugeRing 模板显示四个核心环",
            xaml.Contains("GaugeRingTemplate", StringComparison.Ordinal) &&
            xaml.Contains("controls:PdxGaugeRing", StringComparison.Ordinal));
        ChecksContext.Check("概览仪表用 PdxWrapPanel 折行",
            SliceBetween(xaml, "<ItemsControl x:Name=\"diagGauges\"", "</ItemsControl>")
                .Contains("controls:PdxWrapPanel", StringComparison.Ordinal));
        ChecksContext.Check("实时开关绑定 LiveEnabled 并接 Toggled",
            xaml.Contains("IsOn=\"{x:Bind ViewModel.LiveEnabled, Mode=TwoWay}\"", StringComparison.Ordinal) &&
            codeBehind.Contains("OnLiveToggled", StringComparison.Ordinal));
        ChecksContext.Check("概览实时刷新用 DispatcherTimer 每秒采样",
            codeBehind.Contains("Interval = TimeSpan.FromSeconds(1)", StringComparison.Ordinal) &&
            codeBehind.Contains("_liveTick % 5 == 0", StringComparison.Ordinal));
        ChecksContext.Check("离开概览或页面卸载时停掉实时定时器",
            codeBehind.Contains("StopLiveTimer();", StringComparison.Ordinal) &&
            codeBehind.Contains("CurrentSectionIndex() == OverviewSectionIndex", StringComparison.Ordinal));
        ChecksContext.Check("ViewModel 暴露 Gauges 与 LiveStatusText",
            viewModel.Contains("ObservableCollection<GaugeViewModel> Gauges", StringComparison.Ordinal) &&
            viewModel.Contains("LiveStatusText", StringComparison.Ordinal) &&
            viewModel.Contains("PerformanceGaugeMath.ComputeMemory", StringComparison.Ordinal));

        // 动画三项：补间弧长、更新脉冲、60 秒迷你趋势线。
        ChecksContext.Check("仪表环用 Storyboard + CubicEase 补间弧长",
            gaugeControl.Contains("new DoubleAnimation", StringComparison.Ordinal) &&
            gaugeControl.Contains("new CubicEase", StringComparison.Ordinal) &&
            gaugeControl.Contains("nameof(AnimatedPercent)", StringComparison.Ordinal));
        ChecksContext.Check("仪表环数值更新有高亮脉冲",
            gaugeControl.Contains("StartPulse", StringComparison.Ordinal) &&
            gaugeControl.Contains("nameof(PulseOpacity)", StringComparison.Ordinal) &&
            gaugeStyle.Contains("x:Name=\"GlowPath\"", StringComparison.Ordinal));
        ChecksContext.Check("仪表环有 60 秒迷你趋势折线",
            gaugeControl.Contains("nameof(TrendPoints)", StringComparison.Ordinal) &&
            gaugeControl.Contains("new LineSegment", StringComparison.Ordinal) &&
            gaugeStyle.Contains("x:Name=\"TrendPath\"", StringComparison.Ordinal) &&
            xaml.Contains("TrendPoints=\"{x:Bind TrendPoints, Mode=OneWay}\"", StringComparison.Ordinal));
        ChecksContext.Check("仪表就地更新而不是 Clear+重建（补间前提）",
            viewModel.Contains("gauge.Apply(reading.Percent", StringComparison.Ordinal) &&
            !viewModel.Contains("Gauges.Clear();", StringComparison.Ordinal) &&
            rows.Contains("public bool Apply(double percent", StringComparison.Ordinal));

        ChecksContext.Check("概览指标卡按稳定键就地更新，不再每次采样整批重建",
            viewModel.Contains("existing = Metrics.FirstOrDefault(metric => metric.Key == key)", StringComparison.Ordinal) &&
            !viewModel.Contains("AddMetric(", StringComparison.Ordinal) &&
            rows.Contains("void Apply(string title, string value, string detail)", StringComparison.Ordinal));
        ChecksContext.Check("优化建议按稳定键就地更新，消失的建议才移除",
            viewModel.Contains("existing = Hints.FirstOrDefault(row => row.Code == hint.Code)", StringComparison.Ordinal) &&
            viewModel.Contains("Hints.RemoveAt(index)", StringComparison.Ordinal) &&
            !viewModel.Contains("Hints.Clear();", StringComparison.Ordinal) &&
            rows.Contains("public void Apply(string title, string detail)", StringComparison.Ordinal));

        // 日志页改造：TextViewControl 整行着色 + 下拉筛选 + 两个开关对齐。
        ChecksContext.Check("日志尾部用 TextViewControl（支持整行着色）",
            xaml.Contains("xmlns:tv=\"using:TextViewControl\"", StringComparison.Ordinal)
            && xaml.Contains("<tv:TextControl", StringComparison.Ordinal)
            && xaml.Contains("x:Name=\"LogView\"", StringComparison.Ordinal));
        ChecksContext.Check("日志级别筛选改为 PdxDropDown 下拉",
            xaml.Contains("cmbDiagLogFilter", StringComparison.Ordinal)
            && xaml.Contains("SelectedItem=\"{x:Bind ViewModel.SelectedLogFilterOption, Mode=TwoWay}\"", StringComparison.Ordinal)
            && !xaml.Contains("rdoDiagLogAll", StringComparison.Ordinal));
        ChecksContext.Check("自动换行与自动刷新开关对齐（同为常显标签 + TwoWay VM 绑定）",

            xaml.Contains("Text=\"{x:Bind L.DiagnosticsLogWrap, Mode=OneWay}\"", StringComparison.Ordinal) &&
            xaml.Contains("Text=\"{x:Bind L.DiagnosticsLogAutoRefresh, Mode=OneWay}\"", StringComparison.Ordinal) &&
            xaml.Contains("IsOn=\"{x:Bind ViewModel.LogWrap, Mode=TwoWay}\"", StringComparison.Ordinal) &&
            xaml.Contains("IsOn=\"{x:Bind ViewModel.AutoRefresh, Mode=TwoWay}\"", StringComparison.Ordinal) &&
            !xaml.Contains("Toggled=\"OnAutoRefreshToggled\"", StringComparison.Ordinal));
        var filterRow = SliceBetween(xaml, "<controls:PdxWrapPanel Spacing=\"16\">", "</controls:PdxWrapPanel>");
        ChecksContext.Check("过滤行用 PdxWrapPanel（窄窗口折行，标签不会被裁成 0 宽）",
            filterRow.Contains("cmbDiagLogFilter", StringComparison.Ordinal) &&
            filterRow.Contains("tglDiagLogWrap", StringComparison.Ordinal) &&
            filterRow.Contains("tglDiagAutoRefresh", StringComparison.Ordinal));
        var logArea = SliceBetween(xaml, "<Grid Grid.Row=\"1\">", "</Grid>");
        ChecksContext.Check("日志区高度跟随窗口（不再写死 340）",
            !logArea.Contains("Height=\"340\"", StringComparison.Ordinal) &&
            logArea.Contains("MinHeight=\"160\"", StringComparison.Ordinal) &&
            xaml.Contains("x:Name=\"secLog\"", StringComparison.Ordinal));
        ChecksContext.Check("日志视图代码里配置整行高亮与自动换行",
            codeBehind.Contains("HighlightLanguage = new SyntaxHighlightLanguage", StringComparison.Ordinal)
            && codeBehind.Contains("TextWrapEnabled =", StringComparison.Ordinal)
            && codeBehind.Contains("HighlightRule(@\"^.*\\[ERROR\\].*$\")", StringComparison.Ordinal));
        ChecksContext.Check("日志视图空占位与内容灌入",
            xaml.Contains("x:Name=\"LogTailEmptyText\"", StringComparison.Ordinal)
            && codeBehind.Contains("LogView.SetLines", StringComparison.Ordinal));
        ChecksContext.Check("日志分区移出滚动容器（独立 Grid）",
            xaml.Contains("x:Name=\"diagSectionScroller\"", StringComparison.Ordinal) &&
            Regex.IsMatch(xaml, @"<Grid x:Name=""secLog""[^>]*>\s*<Grid.RowDefinitions>", RegexOptions.Singleline) &&
            !SliceBetween(xaml, "<ScrollViewer x:Name=\"diagSectionScroller\"", "</ScrollViewer>")
                .Contains("x:Name=\"secLog\"", StringComparison.Ordinal));
        ChecksContext.Check("界面文案全部走 i18n（XAML 无硬编码中文）",
            !Regex.IsMatch(StripComments(xaml), @"[\p{IsCJKUnifiedIdeographs}]"));

        // 代码后置：分区持久化、清缓存先确认、打开日志目录。
        ChecksContext.Check("分区写入 LastDiagnosticsSection",
            codeBehind.Contains("LastDiagnosticsSection", StringComparison.Ordinal));
        ChecksContext.Check("清除全部缓存前弹确认",
            codeBehind.Contains("ConfirmOverlay.ConfirmAsync", StringComparison.Ordinal));
        ChecksContext.Check("日志目录用 ShellLauncher 打开",
            codeBehind.Contains("ShellLauncher.TryStart", StringComparison.Ordinal));
        ChecksContext.Check("自动刷新用 DispatcherTimer",
            codeBehind.Contains("DispatcherTimer", StringComparison.Ordinal));
        ChecksContext.Check("自动刷新仅在日志分区且开关打开时运行",
            codeBehind.Contains("SyncAutoRefreshTimer", StringComparison.Ordinal)
            && codeBehind.Contains("LogSectionIndex", StringComparison.Ordinal));

        // 启动耗时埋点：五个里程碑都要有人打点，否则页面会缺段。
        ChecksContext.Check("进程入口打 app-start",
            program.Contains("StartupTimeline.Mark(StartupTimeline.AppStart)", StringComparison.Ordinal));
        ChecksContext.Check("服务装配完成打 services-ready",
            appCode.Contains("StartupTimeline.Mark(StartupTimeline.ServicesReady)", StringComparison.Ordinal));
        ChecksContext.Check("窗口激活后打 window-created",
            appCode.Contains("StartupTimeline.Mark(StartupTimeline.WindowCreated)", StringComparison.Ordinal));
        ChecksContext.Check("外壳就绪打 shell-ready",
            mainWindowCode.Contains("StartupTimeline.Mark(StartupTimeline.ShellReady)", StringComparison.Ordinal));
        ChecksContext.Check("首屏完成打 home-loaded",
            mainWindowCode.Contains("StartupTimeline.Mark(StartupTimeline.HomeLoaded)", StringComparison.Ordinal));

        // 内存快照要进 Release 日志：这两处曾经包在 #if DEBUG 里，页面就看不到真实数值。
        ChecksContext.Check("启动内存快照不再只在 Debug", !appCode.Contains("#if DEBUG", StringComparison.Ordinal));
        ChecksContext.Check("关窗内存快照不再只在 Debug", !mainWindowCode.Contains("#if DEBUG", StringComparison.Ordinal));

        // 设置页与性能页共用同一份缓存行定义，避免两处清除逻辑漂移。
        ChecksContext.Check("设置页复用 CacheRowCatalog",
            settingsViewModel.Contains("CacheRowCatalog.Create()", StringComparison.Ordinal));
        ChecksContext.Check("设置页不再单独定义缓存行标题",
            !settingsViewModel.Contains("SettingsCacheRowThemeTitle", StringComparison.Ordinal));

        ChecksContext.Check("AppSettings 记录性能页上次分节",
            settings.Contains("LastDiagnosticsSection", StringComparison.Ordinal));
        ChecksContext.Check("AppSettings 记录折行与自动刷新开关",
            settings.Contains("DiagnosticsLogWrap", StringComparison.Ordinal)
            && settings.Contains("DiagnosticsLogAutoRefresh", StringComparison.Ordinal));

        ChecksContext.Check("视图模型暴露 LogWrap 开关",
            viewModel.Contains("public bool LogWrap", StringComparison.Ordinal));
        ChecksContext.Check("视图模型暴露 AutoRefresh 开关",
            viewModel.Contains("public bool AutoRefresh", StringComparison.Ordinal));
        ChecksContext.Check("两个开关都写回设置",
            viewModel.Contains("DiagnosticsLogWrap = value", StringComparison.Ordinal) &&
            viewModel.Contains("DiagnosticsLogAutoRefresh = value", StringComparison.Ordinal) &&
            viewModel.Contains("SaveSettings()", StringComparison.Ordinal));
        ChecksContext.Check("日志显示行由解析函数生成",
            viewModel.Contains("LogTailReader.ParseDisplayLines", StringComparison.Ordinal));

        // i18n：两种语言的资源都来自同一份 resw 契约，这里确认区域、生成结果与关键键都在。
        ChecksContext.Check("zh-CN 有 diagnostics 区域", CountKeys(zhTable) > 0);
        ChecksContext.Check("en-US 有 diagnostics 区域", CountKeys(enTable) > 0);
        ChecksContext.Equal("两侧 diagnostics 键数一致", CountKeys(zhTable), CountKeys(enTable));
        ChecksContext.Check("生成表含页面标题键", zhTable.ContainsKey("DiagnosticsPageTitle"));
        ChecksContext.Check("生成表含页脚导航键", zhTable.ContainsKey("ShellNavDiagnostics"));
        ChecksContext.Check("zh-CN 有折行开关文案", zhTable.ContainsKey("DiagnosticsLogWrap"));
        ChecksContext.Check("en-US 有折行开关文案", enTable.ContainsKey("DiagnosticsLogWrap"));
        ChecksContext.Check("生成表含折行开关键", zhTable.ContainsKey("DiagnosticsLogWrap"));

        // 实时仪表的双语文案：两侧键集与占位符一致，切语言时标题/状态不会缺键。
        foreach (var key in new[]
                 {
                     "DiagnosticsGaugeMemory", "DiagnosticsGaugeCache", "DiagnosticsGaugeLog", "DiagnosticsGaugeStartup",
                     "DiagnosticsGaugePercent", "DiagnosticsGaugeLiveOn", "DiagnosticsGaugeLiveOff",
                     "DiagnosticsGaugeLiveToggle", "DiagnosticsGaugeSubtitle",
                 })
        {
            ChecksContext.Check("zh-CN 有实时仪表文案 " + key, zhTable.ContainsKey(key));
            ChecksContext.Check("en-US 有实时仪表文案 " + key, enTable.ContainsKey(key));
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static int CountKeys(Dictionary<string, string> table) =>
        table.Keys.Count(key => key.StartsWith("Diagnostics", StringComparison.Ordinal));

    private static string SliceBetween(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        if (from < 0)
        {
            return string.Empty;
        }

        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        return to < 0 ? text[from..] : text[from..(to + end.Length)];
    }

    private static string StripComments(string text) =>
        Regex.Replace(text, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private static string Read(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "version.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
