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
        "rdoDiagLogAll",
        "rdoDiagLogWarn",
        "rdoDiagLogError",
        "tglDiagAutoRefresh",
        "tglDiagLogWrap",
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
        var catalog = Read(root, Path.Combine("src", "PdxLauncher.App", "ViewModels", "CacheRowCatalog.cs"));
        var navigation = Read(root, Path.Combine("src", "PdxLauncher.App", "Services", "NavigationService.cs"));
        var mainWindowXaml = Read(root, Path.Combine("src", "PdxLauncher.App", "MainWindow.xaml"));
        var mainWindowCode = Read(root, Path.Combine("src", "PdxLauncher.App", "MainWindow.xaml.cs"));
        var appCode = Read(root, Path.Combine("src", "PdxLauncher.App", "App.xaml.cs"));
        var program = Read(root, Path.Combine("src", "PdxLauncher.App", "Program.cs"));
        var settingsViewModel = Read(root, Path.Combine("src", "PdxLauncher.App", "ViewModels", "SettingsViewModel.cs"));
        var settings = Read(root, Path.Combine("src", "PdxLauncher.Core", "Configuration", "AppSettings.cs"));
        var strings = Read(root, Path.Combine("src", "PdxLauncher.App", "Localization", "Strings.g.cs"));
        var zhKeys = Read(root, Path.Combine("tools", "i18n", "zh-CN", "diagnostics.json"));
        var enKeys = Read(root, Path.Combine("tools", "i18n", "en-US", "diagnostics.json"));

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

        ChecksContext.Check("日志尾部只读", xaml.Contains("IsReadOnly=\"True\"", StringComparison.Ordinal));
        ChecksContext.Check("日志尾部折行由开关驱动",
            xaml.Contains("TextWrapping=\"{x:Bind svc:XamlTextWrapping.FromBool(ViewModel.LogWrap), Mode=OneWay}\"", StringComparison.Ordinal));
        ChecksContext.Check("两个日志开关标签常显（不靠 OnContent）",
            xaml.Contains("Text=\"{x:Bind L.DiagnosticsLogWrap, Mode=OneWay}\"", StringComparison.Ordinal) &&
            xaml.Contains("Text=\"{x:Bind L.DiagnosticsLogAutoRefresh, Mode=OneWay}\"", StringComparison.Ordinal) &&
            !xaml.Contains("OnContent=\"{x:Bind L.DiagnosticsLogWrap", StringComparison.Ordinal) &&
            !xaml.Contains("OnContent=\"{x:Bind L.DiagnosticsLogAutoRefresh", StringComparison.Ordinal));
        var filterRow = SliceBetween(xaml, "<controls:PdxWrapPanel Spacing=\"16\">", "</controls:PdxWrapPanel>");
        ChecksContext.Check("过滤行用 PdxWrapPanel（窄窗口折行，标签不会被裁成 0 宽）",
            filterRow.Contains("rdoDiagLogAll", StringComparison.Ordinal) &&
            filterRow.Contains("tglDiagLogWrap", StringComparison.Ordinal) &&
            filterRow.Contains("tglDiagAutoRefresh", StringComparison.Ordinal));
        var tailBox = SliceBetween(xaml, "<TextBox Grid.Row=\"1\"", "/>");
        ChecksContext.Check("日志尾部高度跟随窗口（不再写死 340）",
            !tailBox.Contains("Height=\"340\"", StringComparison.Ordinal) &&
            tailBox.Contains("MinHeight=\"160\"", StringComparison.Ordinal) &&
            tailBox.Contains("Grid.Row=\"1\"", StringComparison.Ordinal));
        ChecksContext.Check("日志分区移出滚动容器（独立 Grid）",
            xaml.Contains("x:Name=\"diagSectionScroller\"", StringComparison.Ordinal) &&
            Regex.IsMatch(xaml, @"<Grid x:Name=""secLog""[^>]*>\s*<Grid.RowDefinitions>", RegexOptions.Singleline) &&
            !SliceBetween(xaml, "<ScrollViewer x:Name=\"diagSectionScroller\"", "</ScrollViewer>")
                .Contains("x:Name=\"secLog\"", StringComparison.Ordinal));
        ChecksContext.Check("日志尾部滚动条用 ScrollViewer 附加属性",
            tailBox.Contains("ScrollViewer.HorizontalScrollBarVisibility", StringComparison.Ordinal) &&
            tailBox.Contains("ScrollViewer.VerticalScrollBarVisibility", StringComparison.Ordinal) &&
            !Regex.IsMatch(tailBox, @"(?<!ScrollViewer\.)(Horizontal|Vertical)ScrollBarVisibility"));
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
        ChecksContext.Check("AppSettings 记录日志折行开关",
            settings.Contains("DiagnosticsLogWrap", StringComparison.Ordinal));

        var wrapHelper = Read(root, Path.Combine("src", "PdxLauncher.App", "Services", "XamlTextWrapping.cs"));
        ChecksContext.Check("换行辅助同时支持折行与不折行",
            wrapHelper.Contains("TextWrapping.Wrap", StringComparison.Ordinal) &&
            wrapHelper.Contains("TextWrapping.NoWrap", StringComparison.Ordinal));
        ChecksContext.Check("视图模型暴露 LogWrap 开关",
            viewModel.Contains("public bool LogWrap", StringComparison.Ordinal));
        ChecksContext.Check("折行开关写回设置",
            viewModel.Contains("DiagnosticsLogWrap = value", StringComparison.Ordinal) &&
            viewModel.Contains("SaveSettings()", StringComparison.Ordinal));

        // i18n：两侧键集合由生成器保证一致，这里只确认区域文件与生成结果都到位。
        ChecksContext.Check("zh-CN 有 diagnostics 区域", zhKeys.Length > 0);
        ChecksContext.Check("en-US 有 diagnostics 区域", enKeys.Length > 0);
        ChecksContext.Equal("两侧 diagnostics 键数一致", CountKeys(zhKeys), CountKeys(enKeys));
        ChecksContext.Check("生成表含页面标题键",
            strings.Contains("DiagnosticsPageTitle", StringComparison.Ordinal));
        ChecksContext.Check("生成表含页脚导航键",
            strings.Contains("ShellNavDiagnostics", StringComparison.Ordinal));
        ChecksContext.Check("zh-CN 有折行开关文案", zhKeys.Contains("diagnostics.log.wrap", StringComparison.Ordinal));
        ChecksContext.Check("en-US 有折行开关文案", enKeys.Contains("diagnostics.log.wrap", StringComparison.Ordinal));
        ChecksContext.Check("生成表含折行开关键",
            strings.Contains("DiagnosticsLogWrap", StringComparison.Ordinal));

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static int CountKeys(string json) =>
        Regex.Matches(json, "\"diagnostics\\.").Count;

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
