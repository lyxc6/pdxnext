namespace PdxLauncher.App.Controls;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PdxLauncher.App.Services;
using Windows.Foundation;

/// <summary>
/// 性能概览用的带值圆环仪表：从正上方起顺时针画弧，弧长 = Value（0 ~ 100）占整圈的比例。
/// <para>
/// 为什么不用 WinUI 的 <see cref="ProgressRing"/>：它只有不确定态转圈，不能显示百分比；
/// 项目也没有 Win2D，所以这里用 <see cref="Path"/> + <see cref="ArcSegment"/> 纯 XAML 绘制，
/// 在一个固定 120×120 的 Viewbox 画布里作画，再整体缩放到控件尺寸。
/// </para>
/// <para>
/// 控件只负责显示：数值与状态由 ViewModel 传入，颜色三态（正常 / 警告 / 危险）走 VisualState，
/// 描边取 WinUI 内置语义色，跟随 Light / Dark / HighContrast 自动切换。
/// </para>
/// </summary>
public sealed class PdxGaugeRing : Control
{
    // 画布固定坐标系：中心 (60,60)，半径 52，与 XAML 里 120×120 的 Viewbox 画布一致。
    private const double CanvasCenter = 60;
    private const double CanvasRadius = 52;

    /// <summary><see cref="Value"/> 的依赖属性（0 ~ 100 的占比）。</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(PdxGaugeRing),
        new PropertyMetadata(0d, OnArcChanged));

    /// <summary><see cref="Title"/> 的依赖属性。</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(PdxGaugeRing),
        new PropertyMetadata(string.Empty));

    /// <summary><see cref="ValueText"/> 的依赖属性（环中心的实测值，如 134 MB）。</summary>
    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText),
        typeof(string),
        typeof(PdxGaugeRing),
        new PropertyMetadata(string.Empty));

    /// <summary><see cref="DetailText"/> 的依赖属性（环中心的占比说明，如 24%）。</summary>
    public static readonly DependencyProperty DetailTextProperty = DependencyProperty.Register(
        nameof(DetailText),
        typeof(string),
        typeof(PdxGaugeRing),
        new PropertyMetadata(string.Empty));

    /// <summary><see cref="Status"/> 的依赖属性。</summary>
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status),
        typeof(GaugeStatus),
        typeof(PdxGaugeRing),
        new PropertyMetadata(GaugeStatus.Normal, OnStatusChanged));

    private Path? _arcPath;

    /// <summary>创建仪表环。</summary>
    public PdxGaugeRing()
    {
        DefaultStyleKey = typeof(PdxGaugeRing);
    }

    /// <summary>占整圈的百分比，自动夹到 0 ~ 100。</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>环下方的标题（内存 / 缓存 / 日志 / 启动）。</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>环中心的实测值。</summary>
    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    /// <summary>环中心 second line（占比）。</summary>
    public string DetailText
    {
        get => (string)GetValue(DetailTextProperty);
        set => SetValue(DetailTextProperty, value);
    }

    /// <summary>健康状态，决定描边颜色。</summary>
    public GaugeStatus Status
    {
        get => (GaugeStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _arcPath = GetTemplateChild("ArcPath") as Path;
        UpdateArc();
        UpdateVisualState(useTransitions: false);
    }

    private static void OnArcChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PdxGaugeRing)d).UpdateArc();

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PdxGaugeRing)d).UpdateVisualState(useTransitions: true);

    /// <summary>按当前 Value 重画圆弧；0% 不画（只留底环），满圈留极小缺口以免 ArcSegment 退化。</summary>
    private void UpdateArc()
    {
        if (_arcPath is null)
        {
            return;
        }

        var percent = PerformanceGaugeMath.ClampPercent(Value);
        _arcPath.Data = BuildArcGeometry(percent);
    }

    private static PathGeometry? BuildArcGeometry(double percent)
    {
        if (percent <= 0.001)
        {
            return null;
        }

        // 满 360° 时起点与终点重合，ArcSegment 会退化成空，故留 0.01° 缺口（肉眼不可见）。
        double degrees = Math.Min(percent / 100d * 360d, 359.99d);
        double radians = (-90d + degrees) * Math.PI / 180d;
        var start = new Point(CanvasCenter, CanvasCenter - CanvasRadius);
        var end = new Point(
            CanvasCenter + CanvasRadius * Math.Cos(radians),
            CanvasCenter + CanvasRadius * Math.Sin(radians));

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(CanvasRadius, CanvasRadius),
            RotationAngle = 0,
            IsLargeArc = degrees > 180d,
            SweepDirection = SweepDirection.Clockwise,
        });

        return new PathGeometry { Figures = { figure } };
    }

    private void UpdateVisualState(bool useTransitions)
    {
        var state = Status switch
        {
            GaugeStatus.Critical => "Critical",
            GaugeStatus.Warning => "Warning",
            _ => "Normal",
        };
        VisualStateManager.GoToState(this, state, useTransitions);
    }
}
