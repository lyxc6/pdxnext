namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 主页对齐官方 2026.14 的自检：公告轮播显示规则（纯函数）、主页/XAML 接线、
/// 无存档时只显示主启动按钮、三点菜单与版本号尺寸，以及新增双语文案。
/// </summary>
public static class HomeNewsCarouselChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckPolicy();
        CheckXamlWiring();
        CheckCodeBehindWiring();
        CheckBilingualKeys();

        return ChecksContext.Failures - before;
    }

    private static void CheckPolicy()
    {
        ChecksContext.Check("单卡不自动轮播", !NewsCarouselPolicy.ShouldAutoAdvance(1));
        ChecksContext.Check("两卡自动轮播", NewsCarouselPolicy.ShouldAutoAdvance(2));
        ChecksContext.Check("零卡不自动轮播", !NewsCarouselPolicy.ShouldAutoAdvance(0));

        ChecksContext.Check("单卡悬停也不显示箭头", !NewsCarouselPolicy.ShouldShowArrows(1, hovered: true));
        ChecksContext.Check("两卡未悬停不显示箭头", !NewsCarouselPolicy.ShouldShowArrows(2, hovered: false));
        ChecksContext.Check("两卡悬停显示箭头", NewsCarouselPolicy.ShouldShowArrows(2, hovered: true));

        ChecksContext.Check("单卡不显示进度点", !NewsCarouselPolicy.ShouldShowDots(1));
        ChecksContext.Check("两卡显示进度点", NewsCarouselPolicy.ShouldShowDots(2));

        ChecksContext.Equal("空集合索引为 -1", -1, NewsCarouselPolicy.ClampIndex(0, 0));
        ChecksContext.Equal("负索引回绕到最后一张", 2, NewsCarouselPolicy.ClampIndex(-1, 3));
        ChecksContext.Equal("越界索引回绕到第一张", 0, NewsCarouselPolicy.ClampIndex(3, 3));

        ChecksContext.Equal("循环前进：最后一张回到第一张", 0, NewsCarouselPolicy.Advance(2, 3, 1));
        ChecksContext.Equal("循环后退：第一张回到最后一张", 2, NewsCarouselPolicy.Advance(0, 3, -1));
        ChecksContext.Equal("空集合前进仍为 -1", -1, NewsCarouselPolicy.Advance(0, 0, 1));

        ChecksContext.Equal("官方自动切换间隔 2 秒", TimeSpan.FromSeconds(2), NewsCarouselPolicy.AutoAdvanceInterval);
        ChecksContext.Equal("进度条宽 160", 160.0, NewsCarouselPolicy.DotsWidth);
        ChecksContext.Equal("进度段宽 37", 37.0, NewsCarouselPolicy.DotWidth);
        ChecksContext.Equal("进度段高 4", 4.0, NewsCarouselPolicy.DotHeight);
    }

    private static void CheckXamlWiring()
    {
        var xamlPath = FindRepoFile(Path.Combine("src", "PdxLauncher.App", "Views", "HomePage.xaml"));
        if (!ChecksContext.Check("找到 HomePage.xaml", xamlPath is not null, xamlPath ?? "未找到"))
        {
            return;
        }

        var text = File.ReadAllText(xamlPath!);
        ChecksContext.Check(
            "继续按钮仅在有存档时显示",
            text.Contains("x:Name=\"ContinueButton\"", StringComparison.Ordinal)
                && ExtractElement(text, "ContinueButton").Contains("Visibility=\"{x:Bind svc:XamlVisibility.FromBool(ViewModel.HasContinue)", StringComparison.Ordinal));
        ChecksContext.Check(
            "三点菜单按钮存在",
            text.Contains("x:Name=\"MoreActionsButton\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "三点菜单按钮点击接到 OnMoreActionsClick",
            ExtractElement(text, "MoreActionsButton").Contains("Click=\"OnMoreActionsClick\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "三个公告槽位都用 NewsCarousel",
            text.Contains("x:Name=\"NewsCarouselMain\"", StringComparison.Ordinal)
                && text.Contains("x:Name=\"NewsCarouselSecondary1\"", StringComparison.Ordinal)
                && text.Contains("x:Name=\"NewsCarouselSecondary2\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "公告槽位保留原 AutomationId",
            text.Contains("CardAutomationId=\"btnNewsMain\"", StringComparison.Ordinal)
                && text.Contains("CardAutomationId=\"btnNewsSecondary1\"", StringComparison.Ordinal)
                && text.Contains("CardAutomationId=\"btnNewsSecondary2\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "主公告槽位尺寸 451×230",
            text.Contains("Width=\"451\"", StringComparison.Ordinal) && text.Contains("Height=\"230\"", StringComparison.Ordinal));
        var versionBlock = ExtractElementByAttribute(text, "AutomationProperties.AutomationId=\"txtHomeVersion\"");
        ChecksContext.Check(
            "版本号高度 24、字号 14、右对齐",
            versionBlock.Contains("Height=\"24\"", StringComparison.Ordinal)
                && versionBlock.Contains("FontSize=\"14\"", StringComparison.Ordinal)
                && versionBlock.Contains("HorizontalAlignment=\"Right\"", StringComparison.Ordinal),
            versionBlock);
    }

    private static void CheckCodeBehindWiring()
    {
        var code = FindRepoFile(Path.Combine("src", "PdxLauncher.App", "Views", "HomePage.xaml.cs"));
        if (!ChecksContext.Check("找到 HomePage.xaml.cs", code is not null, code ?? "未找到"))
        {
            return;
        }

        var text = File.ReadAllText(code!);
        ChecksContext.Check(
            "公告点击接轮播当前卡事件",
            text.Contains("OnNewsCardActivated", StringComparison.Ordinal)
                && text.Contains("CardActivated", StringComparison.Ordinal));
        ChecksContext.Check(
            "打开游戏目录用 InstallRoot",
            text.Contains("InstallRoot", StringComparison.Ordinal)
                && text.Contains("explorer.exe", StringComparison.Ordinal));
        ChecksContext.Check(
            "三点菜单代码里构造打开游戏目录项",
            text.Contains("Localizer.Instance.HomeOpenGameDirectory", StringComparison.Ordinal)
                && text.Contains("new MenuFlyout", StringComparison.Ordinal));
        ChecksContext.Check(
            "打开目录失败写页内状态",
            text.Contains("HomeStatusOpenGameDirectoryFailed", StringComparison.Ordinal));

        var carousel = FindRepoFile(Path.Combine("src", "PdxLauncher.App", "Controls", "NewsCarousel.cs"));
        if (ChecksContext.Check("找到 NewsCarousel.cs", carousel is not null, carousel ?? "未找到"))
        {
            var carouselText = File.ReadAllText(carousel!);
            ChecksContext.Check(
                "轮播使用纯策略判据",
                carouselText.Contains("NewsCarouselPolicy.", StringComparison.Ordinal));
            ChecksContext.Check(
                "轮播单卡也允许点击",
                carouselText.Contains("CurrentItem", StringComparison.Ordinal)
                    && carouselText.Contains("CardActivated", StringComparison.Ordinal));
            ChecksContext.Check(
                "模板未应用时不对空按钮设 Automation 属性",
                carouselText.Contains("AutomationProperties.SetName(_cardButton, name)", StringComparison.Ordinal)
                    && carouselText.Contains("_cardButton is not null", StringComparison.Ordinal));
        }
    }

    private static void CheckBilingualKeys()
    {
        foreach (var key in new[]
        {
            "HomeMoreActions",
            "HomeOpenGameDirectory",
            "HomeNewsPrevious",
            "HomeNewsNext",
            "HomeStatusOpenGameDirectoryFailed",
        })
        {
            ChecksContext.Check("zh-CN 有键 " + key, ReswHasNonEmptyValue("zh-CN", key));
            ChecksContext.Check("en-US 有键 " + key, ReswHasNonEmptyValue("en-US", key));
        }
    }

    private static bool ReswHasNonEmptyValue(string language, string key)
    {
        var path = FindRepoFile(Path.Combine("src", "PdxLauncher.App", "Strings", language, "Resources.resw"));
        if (path is null)
        {
            return false;
        }

        var text = File.ReadAllText(path);
        var marker = "<data name=\"" + key + "\"";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var end = text.IndexOf("</data>", start, StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }

        return text[start..end].Contains("<value></value>", StringComparison.Ordinal) is false
            && text[start..end].Contains("<value>", StringComparison.Ordinal);
    }

    private static string ExtractElement(string text, string name)
    {
        var start = text.IndexOf("x:Name=\"" + name + "\"", StringComparison.Ordinal);
        return start < 0 ? string.Empty : ExtractElementAt(text, start);
    }

    private static string ExtractElementByAttribute(string text, string attribute)
    {
        var start = text.IndexOf(attribute, StringComparison.Ordinal);
        return start < 0 ? string.Empty : ExtractElementAt(text, start);
    }

    private static string ExtractElementAt(string text, int start)
    {
        if (start < 0)
        {
            return string.Empty;
        }

        var elementStart = text.LastIndexOf('<', start);
        var nameOpen = text.IndexOf('"', start);
        var nameClose = nameOpen < 0 ? -1 : text.IndexOf('"', nameOpen + 1);
        var elementEnd = nameClose < 0 ? -1 : text.IndexOf('>', nameClose);
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

            // 仓库根有 version.props；到了这里还没找到就不再往上找，
            // 避免命中 .scratch/ 里留下的旧快照。
            if (File.Exists(Path.Combine(dir.FullName, "version.props")))
            {
                return null;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
