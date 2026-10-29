namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 工坊详情图片区尺寸自检：<see cref="WorkshopGalleryLayout"/> 的内联高度与查看器可用宽高。
/// <para>
/// 起因：查看器里的图被 ScrollViewer 按原图尺寸（工坊给的是 imw=5000）测量，1x 只露出左上角一块；
/// 修好后 1x 必须是「整张刚好放进浮层」，所以这里把上下限、留白与 0/NaN 兜底钉死。
/// </para>
/// <para>纯数学用例，不读写磁盘，不依赖网络。</para>
/// </summary>
public static class WorkshopGalleryLayoutChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckInlineHeight();
        CheckViewerContent();
        CheckDegenerateInputs();
        return ChecksContext.Failures - before;
    }

    private static void CheckInlineHeight()
    {
        // 640 高的窗口按 45% 给 288，落在 [160, 420] 区间内。
        ChecksContext.Equal("640 高 → 288", 288.0, Math.Round(WorkshopGalleryLayout.InlineGalleryHeight(640), 3));
        ChecksContext.Equal("800 高 → 360", 360.0, Math.Round(WorkshopGalleryLayout.InlineGalleryHeight(800), 3));

        // 很高的窗口被 420 上限截住，避免大图吃掉整屏。
        ChecksContext.Equal("1200 高 → 上限 420", 420.0, WorkshopGalleryLayout.InlineGalleryHeight(1200));

        // 很矮的窗口保留 160 下限，否则图片区会被压成一条缝。
        ChecksContext.Equal("200 高 → 下限 160", 160.0, WorkshopGalleryLayout.InlineGalleryHeight(200));
    }

    private static void CheckViewerContent()
    {
        // 1600×1000、顶部栏 68 → 宽 1600-32=1568、高 1000-32-68=900。
        var wide = WorkshopGalleryLayout.ViewerContent(1600, 1000, 68);
        ChecksContext.Equal("1600×1000 宽", 1568.0, wide.Width);
        ChecksContext.Equal("1600×1000 高", 900.0, wide.Height);

        // 最小窗口 1040×600、顶部栏 68 → 宽 1008、高 600-32-68=500。
        var small = WorkshopGalleryLayout.ViewerContent(1040, 600, 68);
        ChecksContext.Equal("1040×600 宽", 1008.0, small.Width);
        ChecksContext.Equal("1040×600 高", 500.0, small.Height);

        // 顶部栏没量出来（0）时，高度只扣留白。
        var noHeader = WorkshopGalleryLayout.ViewerContent(800, 600, 0);
        ChecksContext.Equal("无头栏高度", 568.0, noHeader.Height);
    }

    private static void CheckDegenerateInputs()
    {
        // 折叠状态（0 尺寸）与 NaN 都必须退到最小边长，不能算出 0 把图片压没。
        var collapsed = WorkshopGalleryLayout.ViewerContent(0, 0, 0);
        ChecksContext.Equal("0 尺寸 → 最小边宽", WorkshopGalleryLayout.ViewerMinSide, collapsed.Width);
        ChecksContext.Equal("0 尺寸 → 最小边高", WorkshopGalleryLayout.ViewerMinSide, collapsed.Height);

        var notANumber = WorkshopGalleryLayout.ViewerContent(double.NaN, double.NaN, double.NaN);
        ChecksContext.Equal("NaN → 最小边宽", WorkshopGalleryLayout.ViewerMinSide, notANumber.Width);
        ChecksContext.Equal("NaN → 最小边高", WorkshopGalleryLayout.ViewerMinSide, notANumber.Height);

        // 浮层比「留白 + 头栏」还小的时候也不能是负数。
        var tooSmall = WorkshopGalleryLayout.ViewerContent(100, 100, 80);
        ChecksContext.Equal("过小窗口 → 负值被抬到最小边", WorkshopGalleryLayout.ViewerMinSide, tooSmall.Width);

        // 内联高度：量不到页面高度时用 XAML 同一套默认值。
        ChecksContext.Equal("0 高 → 默认 360", WorkshopGalleryLayout.InlineFallbackHeight, WorkshopGalleryLayout.InlineGalleryHeight(0));
        ChecksContext.Equal("NaN 高 → 默认 360", WorkshopGalleryLayout.InlineFallbackHeight, WorkshopGalleryLayout.InlineGalleryHeight(double.NaN));
    }
}
