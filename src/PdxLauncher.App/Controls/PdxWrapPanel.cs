namespace PdxLauncher.App.Controls;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdxLauncher.App.Services;
using Windows.Foundation;

/// <summary>
/// 排不下就换行的按钮条：子元素保持自然尺寸，行内与行间统一 <see cref="Spacing"/>。
/// <para>
/// 为什么自己写：WinUI 3 没有 WrapPanel，CommunityToolkit.WinUI.Controls 也不在本项目的依赖里；
/// 而水平 StackPanel 从不换行，放在不允许横向滚动的 ScrollViewer 里时，超出的按钮会被直接裁掉
/// （Mod 创建页详情那一排在默认窗口宽下就只有前四个半可见）。
/// </para>
/// <para>
/// 排版算法在 <see cref="WrapLayout"/> 里，这里只做「测量子元素 → 计算 → Arrange」的适配，
/// 因此算法本身可以被检查工程链接进去直接断言。
/// </para>
/// </summary>
public sealed class PdxWrapPanel : Panel
{
    /// <summary><see cref="Spacing"/> 的依赖属性。</summary>
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing),
        typeof(double),
        typeof(PdxWrapPanel),
        new PropertyMetadata(8.0, OnSpacingChanged));

    /// <summary>当前参与排版的子元素（按下标顺序）。</summary>
    private UIElement[] _visible = Array.Empty<UIElement>();

    /// <summary>行内与行间距（DIP）。默认 8，与页面里那一排 <c>StackPanel Spacing="8"</c> 对齐。</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>把间距规整成可用值：非有限值或负数按 0。</summary>
    private double EffectiveSpacing
    {
        get
        {
            var spacing = Spacing;
            return double.IsFinite(spacing) && spacing > 0 ? spacing : 0;
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children;
        var visible = new List<UIElement>(children.Count);
        var items = new List<WrapLayout.WrapItem>(children.Count);

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            // 宽度沿用面板给的约束、高度不限：这样按钮不会被压扁，换行只由宽度决定。
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var desired = child.DesiredSize;
            visible.Add(child);
            items.Add(new WrapLayout.WrapItem(desired.Width, desired.Height));
        }

        _visible = visible.Count == 0 ? Array.Empty<UIElement>() : visible.ToArray();
        var result = WrapLayout.Compute(items, availableSize.Width, EffectiveSpacing);
        return new Size(result.Width, result.Height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = _visible;
        if (visible.Length == 0)
        {
            return finalSize;
        }

        // 用最终宽度重算一遍：测量时的可用宽度可能被夹过或还是无穷大，摆放必须以最终宽度为准。
        var items = new List<WrapLayout.WrapItem>(visible.Length);
        foreach (var child in visible)
        {
            var desired = child.DesiredSize;
            items.Add(new WrapLayout.WrapItem(desired.Width, desired.Height));
        }

        var result = WrapLayout.Compute(items, finalSize.Width, EffectiveSpacing);
        foreach (var placement in result.Items)
        {
            if ((uint)placement.Index >= (uint)visible.Length)
            {
                continue;
            }

            visible[placement.Index].Arrange(new Rect(placement.X, placement.Y, placement.Width, placement.Height));
        }

        return finalSize;
    }

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((PdxWrapPanel)d).InvalidateMeasure();
    }
}
