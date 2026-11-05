namespace PdxLauncher.Checks;

using System.Text.Json;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

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
            var achievementsPath = Path.Combine(root, "tools", "i18n", language, "achievements.json");
            var shellPath = Path.Combine(root, "tools", "i18n", language, "shell.json");

            if (!ChecksContext.Check(
                    "成就页: " + language + " achievements.json 存在",
                    File.Exists(achievementsPath),
                    achievementsPath))
            {
                continue;
            }

            using var achievements = JsonDocument.Parse(File.ReadAllText(achievementsPath));
            foreach (var key in EmptyStateKeys)
            {
                ChecksContext.Check(
                    "成就页: " + language + " 有 " + key,
                    HasNonEmptyString(achievements.RootElement, key));
            }

            ChecksContext.Check(
                "成就页: " + language + " 有 achievements.title",
                HasNonEmptyString(achievements.RootElement, "achievements.title"));

            if (!ChecksContext.Check(
                    "成就页: " + language + " shell.json 存在",
                    File.Exists(shellPath),
                    shellPath))
            {
                continue;
            }

            using var shell = JsonDocument.Parse(File.ReadAllText(shellPath));
            ChecksContext.Check(
                "成就页: " + language + " 有 shell.nav.achievements",
                HasNonEmptyString(shell.RootElement, "shell.nav.achievements"));
        }

        CheckEmptyStateMessagesAreDistinct(root);
    }

    /// <summary>
    /// 三条空状态文案不能同文：中文里三句必须互不相同，否则「未接入」和「没有成就」又会被用户看成同一件事。
    /// </summary>
    private static void CheckEmptyStateMessagesAreDistinct(string root)
    {
        var path = Path.Combine(root, "tools", "i18n", "zh-CN", "achievements.json");
        if (!File.Exists(path))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var values = new List<string>();
        foreach (var key in EmptyStateKeys)
        {
            if (document.RootElement.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                values.Add(value.GetString() ?? string.Empty);
            }
        }

        ChecksContext.Check(
            "成就页: 三条空状态文案互不相同",
            values.Count == EmptyStateKeys.Length && values.Distinct(StringComparer.Ordinal).Count() == values.Count,
            string.Join(" | ", values));
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
}
