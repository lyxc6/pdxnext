namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>六个页面用右键直接刷新：可见刷新按钮移除，上下文菜单与文本输入保持优先。</summary>
public static class RefreshGestureChecks
{
    private static readonly (string Xaml, string Code, string RemovedId)[] Pages =
    [
        ("GameLibraryPage", "GameLibraryPage", "btnRefreshLibrary"),
        ("ModLibraryPage", "ModLibraryPage", "btnRefreshModLibrary"),
        ("WorkshopPage", "WorkshopPage", "btnWorkshopRefresh"),
        ("ModStudioPage", "ModStudioPage", "modStudioRefresh"),
        ("SavesPage", "SavesPage", "btnRefreshSaves"),
        ("SaveBackupsPage", "SaveBackupsPage", "btnBackupsRefresh"),
    ];

    private const string HomeXaml = "HomePage";
    private const string HomeCode = "HomePage";

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var root = FindWorkspaceRoot();

        foreach (var (xamlName, codeName, removedId) in Pages)
        {
            var xamlPath = Path.Combine(root, "src", "PdxLauncher.App", "Views", xamlName + ".xaml");
            var codePath = Path.Combine(root, "src", "PdxLauncher.App", "Views", codeName + ".xaml.cs");
            if (!ChecksContext.Check("右键刷新: " + xamlName + ".xaml 存在", File.Exists(xamlPath), xamlPath)
                || !ChecksContext.Check("右键刷新: " + codeName + ".xaml.cs 存在", File.Exists(codePath), codePath))
            {
                continue;
            }

            var xaml = File.ReadAllText(xamlPath);
            var code = File.ReadAllText(codePath);
            ChecksContext.Check(
                "右键刷新: " + xamlName + " 根容器接管 RightTapped",
                xaml.Contains("x:Name=\"LayoutRoot\"", StringComparison.Ordinal)
                    && xaml.Contains("Background=\"Transparent\"", StringComparison.Ordinal)
                    && xaml.Contains("RightTapped=\"OnPageRightTapped\"", StringComparison.Ordinal));
            ChecksContext.Check(
                "右键刷新: " + xamlName + " 已删除 " + removedId,
                !xaml.Contains(removedId, StringComparison.Ordinal)
                    && !code.Contains(removedId, StringComparison.Ordinal));
            ChecksContext.Check(
                "右键刷新: " + codeName + " 复用原刷新流程",
                code.Contains("OnPageRightTapped(object sender, RightTappedRoutedEventArgs e)", StringComparison.Ordinal)
                    && code.Contains("PageRefreshGesture.ShouldRefresh(e.OriginalSource, LayoutRoot)", StringComparison.Ordinal)
                    && code.Contains("e.Handled = true", StringComparison.Ordinal)
                    && code.Contains("Refresh()", StringComparison.Ordinal));
        }

        CheckHomeNewsRefresh(root);

        ChecksContext.Check(
            "右键刷新: 保留 Mod 库行菜单",
            File.ReadAllText(Path.Combine(root, "src", "PdxLauncher.App", "Views", "ModLibraryPage.xaml"))
                .Contains("menuModLibraryPageRefresh", StringComparison.Ordinal));
        ChecksContext.Check(
            "右键刷新: 保留创意工坊行菜单与失败重试",
            File.ReadAllText(Path.Combine(root, "src", "PdxLauncher.App", "Views", "WorkshopPage.xaml"))
                .Contains("menuWorkshopPageRefresh", StringComparison.Ordinal)
                && File.ReadAllText(Path.Combine(root, "src", "PdxLauncher.App", "Views", "WorkshopPage.xaml"))
                    .Contains("btnWorkshopRetry", StringComparison.Ordinal));

        var helperPath = Path.Combine(root, "src", "PdxLauncher.App", "Services", "PageRefreshGesture.cs");
        if (ChecksContext.Check("右键刷新: PageRefreshGesture.cs 存在", File.Exists(helperPath), helperPath))
        {
            var helper = File.ReadAllText(helperPath);
            foreach (var marker in new[] { "TextBox", "AutoSuggestBox", "PasswordBox", "RichEditBox", "ContextFlyout", "ReferenceEquals(current, root)" })
            {
                ChecksContext.Check("右键刷新: 手势助手包含 " + marker, helper.Contains(marker, StringComparison.Ordinal));
            }
        }

        return ChecksContext.Failures - before;
    }

    private static void CheckHomeNewsRefresh(string root)
    {
        var xamlPath = Path.Combine(root, "src", "PdxLauncher.App", "Views", HomeXaml + ".xaml");
        var codePath = Path.Combine(root, "src", "PdxLauncher.App", "Views", HomeCode + ".xaml.cs");
        var vmPath = Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "HomeViewModel.cs");
        if (!ChecksContext.Check("主页公告刷新: XAML 存在", File.Exists(xamlPath), xamlPath)
            || !ChecksContext.Check("主页公告刷新: code-behind 存在", File.Exists(codePath), codePath)
            || !ChecksContext.Check("主页公告刷新: ViewModel 存在", File.Exists(vmPath), vmPath))
        {
            return;
        }

        var xaml = File.ReadAllText(xamlPath);
        var code = File.ReadAllText(codePath);
        var vm = File.ReadAllText(vmPath);
        ChecksContext.Check(
            "主页公告刷新: 内容区接管 RightTapped",
            xaml.Contains("x:Name=\"HomeScrollViewer\"", StringComparison.Ordinal)
                && xaml.Contains("Background=\"Transparent\"", StringComparison.Ordinal)
                && xaml.Contains("RightTapped=\"OnPageRightTapped\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "主页公告刷新: 复用 PageRefreshGesture",
            code.Contains("OnPageRightTapped(object sender, RightTappedRoutedEventArgs e)", StringComparison.Ordinal)
                && code.Contains("PageRefreshGesture.ShouldRefresh(e.OriginalSource, HomeScrollViewer)", StringComparison.Ordinal)
                && code.Contains("e.Handled = true", StringComparison.Ordinal));
        var handlerStart = code.IndexOf("OnPageRightTapped(object sender, RightTappedRoutedEventArgs e)", StringComparison.Ordinal);
        var handler = handlerStart >= 0
            ? code[handlerStart..Math.Min(code.Length, handlerStart + 800)]
            : string.Empty;
        ChecksContext.Check(
            "主页公告刷新: 只调用 RefreshNewsAsync",
            code.Contains("_viewModel.RefreshNewsAsync(_lifetime.Token)", StringComparison.Ordinal)
                && vm.Contains("internal async Task RefreshNewsAsync(CancellationToken cancellationToken)", StringComparison.Ordinal)
                && !handler.Contains("ReloadAsync()", StringComparison.Ordinal));
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

        throw new DirectoryNotFoundException("找不到仓库根 PdxLauncher.sln");
    }
}
