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

    /// <summary>
    /// 破坏性调用点（删除 / 覆盖 / 清空 / 改名）：必须显式传
    /// <c>InitialFocus.Cancel</c>，否则 Enter 会直接执行。
    /// 锚点取调用参数里唯一出现的资源键，调用起点从锚点向前找方法名。
    /// </summary>
    private static readonly (string RelativePath, string Anchor, string Method, bool Destructive)[] FocusCallSites =
    {
        (Path.Combine("Views", "SettingsPage.xaml.cs"), "SettingsTranslateLocalModelDialogRemoveTitle", "ConfirmAsync", true),
        (Path.Combine("Views", "SettingsPage.xaml.cs"), "SettingsAppDataDialogClearTitle", "ConfirmAsync", true),
        (Path.Combine("Views", "SettingsPage.xaml.cs"), "SettingsCacheDialogClearAllTitle", "ConfirmAsync", true),
        (Path.Combine("Views", "SettingsPage.xaml.cs"), "SettingsPatchApplyDialogTitle", "ConfirmAsync", true),
        (Path.Combine("Views", "SettingsPage.xaml.cs"), "SettingsPatchRemoveDialogTitle", "ConfirmAsync", true),
        (Path.Combine("Views", "SettingsPage.xaml.cs"), "SettingsDangerDialogTitle", "ConfirmAsync", true),
        (Path.Combine("Views", "ModsPage.xaml.cs"), "ModsToolbarDeleteName", "ConfirmAsync", true),
        (Path.Combine("Views", "DiagnosticsPage.xaml.cs"), "SettingsCacheDialogClearAllTitle", "ConfirmAsync", true),
        (Path.Combine("Views", "ModPathRepairDialog.cs"), "ModsRepairDialogTitle", "ShowAsync", true),

        // 反例：新建/改名播放集的输入框不是破坏性操作，必须保持默认确认焦点。
        (Path.Combine("Views", "ModsPage.xaml.cs"), "Localizer.Instance.CommonOk", "ConfirmAsync", false),
    };

    /// <summary>整份文件都不该出现取消焦点的非破坏性调用点。</summary>
    private static readonly string[] NonDestructiveFiles =
    {
        Path.Combine("Views", "WorkshopDetailPage.xaml.cs"),
        Path.Combine("Views", "AccountPage.xaml.cs"),
        Path.Combine("Services", "UpdateFlow.cs"),
        Path.Combine("Views", "ModStudioPage.xaml.cs"),
        Path.Combine("Views", "AutoSortPreviewDialog.cs"),
        Path.Combine("Views", "EditModDescriptorDialog.cs"),
        Path.Combine("Views", "BindWorkshopItemDialog.cs"),
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
        CheckInitialFocus(appDir);
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

        // 键盘语义：Esc 取消、Enter 随初始焦点。Escape 必须实际走到「取消」，
        // 只出现 VirtualKey.Escape 字样不算数（防止分支被改成确认或空处理）。
        var code = File.Exists(codePath) ? File.ReadAllText(codePath) : string.Empty;
        var escapeIndex = code.IndexOf("VirtualKey.Escape", StringComparison.Ordinal);
        var escapeBranch = escapeIndex >= 0
            ? code.Substring(escapeIndex, Math.Min(320, code.Length - escapeIndex))
            : string.Empty;
        ChecksContext.Check(
            "浮层 Escape 分支按取消完成",
            escapeIndex >= 0 && escapeBranch.Contains("Complete(confirmed: false)", StringComparison.Ordinal),
            escapeBranch.ReplaceLineEndings(" "));
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

    /// <summary>
    /// 初始焦点自检：破坏性调用点必须传 <c>InitialFocus.Cancel</c>，非破坏性调用点
    /// 保持默认的确认焦点；浮层的默认参数与 Enter 回退逻辑也不许回退。
    ///
    /// Checks 工程不引用 WinUI 应用程序集，因此这里读源码文本做静态断言。
    /// </summary>
    private static void CheckInitialFocus(string appDir)
    {
        var overlayPath = Path.Combine(appDir, "Controls", "ConfirmOverlay.xaml.cs");
        if (!ChecksContext.Check("ConfirmOverlay.xaml.cs 存在（初始焦点）", File.Exists(overlayPath), overlayPath))
        {
            return;
        }

        var code = File.ReadAllText(overlayPath);
        var defaultCount = CountOccurrences(code, "InitialFocus initialFocus = InitialFocus.Confirm");

        ChecksContext.Check(
            "ConfirmOverlay 默认初始焦点仍是 Confirm（static 与实例两个签名）",
            defaultCount >= 2,
            "出现 " + defaultCount + " 次");
        ChecksContext.Check(
            "ShowAsync 记录 InitialFocus",
            code.Contains("_initialFocus = initialFocus;", StringComparison.Ordinal),
            overlayPath);
        ChecksContext.Check(
            "初始焦点为取消时聚焦取消键",
            code.Contains("_initialFocus == InitialFocus.Cancel", StringComparison.Ordinal)
                && code.Contains("CancelButton.Focus(FocusState.Programmatic)", StringComparison.Ordinal),
            overlayPath);
        ChecksContext.Check(
            "Enter 回退按初始焦点决定确认 / 取消",
            code.Contains("!cancelAvailable || _initialFocus == InitialFocus.Confirm", StringComparison.Ordinal),
            overlayPath);
        ChecksContext.Check(
            "静态入口把初始焦点转发给宿主",
            code.Contains("initialFocus);", StringComparison.Ordinal),
            overlayPath);
        ChecksContext.Check(
            "只有文本输入控件才抢占初始焦点",
            code.Contains("IsTextInput(focusable)", StringComparison.Ordinal),
            overlayPath);

        // 页面自带的 ConfirmAsync 包装必须先转发 initialFocus，否则调用点传了 Cancel 也到不了浮层。
        foreach (var helper in new[]
                 {
                     Path.Combine("Views", "SettingsPage.xaml.cs"),
                     Path.Combine("Views", "DiagnosticsPage.xaml.cs"),
                 })
        {
            var path = Path.Combine(appDir, helper);
            var forwarded = File.Exists(path)
                && File.ReadAllText(path).Contains("initialFocus: initialFocus", StringComparison.Ordinal);
            ChecksContext.Check(helper + " 的确认框包装转发初始焦点", forwarded, path);
        }

        foreach (var site in FocusCallSites)
        {
            var path = Path.Combine(appDir, site.RelativePath);
            var label = site.RelativePath + " 调用点 " + site.Anchor + (site.Destructive ? " 传了取消焦点" : " 保持默认确认焦点");

            if (!File.Exists(path))
            {
                ChecksContext.Check(label, false, "文件不存在：" + path);
                continue;
            }

            var text = File.ReadAllText(path);
            var anchor = text.IndexOf(site.Anchor, StringComparison.Ordinal);
            if (anchor < 0)
            {
                ChecksContext.Check(label, false, "找不到锚点 " + site.Anchor);
                continue;
            }

            var invocation = ExtractInvocation(text, anchor, site.Method);
            var hasCancel = invocation is not null
                && invocation.Contains("InitialFocus.Cancel", StringComparison.Ordinal);
            var ok = site.Destructive ? hasCancel : !hasCancel;

            ChecksContext.Check(
                label,
                ok,
                invocation is null
                    ? "找不到 " + site.Method + " 调用（锚点 " + site.Anchor + "）"
                    : invocation.ReplaceLineEndings(" "));
        }

        foreach (var relative in NonDestructiveFiles)
        {
            var path = Path.Combine(appDir, relative);
            if (!ChecksContext.Check("非破坏性调用点文件存在 " + relative, File.Exists(path), path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            ChecksContext.Check(
                relative + " 的非破坏性调用点保持默认确认焦点",
                !text.Contains("InitialFocus.Cancel", StringComparison.Ordinal),
                path);
        }
    }

    /// <summary>从锚点向前找方法起点、向后找第一个 <c>);</c>，截出一次调用。</summary>
    private static string? ExtractInvocation(string text, int anchor, string method)
    {
        var token = method + "(";
        var start = text.LastIndexOf(token, anchor, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var end = text.IndexOf(");", anchor, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        return text.Substring(start, end + 2 - start);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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
