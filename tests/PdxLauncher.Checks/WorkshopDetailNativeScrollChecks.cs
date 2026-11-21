namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 工坊详情页恢复原生滚动 / 原生缩放的源码契约。
/// <para>
/// AnnotatedScrollBar 的滚轮按固定量移动，不跟随系统滚轮速度；看图浮层此前也接管
/// Ctrl+滚轮做固定 1.2 倍缩放。这里钉住两处都回到 ScrollView / ScrollViewer 原生行为，
/// 同时保留顶部 SelectorBar 的分区跳转。
/// </para>
/// </summary>
public static class WorkshopDetailNativeScrollChecks
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
            "工坊详情原生滚动: 正文用 ScrollView 且显示原生竖直滚动条",
            xaml.Contains("<ScrollView x:Name=\"DetailScroll\"", StringComparison.Ordinal)
                && xaml.Contains("VerticalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal)
                && !xaml.Contains("<ScrollViewer x:Name=\"DetailScroll\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊详情原生滚动: 不再出现 AnnotatedScrollBar",
            !xaml.Contains("AnnotatedScrollBar", StringComparison.Ordinal)
                && !xaml.Contains("barWorkshopDetailScrollMarks", StringComparison.Ordinal)
                && !code.Contains("VerticalScrollController", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊详情原生滚动: 无标签重建与滚轮拦截残留",
            !code.Contains("QueueScrollBarLabels", StringComparison.Ordinal)
                && !code.Contains("RebuildScrollBarLabels", StringComparison.Ordinal)
                && !code.Contains("OnViewerPointerWheelChanged", StringComparison.Ordinal)
                && !xaml.Contains("PointerWheelChanged", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊详情原生滚动: 分区跳转仍走 ScrollView.ScrollTo",
            code.Contains("DetailScroll.ScrollTo", StringComparison.Ordinal));
        var overlayStart = xaml.IndexOf("<ScrollViewer ZoomMode=\"Enabled\"", StringComparison.Ordinal);
        var overlayEnd = xaml.IndexOf("</ScrollViewer>", overlayStart, StringComparison.Ordinal);
        var overlayXaml = overlayStart >= 0 && overlayEnd > overlayStart
            ? xaml[overlayStart..overlayEnd]
            : string.Empty;

        ChecksContext.Check(
            "工坊详情原生滚动: 浮层保留原生缩放且不在 XAML 禁用滚动",
            overlayXaml.Contains("ZoomMode=\"Enabled\"", StringComparison.Ordinal)
                && overlayXaml.Contains("MinZoomFactor=\"1\"", StringComparison.Ordinal)
                && overlayXaml.Contains("MaxZoomFactor=\"4\"", StringComparison.Ordinal)
                && !overlayXaml.Contains("VerticalScrollMode=\"Disabled\"", StringComparison.Ordinal)
                && !overlayXaml.Contains("PointerWheelChanged", StringComparison.Ordinal));

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
