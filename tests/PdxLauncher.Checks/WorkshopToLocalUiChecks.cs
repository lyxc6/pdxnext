namespace PdxLauncher.Checks;

using System.Text.RegularExpressions;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 「工坊 mod 转本地 mod」的 App/UI/文案接线静态断言（由 Program.cs 反射发现）。
/// <para>
/// 覆盖两处右键菜单入口（Mod 库页 + 播放集行模板）、行 VM 门控、两页 code-behind 的
/// 确认 / 反馈流程，以及 zh-CN / en-US 四键的键集与占位符一致性。
/// 全部读源码，不跑构建；编译期兜底由 LocalizationGenerator 负责。
/// </para>
/// </summary>
public static class WorkshopToLocalUiChecks
{
    private static readonly string[] NewKeys =
    [
        "CommonConvertToLocal",
        "ConvertToLocalConfirmDetail",
        "ConvertToLocalStatusDone",
        "ConvertToLocalFailedDetail",
    ];

    private static readonly string[] ReusedKeys =
    [
        "CommonCancel",
        "CommonOk",
        "CommonClose",
        "CommonFailed",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var root = I18nResw.RepoRoot();

        var modsPageXaml = Read(root, "src", "PdxLauncher.App", "Views", "ModsPage.xaml");
        var modsPageCode = Read(root, "src", "PdxLauncher.App", "Views", "ModsPage.xaml.cs");
        var modsVm = Read(root, "src", "PdxLauncher.App", "ViewModels", "ModsViewModel.cs");
        var modRowVm = Read(root, "src", "PdxLauncher.App", "ViewModels", "ModRowViewModel.cs");

        var libPageXaml = Read(root, "src", "PdxLauncher.App", "Views", "ModLibraryPage.xaml");
        var libPageCode = Read(root, "src", "PdxLauncher.App", "Views", "ModLibraryPage.xaml.cs");
        var libVm = Read(root, "src", "PdxLauncher.App", "ViewModels", "ModLibraryViewModel.cs");
        var libRowVm = Read(root, "src", "PdxLauncher.App", "ViewModels", "ModLibraryRowViewModel.cs");

        var invoker = Read(root, "src", "PdxLauncher.App", "Services", "WorkshopToLocalInvoker.cs");
        var appServices = Read(root, "src", "PdxLauncher.App", "Services", "AppServices.cs");
        var app = Read(root, "src", "PdxLauncher.App", "App.xaml.cs");

        CheckModLibraryMenu(libPageXaml);
        CheckModsMenu(modsPageXaml);
        CheckRowViewModels(modRowVm, libRowVm);
        CheckCodeBehind(modsPageCode, libPageCode);
        CheckPageViewModels(modsVm, libVm, invoker);
        CheckComposition(appServices, app);
        CheckBilingualKeys(root);

        return ChecksContext.Failures - before;
    }

