namespace PdxLauncher.Checks;

using System.Text.Json;
using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 左栏「游戏库」计数徽标自检：钉住 InfoBadge 挂在 navLibrary 项上、值来自 ShellViewModel.GamesCount、
/// 读屏名字来自拼好的 <c>shell.badgeLibraryName</c>（InfoBadge 的数值 UI Automation 读不出来）。
/// 只读源码与文案源，不启动 WinUI。
/// </summary>
public static class ShellNavBadgeChecks
{
    private static readonly Regex Placeholder = new(@"\{\d+\}", RegexOptions.CultureInvariant);

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

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
        CheckShellViewModel(viewModel);
        CheckMainWindowExposure(code);
        CheckLocalizedKey(root);

        return ChecksContext.Failures - before;
    }

    /// <summary>InfoBadge 必须落在同一个 navLibrary &lt;NavigationViewItem&gt; 段内，值绑 GamesCount。</summary>
    private static void CheckLibraryItem(string xaml)
    {
        var start = xaml.IndexOf("AutomationProperties.AutomationId=\"navLibrary\"", StringComparison.Ordinal);
        ChecksContext.Check("外壳徽标: 找到 navLibrary 菜单项", start >= 0);
        if (start < 0)
        {
            return;
        }

        var end = xaml.IndexOf("</NavigationViewItem>", start, StringComparison.Ordinal);
        var item = end > start ? xaml[start..end] : xaml[start..];

        ChecksContext.Check(
            "外壳徽标: navLibrary 项带 InfoBadge",
            item.Contains("<NavigationViewItem.InfoBadge>", StringComparison.Ordinal)
                && item.Contains("<InfoBadge", StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: 徽标 Value 绑 ShellViewModel.GamesCount",
            item.Contains("Value=\"{x:Bind ViewModel.GamesCount, Mode=OneWay}\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: 徽标 AutomationId 是 badgeNavLibrary",
            item.Contains("AutomationProperties.AutomationId=\"badgeNavLibrary\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: 徽标读屏名字绑 LibraryBadgeName",
            item.Contains("AutomationProperties.Name=\"{x:Bind ViewModel.LibraryBadgeName, Mode=OneWay}\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: 计数为 0 时徽标隐藏",
            item.Contains(
                "Visibility=\"{x:Bind svc:XamlVisibility.FromBoolInverse(ViewModel.HasNoGames), Mode=OneWay}\"",
                StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: navLibrary 的 Tag 未改动",
            item.Contains("Tag=\"library\"", StringComparison.Ordinal));
    }

    /// <summary>ShellViewModel 提供计数与拼好的读屏文案，并在 ReplaceGames 里通知两者。</summary>
    private static void CheckShellViewModel(string viewModel)
    {
        ChecksContext.Check(
            "外壳徽标: ShellViewModel 有 GamesCount",
            viewModel.Contains("public int GamesCount => Games.Count;", StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: LibraryBadgeName 用 shell.badgeLibraryName 拼装",
            viewModel.Contains("public string LibraryBadgeName", StringComparison.Ordinal)
                && viewModel.Contains("Keys.ShellBadgeLibraryName", StringComparison.Ordinal));

        var replaceStart = viewModel.IndexOf("public void ReplaceGames(", StringComparison.Ordinal);
        ChecksContext.Check("外壳徽标: 找到 ReplaceGames", replaceStart >= 0);
        if (replaceStart < 0)
        {
            return;
        }

        var open = viewModel.IndexOf('{', replaceStart);
        var close = open >= 0 ? viewModel.IndexOf("\n    }", open, StringComparison.Ordinal) : -1;
        var body = open >= 0 && close > open ? viewModel[open..close] : viewModel[open..];

        ChecksContext.Check(
            "外壳徽标: ReplaceGames 通知 GamesCount",
            body.Contains("OnPropertyChanged(nameof(GamesCount))", StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: ReplaceGames 通知 LibraryBadgeName",
            body.Contains("OnPropertyChanged(nameof(LibraryBadgeName))", StringComparison.Ordinal));
    }

    /// <summary>XAML 要能从 MainWindow 拿到公开的 ShellViewModel。</summary>
    private static void CheckMainWindowExposure(string code)
    {
        ChecksContext.Check(
            "外壳徽标: MainWindow 公开 ViewModel 属性",
            code.Contains("public ShellViewModel ViewModel { get; }", StringComparison.Ordinal));
        ChecksContext.Check(
            "外壳徽标: 构造里给 ViewModel 赋值",
            code.Contains("ViewModel = _viewModel;", StringComparison.Ordinal));
    }

    /// <summary>新键在两种语言里都存在，占位符数量一致；资源文件里也有对应条目。</summary>
    private static void CheckLocalizedKey(string root)
    {
        var zhPath = I18nResw.ResourcesPath(root, "zh-CN");
        var enPath = I18nResw.ResourcesPath(root, "en-US");
        ChecksContext.Check("外壳徽标: 中文文案源存在", File.Exists(zhPath), zhPath);
        ChecksContext.Check("外壳徽标: 英文文案源存在", File.Exists(enPath), enPath);

        var zh = I18nResw.Read(root, "zh-CN");
        var en = I18nResw.Read(root, "en-US");

        var zhValue = I18nResw.Get(zh, "shell.badgeLibraryName");
        var enValue = I18nResw.Get(en, "shell.badgeLibraryName");
        ChecksContext.Check(
            "外壳徽标: 中文有 shell.badgeLibraryName",
            !string.IsNullOrWhiteSpace(zhValue));
        ChecksContext.Check(
            "外壳徽标: 英文有 shell.badgeLibraryName",
            !string.IsNullOrWhiteSpace(enValue));

        if (zhValue is not null && enValue is not null)
        {
            ChecksContext.Check(
                "外壳徽标: 中英文占位符一致",
                Placeholder.Matches(zhValue).Count == Placeholder.Matches(enValue).Count,
                zhValue + " / " + enValue);
            ChecksContext.Check("外壳徽标: 占位符带游戏数", zhValue.Contains("{0}", StringComparison.Ordinal));
        }

        ChecksContext.Check(
            "外壳徽标: 资源含 ShellBadgeLibraryName",
            zh.ContainsKey("ShellBadgeLibraryName"));
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
