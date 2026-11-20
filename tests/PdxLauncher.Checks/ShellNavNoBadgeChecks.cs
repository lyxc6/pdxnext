namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 左栏「游戏库」不显示计数徽标的自检。用户明确要求去掉那个数字，所以这里钉住「已经移除」：
/// 菜单项本身保留（AutomationId / Tag 不变），但它所在的 <c>NavigationViewItem</c> 段里不再挂徽标；
/// 视图模型不再提供计数与读屏文案，窗口代码不再为它公开 <c>ViewModel</c>，两种语言的资源表里也没有
/// 对应文案键。只读源码与文案源，不启动 WinUI。
/// </summary>
public static class ShellNavNoBadgeChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        Console.WriteLine("[ShellNavNoBadgeChecks] 左栏「游戏库」不显示计数徽标");

        var root = FindWorkspaceRoot();
        var xamlPath = Path.Combine(root, "src", "PdxLauncher.App", "MainWindow.xaml");
        var codePath = Path.Combine(root, "src", "PdxLauncher.App", "MainWindow.xaml.cs");
        var viewModelPath = Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "ShellViewModel.cs");

        ChecksContext.Check("外壳徽标: MainWindow.xaml 存在", File.Exists(xamlPath), xamlPath);
        ChecksContext.Check("外壳徽标: MainWindow.xaml.cs 存在", File.Exists(codePath), codePath);
        ChecksContext.Check("外壳徽标: ShellViewModel.cs 存在", File.Exists(viewModelPath), viewModelPath);
        if (!File.Exists(xamlPath) || !File.Exists(codePath) || !File.Exists(viewModelPath))
        {
            return ChecksContext.Failures - before;
        }

        var xaml = File.ReadAllText(xamlPath);
        var code = File.ReadAllText(codePath);
        var viewModel = File.ReadAllText(viewModelPath);

        CheckLibraryItem(xaml);
        CheckNoBadgeMarkup(xaml);
        CheckShellViewModel(viewModel);
        CheckMainWindowExposure(code);
        CheckLocalizedKey(root);

        return ChecksContext.Failures - before;
    }

    /// <summary>菜单项本身必须还在（只去掉右边的数字），且同一段里不再有任何徽标标记。</summary>
    private static void CheckLibraryItem(string xaml)
    {
        var start = xaml.IndexOf("AutomationProperties.AutomationId=\"navLibrary\"", StringComparison.Ordinal);
        ChecksContext.Check("外壳徽标: navLibrary 菜单项仍在", start >= 0);
        if (start < 0)
        {
            return;
        }

        var end = xaml.IndexOf("</NavigationViewItem>", start, StringComparison.Ordinal);
        var item = end > start ? xaml[start..end] : xaml[start..];

        ChecksContext.Check(
            "外壳徽标: navLibrary 的 AutomationId 未变",
            item.Contains("AutomationProperties.AutomationId=\"navLibrary\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: navLibrary 的 Tag 未变",
            item.Contains("Tag=\"library\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: navLibrary 段内不再有徽标",
            !item.Contains("InfoBadge", StringComparison.Ordinal),
            item.Contains("InfoBadge", StringComparison.Ordinal) ? "该项仍挂着徽标标记" : "已移除");
    }

    /// <summary>整个外壳 XAML 里不再残留徽标标记、自动化 Id 与绑定表达式。</summary>
    private static void CheckNoBadgeMarkup(string xaml)
    {
        ChecksContext.Check(
            "外壳徽标: 全文不再挂 NavigationViewItem.InfoBadge",
            !xaml.Contains("NavigationViewItem.InfoBadge", StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: 全文不再有 badgeNavLibrary",
            !xaml.Contains("badgeNavLibrary", StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: 全文不再绑 ViewModel.GamesCount",
            !xaml.Contains("ViewModel.GamesCount", StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: 全文不再绑 ViewModel.LibraryBadgeName",
            !xaml.Contains("ViewModel.LibraryBadgeName", StringComparison.Ordinal));
    }

    /// <summary>视图模型里不再有计数、读屏文案与「无游戏」三个只为徽标存在的成员。</summary>
    private static void CheckShellViewModel(string viewModel)
    {
        ChecksContext.Check(
            "外壳徽标: ShellViewModel 不再有 GamesCount",
            !viewModel.Contains("GamesCount", StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: ShellViewModel 不再有 LibraryBadgeName",
            !viewModel.Contains("LibraryBadgeName", StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: ShellViewModel 不再有 HasNoGames",
            !viewModel.Contains("HasNoGames", StringComparison.Ordinal));
    }

    /// <summary>XAML 不再需要用公开的 ViewModel 取外壳数据。</summary>
    private static void CheckMainWindowExposure(string code)
    {
        ChecksContext.Check(
            "外壳徽标: MainWindow 不再公开 ViewModel 属性",
            !code.Contains("public ShellViewModel ViewModel { get; }", StringComparison.Ordinal));
    }

    /// <summary>两种语言的文案源里都不再保留徽标读屏文案的键。</summary>
    private static void CheckLocalizedKey(string root)
    {
        var zhPath = I18nResw.ResourcesPath(root, "zh-CN");
        var enPath = I18nResw.ResourcesPath(root, "en-US");
        ChecksContext.Check("外壳徽标: 中文文案源存在", File.Exists(zhPath), zhPath);
        ChecksContext.Check("外壳徽标: 英文文案源存在", File.Exists(enPath), enPath);

        var zh = I18nResw.Read(root, "zh-CN");
        var en = I18nResw.Read(root, "en-US");

        ChecksContext.Check(
            "外壳徽标: 中文资源不含 ShellBadgeLibraryName",
            I18nResw.Get(zh, "ShellBadgeLibraryName") is null);
        ChecksContext.Check(
            "外壳徽标: 英文资源不含 ShellBadgeLibraryName",
            I18nResw.Get(en, "ShellBadgeLibraryName") is null);
        ChecksContext.Check(
            "外壳徽标: 中英文键集仍一致",
            zh.Count == en.Count,
            "zh " + zh.Count + " / en " + en.Count);
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PdxLauncher.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