    /// <summary>(a) Mod 库页行菜单：六项行操作在「刷新页面」之前，转本地项夹在刷新与翻译之间，四项属性齐全且 Opening 仍在。</summary>
    private static void CheckModLibraryMenu(string xaml)
    {
        var start = xaml.IndexOf("menuModLibraryPageRefresh", StringComparison.Ordinal);
        var end = xaml.IndexOf("menuModLibraryTranslationRetry", StringComparison.Ordinal);
        var rowActionsStart = start >= 0
            ? xaml.LastIndexOf("<MenuFlyout Opening=\"OnTranslationMenuOpening\">", start, StringComparison.Ordinal)
            : -1;
        var rowActionsEnd = rowActionsStart >= 0
            ? xaml.IndexOf("<MenuFlyoutSeparator />", rowActionsStart, StringComparison.Ordinal)
            : -1;
        ChecksContext.Check(
            "工坊转本地: Mod 库页行菜单存在（刷新项在翻译项之前）",
            start >= 0 && end > start);
        var menu = start >= 0 && end > start ? xaml[start..end] : string.Empty;
        var rowActions = rowActionsStart >= 0 && rowActionsEnd > rowActionsStart
            ? xaml[rowActionsStart..rowActionsEnd]
            : string.Empty;

        var actions = new[]
        {
            ("menuModLibraryOpenDetail", "WorkshopDetailText", "HasWorkshopDetail", "OnOpenDetailClick"),
            ("menuModLibraryOpenFolder", "L.ModlibOpenFolder", "HasLocalContent", "OnOpenFolderClick"),
            ("menuModLibraryOpenSteam", "L.ModlibOpenSteam", "HasWorkshopPage", "OnOpenSteamClick"),
            ("menuModLibraryOpenBrowser", "L.ModlibOpenBrowser", "HasWorkshopPage", "OnOpenBrowserClick"),
            ("menuModLibrarySteamDownload", "L.ModlibSteamDownloadName", "CanTriggerSteamDownload", "OnSteamDownloadClick"),
            ("menuModLibrarySteamDisable", "SteamDisableActionText", "HasWorkshopPage", "OnSteamDisableClick"),
        };
        foreach (var (automationId, text, enabled, click) in actions)
        {
            ChecksContext.Check(
                "Mod 库右键: " + automationId + " 文案 / 门控 / Click / AutomationId 齐全",
                rowActions.Contains("AutomationProperties.AutomationId=\"" + automationId + "\"", StringComparison.Ordinal)
                    && rowActions.Contains("{x:Bind " + text + ", Mode=OneWay}", StringComparison.Ordinal)
                    && rowActions.Contains("{x:Bind " + enabled + ", Mode=OneTime}", StringComparison.Ordinal)
                    && rowActions.Contains("Click=\"" + click + "\"", StringComparison.Ordinal),
                "ModLibraryPage.xaml");
        }

        ChecksContext.Equal(
            "Mod 库右键: 行操作含快速添加与原有六项",
            7,
            CountOccurrences(rowActions, "<MenuFlyoutItem"));
        ChecksContext.Check(
            "Mod 库右键: 快速添加位于原有行操作之前",
            rowActions.IndexOf("menuModLibraryAddToActivePlaySet", StringComparison.Ordinal) >= 0
                && rowActions.IndexOf("menuModLibraryAddToActivePlaySet", StringComparison.Ordinal)
                    < rowActions.IndexOf("menuModLibraryOpenDetail", StringComparison.Ordinal));
        ChecksContext.Check(
            "Mod 库右键: 原有六项行操作按截图顺序排列",
            actions
                .Select(action => rowActions.IndexOf(action.Item1, StringComparison.Ordinal))
                .Aggregate(
                    -1,
                    (previous, current) => current > previous ? current : -1) >= 0);
        ChecksContext.Check(
            "Mod 库右键: 六项行操作后有分隔符与页面级动作隔开",
            rowActionsEnd > rowActionsStart
                && xaml.IndexOf("menuModLibraryPageRefresh", rowActionsEnd, StringComparison.Ordinal) > rowActionsEnd);

        ChecksContext.Check(
            "工坊转本地: Mod 库页菜单含 CommonConvertToLocal 文案绑定",
            menu.Contains("{x:Bind L.CommonConvertToLocal, Mode=OneWay}", StringComparison.Ordinal),
            "ModLibraryPage.xaml");
        ChecksContext.Check(
            "工坊转本地: Mod 库页菜单 Click=OnConvertToLocalClick",
            menu.Contains("Click=\"OnConvertToLocalClick\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊转本地: Mod 库页菜单 AutomationId=menuModLibraryConvertToLocal",
            menu.Contains("AutomationProperties.AutomationId=\"menuModLibraryConvertToLocal\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊转本地: Mod 库页菜单 IsEnabled 绑 CanConvertToLocal",
            menu.Contains("{x:Bind CanConvertToLocal, Mode=OneWay}", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊转本地: Mod 库页菜单保留 Opening=OnTranslationMenuOpening",
            xaml.Contains("<MenuFlyout Opening=\"OnTranslationMenuOpening\">", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊转本地: Mod 库页新项后补分隔符做行级 / 页面级分组",
            CountOccurrences(menu, "<MenuFlyoutSeparator />") >= 1);
    }

    /// <summary>(d) 播放集页：PlaysetModRowTemplate 含新项，候选行模板本次不改、仍不含本项。</summary>
    private static void CheckModsMenu(string xaml)
    {
        var playset = SliceTemplate(xaml, "PlaysetModRowTemplate");
        var candidate = SliceTemplate(xaml, "PlaysetCandidateRowTemplate");

        ChecksContext.Check(
            "工坊转本地: 播放集行模板含文案 / 门控 / Click / AutomationId 四项",
            playset.Contains("{x:Bind L.CommonConvertToLocal, Mode=OneWay}", StringComparison.Ordinal)
                && playset.Contains("Click=\"OnConvertToLocalClick\"", StringComparison.Ordinal)
                && playset.Contains("{x:Bind CanConvertToLocal, Mode=OneWay}", StringComparison.Ordinal)
                && playset.Contains("{x:Bind ConvertToLocalMenuAutomationId, Mode=OneTime}", StringComparison.Ordinal),
            "ModsPage.xaml");
        ChecksContext.Check(
            "工坊转本地: 新项位于「复制注册 ID」之后、分隔符之前",
            IndexOf(playset, "CopyIdMenuAutomationId") >= 0
                && IndexOf(playset, "ConvertToLocalMenuAutomationId") > IndexOf(playset, "CopyIdMenuAutomationId"));
        ChecksContext.Check(
            "工坊转本地: 候选行模板本次不改，不含转本地项",
            candidate.Length > 0
                && !candidate.Contains("CommonConvertToLocal", StringComparison.Ordinal)
                && !candidate.Contains("OnConvertToLocalClick", StringComparison.Ordinal));
    }

    /// <summary>(b/e) 两个行 VM 的门控依据：Mod 库按订阅 + 本地内容，播放集按非本地 + 本地文件。</summary>
    private static void CheckRowViewModels(string modRowVm, string libRowVm)
    {
        var gating = SliceMember(libRowVm, "public bool CanConvertToLocal");
        ChecksContext.Check(
            "工坊转本地: Mod 库行 VM 有 CanConvertToLocal 且引用 IsSubscribedOrigin 与 HasLocalContent",
            gating.Contains("IsSubscribedOrigin", StringComparison.Ordinal)
                && gating.Contains("HasLocalContent", StringComparison.Ordinal),
            "ModLibraryRowViewModel.cs");

        var rowGating = SliceMember(modRowVm, "public bool CanConvertToLocal");
        ChecksContext.Check(
            "工坊转本地: 播放集行 VM 有 CanConvertToLocal 且引用 IsLocalOrigin 与 HasLocalFiles",
            rowGating.Contains("IsLocalOrigin", StringComparison.Ordinal)
                && rowGating.Contains("HasLocalFiles", StringComparison.Ordinal),
            "ModRowViewModel.cs");
        ChecksContext.Check(
            "工坊转本地: 播放集行 VM 有 modConvertToLocal_ 前缀的 AutomationId",
            modRowVm.Contains("\"modConvertToLocal_\"", StringComparison.Ordinal)
                && modRowVm.Contains("ConvertToLocalMenuAutomationId", StringComparison.Ordinal));
    }

    /// <summary>(c) 两页 code-behind：确认框（默认焦点取消）、VM 异步转换、失败 ShowMessage、状态栏反馈齐全。</summary>
    private static void CheckCodeBehind(string modsPageCode, string libPageCode)
    {
        foreach (var (name, code, readKey) in new[]
        {
            ("ModsPage", modsPageCode, "ReportNoLocalFiles"),
            ("ModLibraryPage", libPageCode, "SetStatus"),
        })
        {
            var handler = SliceMember(code, "private async void OnConvertToLocalClick");
            ChecksContext.Check("工坊转本地: " + name + " 有 OnConvertToLocalClick（async void + try/catch）",
                handler.Contains("try", StringComparison.Ordinal)
                    && handler.Contains("catch", StringComparison.Ordinal));
            ChecksContext.Check("工坊转本地: " + name + " 二次校验 CanConvertToLocal",
                handler.Contains("CanConvertToLocal", StringComparison.Ordinal));
            ChecksContext.Check("工坊转本地: " + name + " 用 ConfirmOverlay.ConfirmAsync 且默认焦点在取消",
                handler.Contains("ConfirmOverlay.ConfirmAsync", StringComparison.Ordinal)
                    && handler.Contains("InitialFocus.Cancel", StringComparison.Ordinal));
            ChecksContext.Check("工坊转本地: " + name + " 确认文案用 CommonConvertToLocal / ConvertToLocalConfirmDetail",
                handler.Contains("L.CommonConvertToLocal", StringComparison.Ordinal)
                    && handler.Contains("Keys.ConvertToLocalConfirmDetail", StringComparison.Ordinal));
            ChecksContext.Check("工坊转本地: " + name + " 取消即静默返回",
                handler.Contains("if (!confirmed)", StringComparison.Ordinal));
            ChecksContext.Check("工坊转本地: " + name + " 调用 VM 异步转换方法",
                handler.Contains("_viewModel.ConvertToLocalAsync", StringComparison.Ordinal));
            ChecksContext.Check("工坊转本地: " + name + " 失败记录日志并弹 ShowMessage",
                handler.Contains("AppServices.Logger.Error", StringComparison.Ordinal)
                    && code.Contains("ConfirmOverlay.ShowMessage", StringComparison.Ordinal)
                    && handler.Contains("Keys.ConvertToLocalFailedDetail", StringComparison.Ordinal));
            ChecksContext.Check("工坊转本地: " + name + " 状态栏反馈（" + readKey + "）",
                handler.Contains(readKey, StringComparison.Ordinal));
        }

        // Opening 处理器一律 default:continue，不再回写 IsEnabled 切断 x:Bind。
        foreach (var (name, code) in new[]
        {
            ("ModsPage", modsPageCode),
            ("ModLibraryPage", libPageCode),
        })
        {
            var opening = SliceMember(code, "private void OnTranslationMenuOpening");
            ChecksContext.Check("工坊转本地: " + name + " 菜单 Opening 用 switch + default:continue 保留 x:Bind",
                opening.Contains("switch", StringComparison.Ordinal)
                    && opening.Contains("default:", StringComparison.Ordinal)
                    && opening.Contains("continue;", StringComparison.Ordinal)
                    && !opening.Contains("_ => menuItem.IsEnabled", StringComparison.Ordinal));
        }
    }

    /// <summary>两页 VM 暴露异步转换方法与忙碌状态，并统一经 Invoker 调 Core 固定 API。</summary>
    private static void CheckPageViewModels(string modsVm, string libVm, string invoker)
    {
        ChecksContext.Check(
            "工坊转本地: 播放集页 VM 有异步转换方法与忙碌态",
            modsVm.Contains("public async Task ConvertToLocalAsync", StringComparison.Ordinal)
                && modsVm.Contains("IsConvertToLocalBusy", StringComparison.Ordinal)
                && modsVm.Contains("ReportConvertedToLocal", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊转本地: Mod 库页 VM 有异步转换方法、忙碌态与成功状态栏",
            libVm.Contains("public async Task ConvertToLocalAsync", StringComparison.Ordinal)
                && libVm.Contains("IsConvertToLocalBusy", StringComparison.Ordinal)
                && libVm.Contains("Keys.ConvertToLocalStatusDone", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊转本地: Invoker 调 Core 固定 API（ConvertToLocalAsync + 新 ModId 结果类型）",
            invoker.Contains("AppServices.WorkshopToLocal.ConvertToLocalAsync", StringComparison.Ordinal)
                && invoker.Contains("WorkshopToLocalResult", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊转本地: Invoker 的目录名规则委托给 Core（不再自己折 ASCII）",
            invoker.Contains("LocalModFolderName.Normalize", StringComparison.Ordinal)
                && invoker.Contains("LocalModFolderName.NormalizeAscii", StringComparison.Ordinal)
                && invoker.Contains("GamePathRules.RequiresAsciiPaths", StringComparison.Ordinal)
                && !invoker.Contains("IsAsciiLetterOrDigit", StringComparison.Ordinal));
    }

    /// <summary>DI 注册与启动清理。</summary>
    private static void CheckComposition(string appServices, string app)
    {
        ChecksContext.Check(
            "工坊转本地: AppServices 暴露 WorkshopToLocal 并登记实例",
            appServices.Contains("WorkshopToLocalConverter WorkshopToLocal", StringComparison.Ordinal)
                && appServices.Contains("WorkshopToLocal = workshopToLocal", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊转本地: App.xaml.cs 装配 Packager、Converter 并清理过期暂存",
            app.Contains("new ModArchivePackager()", StringComparison.Ordinal)
                && app.Contains("new WorkshopToLocalConverter(", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊转本地: 启动 / 游戏发现时调 CleanupAbandonedStaging",
            appServices.Contains("CleanupAbandonedStaging(game)", StringComparison.Ordinal));
    }

    /// <summary>双语四键存在、非空、占位符一致；复用键两语都在。</summary>
    private static void CheckBilingualKeys(string root)
    {
        var zh = I18nResw.Read(root, I18nResw.SimplifiedChinese);
        var en = I18nResw.Read(root, I18nResw.English);

        foreach (var key in NewKeys)
        {
            var zhValue = I18nResw.Get(zh, key);
            var enValue = I18nResw.Get(en, key);
            ChecksContext.Check(
                "工坊转本地: 双语键 " + key + " 存在且非空",
                !string.IsNullOrWhiteSpace(zhValue) && !string.IsNullOrWhiteSpace(enValue),
                "Resources.resw");
            if (zhValue is null || enValue is null)
            {
                continue;
            }

            ChecksContext.Check(
                "工坊转本地: 双语键 " + key + " 占位符一致",
                Placeholders(zhValue).SequenceEqual(Placeholders(enValue), StringComparer.Ordinal));
            var expected = key == "CommonConvertToLocal" ? Array.Empty<string>() : new[] { "{0}" };
            ChecksContext.Check(
                "工坊转本地: 键 " + key + " 占位符符合设计（" + (key == "CommonConvertToLocal" ? "0" : "1 个 {0}") + "）",
                Placeholders(zhValue).SequenceEqual(expected, StringComparer.Ordinal));
        }

        foreach (var key in ReusedKeys)
        {
            ChecksContext.Check(
                "工坊转本地: 复用键 " + key + " 两语均存在",
                !string.IsNullOrWhiteSpace(I18nResw.Get(zh, key))
                    && !string.IsNullOrWhiteSpace(I18nResw.Get(en, key)));
        }
    }

    private static IReadOnlyList<string> Placeholders(string value) => Regex
        .Matches(value, @"\{(\d+)\}", RegexOptions.CultureInvariant)
        .Cast<Match>()
        .Select(static match => match.Value)
        .OrderBy(static token => token, StringComparer.Ordinal)
        .ToArray();

    private static string Read(string root, params string[] parts) =>
        File.Exists(Path.Combine(new[] { root }.Concat(parts).ToArray()))
            ? File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()))
            : string.Empty;

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static int IndexOf(string text, string needle) => text.IndexOf(needle, StringComparison.Ordinal);

    /// <summary>取 XAML 里某个 DataTemplate 的文本切片。</summary>
    private static string SliceTemplate(string xaml, string key)
    {
        var marker = "x:Key=\"" + key + "\"";
        var start = xaml.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var end = xaml.IndexOf("</DataTemplate>", start, StringComparison.Ordinal);
        return end > start ? xaml[start..end] : string.Empty;
    }

    /// <summary>从某个成员声明起截到下一个「    /// &lt;summary&gt;」注释或 1200 字符，够覆盖方法体 / 属性表达式。</summary>
    private static string SliceMember(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var nextDoc = source.IndexOf("    /// <summary>", start + signature.Length, StringComparison.Ordinal);
        var end = nextDoc > start ? nextDoc : Math.Min(source.Length, start + 3000);
        return source[start..end];
    }
}
