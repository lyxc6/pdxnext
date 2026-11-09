namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 工坊详情页注释滚动条（AnnotatedScrollBar）的源码契约。
/// <para>
/// 接线方向很容易写反：<c>AnnotatedScrollBar.ScrollController</c> 是只读的，
/// 必须把它赋给 <c>ScrollView.ScrollPresenter.VerticalScrollController</c>。
/// 写反了编译不过（真机上则表现为滚动条完全不动），所以用源码断言钉住。
/// 标签位置随正文高度变化重建，漏了订阅 SizeChanged 会让标签停在旧位置。
/// </para>
/// </summary>
public static class AnnotatedScrollBarChecks
{
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
            "注释滚动条: 外层从 ScrollViewer 换成 ScrollView",
            xaml.Contains("<ScrollView x:Name=\"DetailScroll\"", StringComparison.Ordinal)
                && !xaml.Contains("<ScrollViewer x:Name=\"DetailScroll\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "注释滚动条: 有 AnnotatedScrollBar 与自动化 id",
            xaml.Contains("<AnnotatedScrollBar x:Name=\"DetailScrollBar\"", StringComparison.Ordinal)
                && xaml.Contains("AutomationProperties.AutomationId=\"barWorkshopDetailScrollMarks\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "注释滚动条: 接线方向正确（bar 控制器挂到 ScrollPresenter）",
            code.Contains(
                "DetailScroll.ScrollPresenter.VerticalScrollController = DetailScrollBar.ScrollController",
                StringComparison.Ordinal));
        ChecksContext.Check(
            "注释滚动条: 标签按分区 Y 坐标生成",
            code.Contains("new AnnotatedScrollBarLabel(SectionLabel(tag), offset)", StringComparison.Ordinal)
                && code.Contains("TransformToVisual(DetailContent)", StringComparison.Ordinal));
        ChecksContext.Check(
            "注释滚动条: 正文高度变化后重建标签",
            code.Contains("DetailContent.SizeChanged += OnDetailContentSizeChanged", StringComparison.Ordinal)
                && code.Contains("QueueScrollBarLabels", StringComparison.Ordinal)
                && code.Contains("DetailContent.SizeChanged -= OnDetailContentSizeChanged", StringComparison.Ordinal));
        ChecksContext.Check(
            "注释滚动条: 隐藏 ScrollView 自带竖直滚动条（避免双条）",
            xaml.Contains("VerticalScrollBarVisibility=\"Hidden\"", StringComparison.Ordinal));

        return ChecksContext.Failures == before ? 0 : 1;
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
