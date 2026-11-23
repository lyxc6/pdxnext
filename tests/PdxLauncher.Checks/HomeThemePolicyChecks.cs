namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// CS1 主页视觉特例的行为与接线：只影响 Cities: Skylines 1，
/// 不误伤 CS2 / PA，也不改动原主题的背景、字标、按钮底图与来源。
/// </summary>
public static class HomeThemePolicyChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckIdentityAndSizes();
        CheckSpecOverrides();
        CheckHomePageWiring();

        return ChecksContext.Failures - before;
    }

    private static void CheckIdentityAndSizes()
    {
        foreach (var id in new[] { "cities_skylines", "cities_v2", "CITIES_V2" })
        {
            ChecksContext.Check("CS1 身份: " + id, HomeThemePolicy.IsCitiesSkylines1(id));
            ChecksContext.Equal("CS1 字标宽: " + id, 320.0, HomeThemePolicy.LogoMaxWidth(id));
            ChecksContext.Equal("CS1 字标高: " + id, 144.0, HomeThemePolicy.LogoMaxHeight(id));
        }

        foreach (var id in new[] { "cities_skylines_2", "prison_architect_pc_console", "ck3", "", null })
        {
            var label = id ?? "null";
            ChecksContext.Check("非 CS1 身份: " + label, !HomeThemePolicy.IsCitiesSkylines1(id));
            ChecksContext.Equal("非 CS1 字标宽: " + label, 420.0, HomeThemePolicy.LogoMaxWidth(id));
            ChecksContext.Equal("非 CS1 字标高: " + label, 160.0, HomeThemePolicy.LogoMaxHeight(id));
        }
    }

    private static void CheckSpecOverrides()
    {
        var spec = new ThemeSpec(
            Background: @"C:\themes\app-background.png",
            Logo: @"C:\themes\game-logo.png",
            AppIcon: null,
            AppTaskbarIcon: null,
            ButtonBackground: @"C:\themes\button.png",
            ButtonFont: @"C:\themes\overpass-regular.otf",
            ButtonTextColor: "#33eb18",
            ButtonHoverTextColor: "#6eff5e",
            FontSize: 22,
            FontWeight: 600,
            TextShadow: null,
            Source: ThemeAssetSource.InstallDir);

        var polished = HomeThemePolicy.ApplyOverrides(spec, "cities_v2");
        ChecksContext.Equal("CS1 按钮文字色改为近白", "#F2FFF2", polished.ButtonTextColor);
        ChecksContext.Equal("CS1 按钮悬停文字色纯白", "#FFFFFF", polished.ButtonHoverTextColor);
        ChecksContext.Equal("CS1 背景不变", spec.Background, polished.Background);
        ChecksContext.Equal("CS1 字标不变", spec.Logo, polished.Logo);
        ChecksContext.Equal("CS1 按钮底图不变", spec.ButtonBackground, polished.ButtonBackground);
        ChecksContext.Equal("CS1 主题来源不变", spec.Source, polished.Source);
        ChecksContext.Equal("CS1 字号不变", spec.FontSize, polished.FontSize);

        var untouched = HomeThemePolicy.ApplyOverrides(spec, "ck3");
        ChecksContext.Check("非 CS1 使用原 spec 实例", ReferenceEquals(spec, untouched));
    }

    private static void CheckHomePageWiring()
    {
        var page = FindRepoFile(Path.Combine("src", "PdxLauncher.App", "Views", "HomePage.xaml.cs"));
        if (ChecksContext.Check("找到 HomePage.xaml.cs", page is not null, page ?? "未找到"))
        {
            var text = File.ReadAllText(page!);
            ChecksContext.Check(
                "主页用视觉策略覆盖主题",
                text.Contains("HomeThemePolicy.ApplyOverrides(", StringComparison.Ordinal));
            ChecksContext.Check(
                "主页用视觉策略设置字标尺寸",
                text.Contains("HomeThemePolicy.LogoMaxWidth(", StringComparison.Ordinal)
                    && text.Contains("HomeThemePolicy.LogoMaxHeight(", StringComparison.Ordinal));
            ChecksContext.Check(
                "CS1 两个主按钮都保留主题皮肤",
                text.Contains("IsCitiesSkylines1(game?.GameId)", StringComparison.Ordinal)
                    && text.Contains("|| continueKind == HomeButtonStyleKind.Themed", StringComparison.Ordinal));
        }

        var xaml = FindRepoFile(Path.Combine("src", "PdxLauncher.App", "Views", "HomePage.xaml"));
        if (ChecksContext.Check("找到 HomePage.xaml", xaml is not null, xaml ?? "未找到"))
        {
            var text = File.ReadAllText(xaml!);
            var notice = ExtractElement(text, "SupportNoticeHost");
            ChecksContext.Check("说明条保留玻璃底", notice.Contains("PdxCardGlassBrush", StringComparison.Ordinal));
            ChecksContext.Check("说明条AutomationId仍在TextBlock", text.Contains("AutomationProperties.AutomationId=\"txtHomeSupportNotice\"", StringComparison.Ordinal));
        }
    }

    private static string ExtractElement(string text, string name)
    {
        var start = text.IndexOf("x:Name=\"" + name + "\"", StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var elementStart = text.LastIndexOf('<', start);
        var elementEnd = text.IndexOf('>', start);
        return elementStart >= 0 && elementEnd > elementStart
            ? text[elementStart..(elementEnd + 1)]
            : string.Empty;
    }

    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var isRepoRoot = Directory.Exists(Path.Combine(dir.FullName, ".git"))
                || File.Exists(Path.Combine(dir.FullName, ".git"));
            if (isRepoRoot)
            {
                break;
            }

            dir = dir.Parent;
        }

        var fromCwd = Path.Combine(Directory.GetCurrentDirectory(), relative);
        return File.Exists(fromCwd) ? fromCwd : null;
    }
}
