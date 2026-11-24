namespace PdxLauncher.Checks;

using System.Text.Json;

using PdxLauncher.Achievements;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Achievements;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Infrastructure;

/// <summary>
/// 成就页自检：这条需求是「先立骨架与契约」，最容易悄悄退化的不是编译错误，而是
/// 菜单项 / 导航 Tag / 页面类型三处只改了两处（菜单点了没反应）、
/// 空状态文案被合并成一句「暂无数据」、以及快照默认值被写成 Available
/// （没接数据源却显示「可用」，把接入缺口藏起来）。所以这里读源码文本 + 直接实例化契约来钉住。
/// </summary>
public static class AchievementsPageChecks
{
    private const string PageClass = "PdxLauncher.App.Views.AchievementsPage";
    private const string AchievementsTag = "achievements";
    private const string PageTypeName = "PdxLauncher.App.Views.AchievementsPage";

    /// <summary>空状态三条文案键：未接入 / 连不上 Steam / 确实没有成就，必须各自独立。</summary>
    private static readonly string[] EmptyStateKeys =
    [
        "achievements.unavailable.notSupported",
        "achievements.unavailable.steamUnavailable",
        "achievements.unavailable.empty",
    ];

    /// <summary>ViewModel 里必须引用到的生成键常量（与 <see cref="EmptyStateKeys"/> 一一对应）。</summary>
    private static readonly string[] EmptyStateKeyConstants =
    [
        "Keys.AchievementsUnavailableNotSupported",
        "Keys.AchievementsUnavailableSteamUnavailable",
        "Keys.AchievementsUnavailableEmpty",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var root = FindWorkspaceRoot();
        var conditionRoot = Path.Combine(Path.GetTempPath(), "pdxnext-achievement-page-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            CheckPathsAndMapping(conditionRoot);
            CheckTruncation(conditionRoot);
            CheckHiddenPresentation();
            CheckSchema();
            CheckUiContract(root);
        }
        finally
        {
            try
            {
                if (Directory.Exists(conditionRoot))
                {
                    Directory.Delete(conditionRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // 临时目录清理失败不影响断言。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }
        }

        var appRoot = Path.Combine(root, "src", "PdxLauncher.App");
        var xamlPath = Path.Combine(appRoot, "Views", "AchievementsPage.xaml");
        var pagePath = Path.Combine(appRoot, "Views", "AchievementsPage.xaml.cs");
        var viewModelPath = Path.Combine(appRoot, "ViewModels", "AchievementsViewModel.cs");
        var navigationPath = Path.Combine(appRoot, "Services", "NavigationService.cs");
        var mainWindowPath = Path.Combine(appRoot, "MainWindow.xaml");

        CheckPage(xamlPath);
        CheckViewModel(viewModelPath, pagePath);
        CheckNavigation(navigationPath, mainWindowPath);
        CheckI18n(root);
        CheckContractDefaults();

        return ChecksContext.Failures - before;
    }

    // ------------------------------------------------------------ 页面与 ViewModel

    private static void CheckPage(string xamlPath)
    {
        if (!ChecksContext.Check("成就页: AchievementsPage.xaml 存在", File.Exists(xamlPath), xamlPath))
        {
            return;
        }

        var xaml = File.ReadAllText(xamlPath);
        ChecksContext.Check(
            "成就页: x:Class 指向 AchievementsPage",
            xaml.Contains("x:Class=\"" + PageClass + "\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "成就页: 含列表控件（ListView / ItemsRepeater）",
            xaml.Contains("<ListView", StringComparison.Ordinal)
                || xaml.Contains("<ItemsRepeater", StringComparison.Ordinal));
        ChecksContext.Check(
            "成就页: 列表绑定 ViewModel.Achievements",
            xaml.Contains("ItemsSource=\"{x:Bind ViewModel.Achievements", StringComparison.Ordinal));
        ChecksContext.Check(
            "成就页: 顶部显示「已解锁 X / Y」统计",
            xaml.Contains("ViewModel.CountsText", StringComparison.Ordinal));
        ChecksContext.Check(
            "成就页: 空状态面板绑定 EmptyStateMessage",
            xaml.Contains("ViewModel.EmptyStateMessage", StringComparison.Ordinal));
    }

    private static void CheckViewModel(string viewModelPath, string pagePath)
    {
        if (!ChecksContext.Check("成就页: AchievementsViewModel.cs 存在", File.Exists(viewModelPath), viewModelPath))
        {
            return;
        }

        var viewModel = File.ReadAllText(viewModelPath);
        ChecksContext.Check(
            "成就页: ViewModel 不引用 Microsoft.UI.Xaml",
            !viewModel.Contains("using Microsoft.UI.Xaml", StringComparison.Ordinal)
                && !viewModel.Contains("Microsoft.UI.Xaml.", StringComparison.Ordinal));

        foreach (var constant in EmptyStateKeyConstants)
        {
            ChecksContext.Check(
                "成就页: ViewModel 引用空状态键 " + constant,
                viewModel.Contains(constant, StringComparison.Ordinal));
        }

        ChecksContext.Check(
            "成就页: 切游戏时先清空旧列表",
            viewModel.Contains("Achievements.Clear()", StringComparison.Ordinal));

        // EmptyStateMessage 的「请先选游戏」分支依赖 Game；Game 从 null 变成实例时
        // Availability 可能仍是 NotSupported（初值相同、不触发通知），必须显式通知。
        // 这个 bug 只在真机点开页面时才看得见：标题有游戏名，空状态却还停在「请先选游戏」。
        ChecksContext.Check(
            "成就页: Game 变化时刷新空状态文案",
            System.Text.RegularExpressions.Regex.IsMatch(
                viewModel,
                @"Game\s*\{[\s\S]{0,800}?OnPropertyChanged\(nameof\(EmptyStateMessage\)\)"));

        if (!ChecksContext.Check("成就页: AchievementsPage.xaml.cs 存在", File.Exists(pagePath), pagePath))
        {
            return;
        }

        var page = File.ReadAllText(pagePath);
        ChecksContext.Check(
            "成就页: 页面订阅 CurrentGameChanged",
            page.Contains("AppServices.CurrentGameChanged +=", StringComparison.Ordinal));
        ChecksContext.Check(
            "成就页: 页面卸载时取消订阅",
            page.Contains("AppServices.CurrentGameChanged -=", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ 导航三处一致

    private static void CheckNavigation(string navigationPath, string mainWindowPath)
    {
        if (!ChecksContext.Check(
                "成就页: NavigationService.cs 存在",
                File.Exists(navigationPath),
                navigationPath))
        {
            return;
        }

        var navigation = File.ReadAllText(navigationPath);
        ChecksContext.Check(
            "成就页: NavigationService 定义 AchievementsTag 常量",
            navigation.Contains(
                "public const string AchievementsTag = \"" + AchievementsTag + "\";",
                StringComparison.Ordinal));
        ChecksContext.Check(
            "成就页: TypeNames 映射成就页类型",
            navigation.Contains(
                "[AchievementsTag] = \"" + PageTypeName + "\"",
                StringComparison.Ordinal));

        if (!ChecksContext.Check(
                "成就页: MainWindow.xaml 存在",
                File.Exists(mainWindowPath),
                mainWindowPath))
        {
            return;
        }

        var mainWindow = File.ReadAllText(mainWindowPath);
        ChecksContext.Check(
            "成就页: MainWindow.xaml 有同 Tag 的菜单项",
            mainWindow.Contains("Tag=\"" + AchievementsTag + "\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "成就页: 菜单项文案走 shell.nav.achievements",
            mainWindow.Contains("L.ShellNavAchievements", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ i18n

    private static void CheckI18n(string root)
    {
        foreach (var language in new[] { "zh-CN", "en-US" })
        {
            var path = I18nResw.ResourcesPath(root, language);
            if (!ChecksContext.Check(
                    "成就页: " + language + " Resources.resw 存在",
                    File.Exists(path),
                    path))
            {
                continue;
            }

            var table = I18nResw.Read(root, language);
            foreach (var key in EmptyStateKeys)
            {
                ChecksContext.Check(
                    "成就页: " + language + " 有 " + key,
                    !string.IsNullOrWhiteSpace(I18nResw.Get(table, key)));
            }

            ChecksContext.Check(
                "成就页: " + language + " 有 achievements.title",
                !string.IsNullOrWhiteSpace(I18nResw.Get(table, "achievements.title")));

            ChecksContext.Check(
                "成就页: " + language + " 有 shell.nav.achievements",
                !string.IsNullOrWhiteSpace(I18nResw.Get(table, "shell.nav.achievements")));
        }

        CheckEmptyStateMessagesAreDistinct(root);
    }

    /// <summary>
    /// 三条空状态文案不能同文：中文里三句必须互不相同，否则「未接入」和「没有成就」又会被用户看成同一件事。
    /// </summary>
    private static void CheckEmptyStateMessagesAreDistinct(string root)
    {
        var path = I18nResw.ResourcesPath(root, "zh-CN");
        if (!File.Exists(path))
        {
            return;
        }

        var table = I18nResw.Read(root, "zh-CN");
        var values = new List<string>();
        foreach (var key in EmptyStateKeys)
        {
            if (I18nResw.Get(table, key) is { } value)
            {
                values.Add(value);
            }
        }

        ChecksContext.Check(
            "成就页: 三条空状态文案互不相同",
            values.Count == EmptyStateKeys.Length && values.Distinct(StringComparer.Ordinal).Count() == values.Count,
            string.Join(" | ", values));
    }

    private static void CheckPathsAndMapping(string root)
    {
        var reader = new AchievementConditionReader(new FileSystem());
        var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HINT_TITLE"] = "§Y征服世界§! #bold必读#!指南",
            ["HINT_ALIAS"] = "$HINT_TITLE$",
        };

        var eu4 = Path.Combine(root, "eu4");
        Write(Path.Combine(eu4, "game", "common", "achievements", "achievements.txt"),
            """
            ACH_EU4 = {
                localization = ACH_EU4
                happened = {
                    custom_tooltip = {
                        tooltip = HINT_ALIAS
                    }
                    owns = 1
                }
            }
            """);
        var fromField = reader.Read(eu4, "ACH_EU4", strings);
        ChecksContext.Check("成就条件: EU4 localization 字段精确映射", fromField.HasData && fromField.IsOfficialHint, fromField.Summary);
        ChecksContext.Equal("成就条件: §Y/#bold 标记被清理", "征服世界 必读 指南", fromField.Summary);
        ChecksContext.Check("成就条件: 别名链最多跟随一次即可命中", fromField.Summary.Contains("征服世界", StringComparison.Ordinal));
        ChecksContext.Check(
            "成就条件: 近似名不能映射",
            !reader.Read(eu4, "ACH_EU", strings).HasData);

        var hoi4 = Path.Combine(root, "hoi4");
        Write(Path.Combine(hoi4, "game", "main_menu", "common", "achievements", "achievements.txt"),
            """
            hoi4_exact = {
                happened = {
                    has_war = yes
                }
            }
            """);
        var fromMainMenu = reader.Read(hoi4, "hoi4_exact", strings);
        ChecksContext.Check(
            "成就条件: HOI4 main_menu 布局可见",
            fromMainMenu.UnlockScript.Contains("has_war = yes", StringComparison.Ordinal),
            fromMainMenu.SourceFile);

        var stellaris = Path.Combine(root, "stellaris");
        Write(Path.Combine(stellaris, "common", "achievements.txt"),
            """
            stellaris_exact = {
                possible = {
                    difficulty = grand_council
                }
                happened = {
                    custom_description = {
                        text = HINT_TITLE
                    }
                    is_ironman = yes
                }
            }
            """);
        var fromRoot = reader.Read(stellaris, "stellaris_exact", strings);
        ChecksContext.Check(
            "成就条件: Stellaris 安装根布局可见",
            fromRoot.PossibleScript.Contains("grand_council", StringComparison.Ordinal),
            fromRoot.SourceFile);

        var cycle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["LOOP_A"] = "$LOOP_B$",
            ["LOOP_B"] = "$LOOP_A$",
        };
        Write(Path.Combine(stellaris, "game", "common", "achievements", "other.txt"),
            """
            loop_achievement = {
                happened = {
                    custom_tooltip = {
                        tooltip = LOOP_A
                    }
                    flag = loop
                }
            }
            """);
        var loop = reader.Read(stellaris, "loop_achievement", cycle);
        ChecksContext.Check(
            "成就条件: 本地化别名成环不挂起，退回原始判定",
            !loop.IsOfficialHint && loop.UnlockScript.Contains("flag = loop", StringComparison.Ordinal),
            loop.Summary);

        var missing = reader.Read(Path.Combine(root, "missing"), "anything", strings);
        ChecksContext.Check("成就条件: 找不到目录时为空", !missing.HasData && missing.SourceFile.Length == 0);
    }

    private static void CheckTruncation(string root)
    {
        var reader = new AchievementConditionReader(new FileSystem());
        var install = Path.Combine(root, "truncate-game");
        var longToken = new string('x', AchievementConditionReader.MaxScriptLength + 500);
        Write(Path.Combine(install, "game", "common", "achievements", "long.txt"),
            "long_achievement = {\n  happened = {\n    flag = " + longToken + "\n  }\n}\n");
        var info = reader.Read(install, "long_achievement", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        ChecksContext.Check(
            "成就条件: 超长脚本被截断并标注",
            info.UnlockScript.Length > AchievementConditionReader.MaxScriptLength
                && info.UnlockScript.EndsWith("[truncated]", StringComparison.Ordinal),
            info.UnlockScript.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void CheckHiddenPresentation()
    {
        var hidden = new AchievementEntry(
            "HIDDEN_ACH",
            "隐藏成就",
            "说明",
            IsHidden: true,
            IsUnlocked: false,
            UnlockTimeUtc: null,
            GlobalPercent: null,
            IconUrl: null,
            new AchievementConditionInfo("条件提示", "has_flag = yes", "ironman = yes", "C:\\game\\achievements.txt", true));
        var row = new AchievementRowViewModel(hidden);
        ChecksContext.Check("成就条件: 隐藏成就也带条件数据", row.HasConditionData);
        row.ToggleConditionCommand.Execute(null);
        ChecksContext.Check("成就条件: 点击后展开详情", row.IsExpanded && row.ShowConditionDetails);
        ChecksContext.Equal("成就条件: 官方提示显示", "条件提示", row.ConditionSummary);
        ChecksContext.Check("成就条件: 解锁判定显示", row.ConditionUnlockText.Contains("has_flag", StringComparison.Ordinal));
        ChecksContext.Check("成就条件: 前置限制显示", row.ConditionPossibleText.Contains("ironman", StringComparison.Ordinal));
        ChecksContext.Check("成就条件: 来源显示", row.ConditionSourceText.Length > 0);
        row.ToggleConditionCommand.Execute(null);
        ChecksContext.Check("成就条件: 再次点击收起", !row.IsExpanded && !row.ShowConditionDetails);

        var noData = new AchievementRowViewModel(new AchievementEntry("NO_DATA", "无数据", "说明", false, false, null, null, null));
        ChecksContext.Check("成就条件: 无数据时不可展开详情", !noData.HasConditionData && !noData.ShowConditionDetails);
    }

    private static void CheckSchema()
    {
        ChecksContext.Equal("成就条件: 快照缓存版本已升级", 2, AchievementSnapshotCache.SchemaVersion);
    }

    private static void CheckUiContract(string root)
    {
        var app = Path.Combine(root, "src", "PdxLauncher.App");
        var xaml = File.ReadAllText(Path.Combine(app, "Views", "AchievementsPage.xaml"));
        foreach (var token in new[]
                 {
                     "ToggleConditionCommand",
                     "ShowConditionDetails",
                     "ConditionSummary",
                     "ConditionUnlockText",
                     "ConditionPossibleText",
                      "ConditionSourceDisplay",
                     "ConditionTitle",
                     "ConditionOfficialHint",
                     "ConditionUnlockScript",
                     "ConditionPossibleScript",
                     "ConditionSource",
                     "ConditionNoData",
                     "ConditionToggleAutomationId",
                     "ConditionDetailsAutomationId",
                 })
        {
            ChecksContext.Check("成就条件: XAML 含 " + token, xaml.Contains(token, StringComparison.Ordinal));
        }

        var zh = I18nResw.Read(root, "zh-CN");
        var en = I18nResw.Read(root, "en-US");
        foreach (var key in new[]
                 {
                     "AchievementsConditionTitle",
                     "AchievementsConditionOfficialHint",
                     "AchievementsConditionUnlockScript",
                     "AchievementsConditionPossibleScript",
                     "AchievementsConditionSource",
                     "AchievementsConditionNoData",
                     "AchievementsConditionExpand",
                     "AchievementsConditionCollapse",
                 })
        {
            ChecksContext.Check("成就条件: zh-CN 有 " + key, !string.IsNullOrWhiteSpace(I18nResw.Get(zh, key)));
            ChecksContext.Check("成就条件: en-US 有 " + key, !string.IsNullOrWhiteSpace(I18nResw.Get(en, key)));
        }
    }

    private static void Write(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static bool HasNonEmptyString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString());

    // ------------------------------------------------------------ 契约默认值

    private static void CheckContractDefaults()
    {
        ChecksContext.Equal(
            "成就页: AchievementSnapshot 默认不可用（NotSupported，不是 Available）",
            AchievementAvailability.NotSupported,
            new AchievementSnapshot().Availability);

        var nullSource = new NullAchievementSource();
        var snapshot = nullSource.GetAsync("test-game", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal(
            "成就页: NullAchievementSource 返回 NotSupported",
            AchievementAvailability.NotSupported,
            snapshot.Availability);
        ChecksContext.Check(
            "成就页: NullAchievementSource 返回空列表",
            snapshot.Entries.Count == 0);
        ChecksContext.Check(
            "成就页: NullAchievementSource 带原因键",
            string.Equals(snapshot.Message, NullAchievementSource.NotSupportedMessageKey, StringComparison.Ordinal));

        var entryType = typeof(AchievementEntry);
        foreach (var property in new[]
                 {
                     "Id", "Name", "Description", "IsHidden", "IsUnlocked",
                     "UnlockTimeUtc", "GlobalPercent", "IconUrl",
                 })
        {
            ChecksContext.Check(
                "成就页: AchievementEntry 有 " + property,
                entryType.GetProperty(property) is not null);
        }
    }

    // ------------------------------------------------------------ 路径

}
