namespace PdxLauncher.App.Controls;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

/// <summary>
/// 列表滚动位置的保存与恢复：页面改成「离开即销毁」后，靠它在重新进入时回到原来的位置。
/// <para>
/// 列表的 ScrollViewer 在模板里，页面上拿不到名字，所以这里沿可视树找；
/// 恢复要等行容器生成完（否则可滚动高度还是 0，ChangeView 会被夹到 0），因此按帧重试几次。
/// </para>
/// </summary>
public static class ListViewScrollState
{
    /// <summary>读取列表当前的纵向滚动位置；还没生成 ScrollViewer 时返回 0。</summary>
    /// <param name="root">列表（或任何包含 ScrollViewer 的元素）。</param>
    public static double GetVerticalOffset(DependencyObject? root) => FindScrollViewer(root)?.VerticalOffset ?? 0;

    /// <summary>
    /// 把列表滚回指定位置。行容器还没生成时会等一会儿再试，最多四次。
    /// </summary>
    /// <param name="root">列表（或任何包含 ScrollViewer 的元素）。</param>
    /// <param name="offset">目标纵向偏移（像素），小于等于 0 时什么都不做。</param>
    /// <returns>是否真的滚到了目标位置（允许 1 像素误差）。</returns>
    public static async Task<bool> RestoreAsync(DependencyObject? root, double offset)
    {
        if (root is null || offset <= 0 || double.IsNaN(offset))
        {
            return false;
        }

        var applied = false;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (root is FrameworkElement element)
            {
                element.UpdateLayout();
            }

            var viewer = FindScrollViewer(root);
            if (viewer is null)
            {
                break;
            }

            // 可滚动高度还不够时先不滚，等下一轮；否则会被夹到当前能滚到的位置。
            if (viewer.ScrollableHeight >= offset)
            {
                applied = viewer.ChangeView(null, offset, null, disableAnimation: true);
                if (applied && Math.Abs(viewer.VerticalOffset - offset) < 1)
                {
                    return true;
                }
            }

            await Task.Delay(30);
        }

        // 最后一轮还没到位（内容确实变短了），退而求其次滚到能到的位置。
        var final = FindScrollViewer(root);
        if (final is not null && applied)
        {
            final.ChangeView(null, Math.Min(offset, final.ScrollableHeight), null, disableAnimation: true);
        }

        return applied;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
