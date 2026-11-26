namespace PdxLauncher.App.Controls;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

/// <summary>
/// 按 <see cref="Scale"/> 缩放唯一子元素，并**按缩放后的尺寸参与测量与排版**的容器。
/// <para>
/// 为什么不用 <c>Viewbox</c>：Viewbox 只缩放绘制结果，布局仍按子元素原始大小占位，
/// 父级 ScrollViewer 会按原尺寸计算可滚动范围；主页在 1040×600 下需要「整体缩小后
/// 真的少占高度」，否则仍会露出半截且滚动范围虚高。这里在 Measure/Arrange 里把
/// 子元素按 1/scale 的可用空间测量，再按缩小后的尺寸返回，滚动范围与实际内容一致。
/// </para>
/// <para>
/// 只做缩小（<see cref="Scale"/> ≤ 1）；放大交给调用方决定，避免 1.0 以上把位图糊掉。
/// </para>
/// </summary>
public sealed class PdxScaledPresenter : Panel
{
    public static readonly DependencyProperty ScaleFactorProperty = DependencyProperty.Register(
        nameof(ScaleFactor),
        typeof(double),
        typeof(PdxScaledPresenter),
        new PropertyMetadata(1.0, OnScaleChanged));

    /// <summary>0.01 ~ 2.0 的缩放系数；非法值按 1.0。</summary>
    public double ScaleFactor
    {
        get => (double)GetValue(ScaleFactorProperty);
        set => SetValue(ScaleFactorProperty, value);
    }

    private double EffectiveScale
    {
        get
        {
            var scale = ScaleFactor;
            return double.IsFinite(scale) && scale > 0 ? scale : 1.0;
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            return new Size(0, 0);
        }

        var scale = EffectiveScale;
        var child = Children[0];
        var childAvailable = new Size(
            double.IsFinite(availableSize.Width) ? availableSize.Width / scale : double.PositiveInfinity,
            double.IsFinite(availableSize.Height) ? availableSize.Height / scale : double.PositiveInfinity);
        child.Measure(childAvailable);

        return new Size(child.DesiredSize.Width * scale, child.DesiredSize.Height * scale);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }

        var scale = EffectiveScale;
        var child = Children[0];
        // 子元素按原始尺寸排版（用测量时的可用空间），再由 RenderTransform 缩到最终尺寸。
        // 只约束高度；宽度保持子元素的自然宽度（它自己有 MaxWidth），
        // 这样居中由子元素的 HorizontalAlignment 决定，而不是被容器拉伸。
        // 先按 1/scale 的宽度排版（子元素自身 MaxWidth=1120 且水平居中），再整体缩放。
        var unscaledWidth = Math.Max(0, finalSize.Width / scale);
        var unscaledHeight = Math.Max(0, finalSize.Height / scale);
        child.Arrange(new Rect(0, 0, unscaledWidth, unscaledHeight));

        if (child.RenderTransform is ScaleTransform transform)
        {
            transform.ScaleX = scale;
            transform.ScaleY = scale;
        }
        else
        {
            child.RenderTransform = new ScaleTransform { ScaleX = scale, ScaleY = scale };
        }

        // 锚点必须是左上角：子元素按 1/scale 的宽度排好后，以 (0,0) 缩放，
        // 缩放后的宽度正好等于 finalSize，不会像“中心锚点”那样整体右移并被裁掉。
        // 行内居中由 StackPanel 自己负责。
        child.RenderTransformOrigin = new Point(0, 0);
        return finalSize;
    }

    private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PdxScaledPresenter)d).InvalidateMeasure();
}
