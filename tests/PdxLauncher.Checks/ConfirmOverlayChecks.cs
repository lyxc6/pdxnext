namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 「确认 / 取消按钮换位」自检。
///
/// 这条需求最终落在自绘浮层 <c>ConfirmOverlay</c> 上，而不是 WinUI 的 ContentDialog：
/// ContentDialog 的模板把 PrimaryButton 固定在命令区第 0 列、CloseButton 固定在第 4 列，
/// 视觉顺序永远是「确认在左、取消在右」，框架没有公开的交换能力。
///
/// 这里钉死三件事：
/// <list type="number">
///   <item>浮层控件本身在：XAML 里「取消键在确认键左边」，确认键是 Accent 主色。</item>
///   <item>应用里不再有 <c>new ContentDialog</c>（残留一处就说明还有框没换位）。</item>
///   <item>窗口挂了浮层宿主，且 publish 会带上 Controls 下的 .xbf（否则发布版浮层是空壳）。</item>
/// </list>
/// </summary>
public static class ConfirmOverlayChecks
{
    /// <summary>应用里不允许再出现 ContentDialog 的调用点（注释里的字样不算）。</summary>
    private static readonly string[] AppSourceFiles =
    {
        Path.Combine("Views", "WorkshopDetailPage.xaml.cs"),
        Path.Combine("Views", "SettingsPage.xaml.cs"),
        Path.Combine("Views", "ModsPage.xaml.cs"),
        Path.Combine("Views", "AccountPage.xaml.cs"),
        Path.Combine("Views", "DlcPage.xaml.cs"),
        Path.Combine("Views", "ModPathRepairDialog.cs"),
        Path.Combine("Services", "CrashReporter.cs"),
    };

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var appDir = Path.Combine(root, "src", "PdxLauncher.App");

        CheckOverlayControl(appDir);
        CheckCallSitesMigrated(appDir);
        CheckWindowHost(root, appDir);
        CheckPublishArtifacts(appDir);

        return ChecksContext.Failures - before;
    }

    /// <summary>浮层控件：文件存在、取消键在确认键左边、确认键是 Accent 主色。</summary>
    private static void CheckOverlayControl(string appDir)
    {
        var xamlPath = Path.Combine(appDir, "Controls", "ConfirmOverlay.xaml");
        var codePath = Path.Combine(appDir, "Controls", "ConfirmOverlay.xaml.cs");

        if (!ChecksContext.Check("ConfirmOverlay.xaml 存在", File.Exists(xamlPath), xamlPath))
        {
            return;
        }

        ChecksContext.Check("ConfirmOverlay.xaml.cs 存在", File.Exists(codePath), codePath);

        var xaml = File.ReadAllText(xamlPath);

        var cancelIndex = xaml.IndexOf("x:Name=\"CancelButton\"", StringComparison.Ordinal);
        var confirmIndex = xaml.IndexOf("x:Name=\"ConfirmButton\"", StringComparison.Ordinal);

        ChecksContext.Check(
            "ConfirmOverlay 里能找到取消键",
            cancelIndex >= 0,
            $"CancelButton 索引 {cancelIndex}");
        ChecksContext.Check(
            "ConfirmOverlay 里能找到确认键",
            confirmIndex >= 0,
            $"ConfirmButton 索引 {confirmIndex}");

        if (cancelIndex >= 0 && confirmIndex >= 0)
        {
            ChecksContext.Check(
                "取消键排在确认键左边",
                cancelIndex < confirmIndex,
                $"Cancel={cancelIndex} / Confirm={confirmIndex}");
        }

        // 确认键必须是 Accent 主色：这是「右侧确认键高亮」这条决策的直接体现。
        var confirmButton = confirmIndex >= 0
            ? ExtractElement(xaml, confirmIndex)
            : string.Empty;
        ChecksContext.Check(
            "确认键使用 AccentButtonStyle",
            confirmButton.Contains("AccentButtonStyle", StringComparison.Ordinal),
            confirmButton);

        // 键盘语义：Esc 取消、Enter 确认。
        var code = File.Exists(codePath) ? File.ReadAllText(codePath) : string.Empty;
        ChecksContext.Check(
            "浮层处理 Escape",
            code.Contains("VirtualKey.Escape", StringComparison.Ordinal));
        ChecksContext.Check(
            "浮层处理 Enter",
            code.Contains("VirtualKey.Enter", StringComparison.Ordinal));
    }

    /// <summary>所有原 ContentDialog 调用点都已经换成 ConfirmOverlay。</summary>
    private static void CheckCallSitesMigrated(string appDir)
    {
        foreach (var relative in AppSourceFiles)
        {
            var path = Path.Combine(appDir, relative);
            if (!ChecksContext.Check("源文件存在 " + relative, File.Exists(path), path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            ChecksContext.Check(
                relative + " 不再 new ContentDialog",
                !text.Contains("new ContentDialog", StringComparison.Ordinal),
                path);
        }
    }

    /// <summary>窗口挂了浮层宿主并登记成全局 Host。</summary>
    private static void CheckWindowHost(string root, string appDir)
    {
        var xamlPath = Path.Combine(appDir, "MainWindow.xaml");
        var codePath = Path.Combine(appDir, "MainWindow.xaml.cs");

        if (ChecksContext.Check("MainWindow.xaml 存在", File.Exists(xamlPath), xamlPath))
        {
            var xaml = File.ReadAllText(xamlPath);
            ChecksContext.Check(
                "MainWindow 挂了 ConfirmOverlay 宿主",
                xaml.Contains("ConfirmOverlay", StringComparison.Ordinal),
                xamlPath);
        }

        if (ChecksContext.Check("MainWindow.xaml.cs 存在", File.Exists(codePath), codePath))
        {
            var code = File.ReadAllText(codePath);
            ChecksContext.Check(
                "MainWindow 登记了 ConfirmOverlay.Host",
                code.Contains("ConfirmOverlay.Host", StringComparison.Ordinal),
                codePath);
        }

        _ = root;
    }

    /// <summary>publish 必须带上 Controls 下的 .xbf，否则发布版浮层没有模板。</summary>
    private static void CheckPublishArtifacts(string appDir)
    {
        var csprojPath = Path.Combine(appDir, "PdxLauncher.App.csproj");
        if (!ChecksContext.Check("PdxLauncher.App.csproj 存在", File.Exists(csprojPath), csprojPath))
        {
            return;
        }

        var csproj = File.ReadAllText(csprojPath);
        // publish 目标里要有一项收集 Controls 下的 .xbf（转义写法各版本不一，这里按关键字判定）。
        var hasControlXbf = csproj.Contains("_WinUIPublishControlXaml", StringComparison.Ordinal)
            && csproj.Contains("Controls", StringComparison.Ordinal)
            && csproj.Contains(".xbf", StringComparison.Ordinal);
        ChecksContext.Check(
            "publish 目标带上 Controls 下的 *.xbf",
            hasControlXbf,
            csprojPath);
    }

    /// <summary>从元素起始位置往后截一段，用来断言该元素自己的属性。</summary>
    private static string ExtractElement(string xaml, int elementIndex)
    {
        if (elementIndex < 0 || elementIndex >= xaml.Length)
        {
            return string.Empty;
        }

        var end = xaml.IndexOf('>', elementIndex);
        if (end < 0)
        {
            end = Math.Min(xaml.Length, elementIndex + 400);
        }

        return xaml.Substring(elementIndex, end - elementIndex);
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
