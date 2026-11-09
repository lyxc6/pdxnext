namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 工坊详情页分区跳转（SelectorBar）的源码契约。
/// <para>
/// 8 个标签、8 个目标容器、滚动容器与内容容器的 x:Name 是 code-behind 跳转的唯一依据；
/// 少一个不会编译不过（x:Name 只是字符串），只会在真机上表现为「点了某个标签没反应」。
/// 两个新增文案键还必须中英成对，否则切语言会露出键名。
/// </para>
/// </summary>
public static class WorkshopDetailSectionsChecks
{
    private static readonly string[] Tags =
    [
        "gallery",
        "info",
        "status",
        "description",
        "dependencies",
        "changelog",
        "links",
        "comments",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PdxLauncher.App",
            "Views",
            "WorkshopDetailPage.xaml"));
        var code = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PdxLauncher.App",
            "Views",
            "WorkshopDetailPage.xaml.cs"));

        ChecksContext.Check(
            "工坊详情分区: 有 SelectorBar 与自动化 id",
            xaml.Contains("<SelectorBar x:Name=\"DetailSections\"", StringComparison.Ordinal)
                && xaml.Contains("AutomationProperties.AutomationId=\"barWorkshopDetailSections\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊详情分区: SelectorBar 在滚动容器之外（Row 1）",
            xaml.Contains("Grid.Row=\"1\"", StringComparison.Ordinal)
                && RegexIndexOf(xaml, "<SelectorBar", "<ScrollViewer x:Name=\"DetailScroll\"") >= 0);
        ChecksContext.Check(
            "工坊详情分区: 滚动与内容容器有 x:Name",
            xaml.Contains("x:Name=\"DetailScroll\"", StringComparison.Ordinal)
                && xaml.Contains("x:Name=\"DetailContent\"", StringComparison.Ordinal));

        foreach (var tag in Tags)
        {
            ChecksContext.Check(
                "工坊详情分区: 有 Tag=" + tag,
                xaml.Contains("Tag=\"" + tag + "\"", StringComparison.Ordinal));
        }

        foreach (var name in new[]
                 {
                     "sectionGallery",
                     "sectionInfo",
                     "sectionStatus",
                     "sectionDescription",
                     "sectionDependencies",
                     "sectionChangelog",
                     "sectionLinks",
                     "sectionComments",
                 })
        {
            ChecksContext.Check(
                "工坊详情分区: 目标容器有 x:Name=" + name,
                xaml.Contains("x:Name=\"" + name + "\"", StringComparison.Ordinal));
        }

        ChecksContext.Check(
            "工坊详情分区: code-behind 接 SelectionChanged",
            code.Contains("OnSectionSelected(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)", StringComparison.Ordinal)
                && code.Contains("ResolveVisibleSection", StringComparison.Ordinal)
                && code.Contains("DetailScroll.ChangeView", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊详情分区: 不可见分区顺延到下一段",
            code.Contains("for (var index = start; index < SectionOrder.Length; index++)", StringComparison.Ordinal)
                && code.Contains("ActualHeight: > 0", StringComparison.Ordinal));

        var zh = I18nResw.Read(root, "zh-CN");
        var en = I18nResw.Read(root, "en-US");
        foreach (var key in new[] { "workshop.sections.gallery", "workshop.sections.status" })
        {
            ChecksContext.Check(
                "工坊详情分区: zh-CN 有 " + key,
                I18nResw.Get(zh, key) is { Length: > 0 });
            ChecksContext.Check(
                "工坊详情分区: en-US 有 " + key,
                I18nResw.Get(en, key) is { Length: > 0 });
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static int RegexIndexOf(string text, string first, string second)
    {
        var firstAt = text.IndexOf(first, StringComparison.Ordinal);
        var secondAt = text.IndexOf(second, StringComparison.Ordinal);
        return firstAt >= 0 && secondAt > firstAt ? firstAt : -1;
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PdxLauncher.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("找不到仓库根（PdxLauncher.sln）");
    }
}
