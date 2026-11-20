namespace PdxLauncher.App.Controls;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using PdxLauncher.App.Services;
using Windows.Foundation;

/// <summary>
/// 性能概览用的带值圆环仪表：从正上方起顺时针画弧，弧长 = Value（0 ~ 100）占整圈的比例。
/// <para>
/// 三个视觉效果都在控件内完成，不依赖 Win2D / 第三方动画库：
/// ① Value 变化时用 <see cref="Storyboard"/> + <see cref="CubicEase"/> 对 AnimatedPercent 补间，
///    每帧重算弧线几何（XAML Path 走 GPU Direct2D，开销可忽略）；
/// ② 数值变化时叠一层描边高亮并渐隐，做出「刚更新」的脉冲；
/// ③ 环下方画一条 60 秒迷你趋势折线，直观看到指标最近是涨是落。
/// </para>
/// </summary>
public sealed class PdxGaugeRing : Control
{
    // 画布固定坐标系：中心 (60,60)，半径 52，与 XAML 里 120×120 的 Viewbox 画布一致。
    private const double CanvasCenter = 60;
    private const double CanvasRadius = 52;

    // 补间时长：小于一秒的刷新间隔，不拖沓又能看出滑动。
    private static readonly Duration TweenDuration = new(TimeSpan.FromMilliseconds(300));

    /// <summary><see cref="Value"/> 的依赖属性（0 ~ 100 的占比）。</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(PdxGaugeRing),
        new PropertyMetadata(0d, OnValueChanged));

    /// <summary>补间后的实际绘制百分比（动画目标，Storyboard 只动它）。</summary>
    public static readonly DependencyProperty AnimatedPercentProperty = DependencyProperty.Register(
        nameof(AnimatedPercent),
        typeof(double),
        typeof(PdxGaugeRing),
        new PropertyMetadata(0d, OnAnimatedPercentChanged));

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

    /// <summary><see cref="TrendPoints"/> 的依赖属性（60 秒迷你趋势折线点）。</summary>
    public static readonly DependencyProperty TrendPointsProperty = DependencyProperty.Register(
        nameof(TrendPoints),
        typeof(IReadOnlyList<TrendPoint>),
        typeof(PdxGaugeRing),
        new PropertyMetadata(null, OnTrendChanged));

    /// <summary>脉冲高亮的依赖属性（0 ~ 1，Storyboard 先拉到 1 再淡回 0）。</summary>
    public static readonly DependencyProperty PulseOpacityProperty = DependencyProperty.Register(
        nameof(PulseOpacity),
        typeof(double),
        typeof(PdxGaugeRing),
        new PropertyMetadata(0d));

    private Path? _arcPath;
    private Path? _glowPath;
    private Path? _trendPath;
    private Storyboard? _tween;
    private Storyboard? _pulse;
    private bool _templateApplied;
    private bool _animationFailureLogged;

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

    /// <summary>补间后的实际绘制百分比（动画目标）。</summary>
    public double AnimatedPercent
    {
        get => (double)GetValue(AnimatedPercentProperty);
        set => SetValue(AnimatedPercentProperty, value);
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

    /// <summary>60 秒迷你趋势折线点。</summary>
    public IReadOnlyList<TrendPoint>? TrendPoints
    {
        get => (IReadOnlyList<TrendPoint>?)GetValue(TrendPointsProperty);
        set => SetValue(TrendPointsProperty, value);
    }

    /// <summary>脉冲高亮透明度（Storyboard 驱动）。</summary>
    public double PulseOpacity
    {
        get => (double)GetValue(PulseOpacityProperty);
        set => SetValue(PulseOpacityProperty, value);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _arcPath = GetTemplateChild("ArcPath") as Path;
        _glowPath = GetTemplateChild("GlowPath") as Path;
        _trendPath = GetTemplateChild("TrendPath") as Path;
        _templateApplied = true;

        // 首次套用模板：直接落到目标值，不从 0 滑上来。
        AnimatedPercent = PerformanceGaugeMath.ClampPercent(Value);
        UpdateArc();
        UpdateTrend();
        UpdateVisualState(useTransitions: false);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PdxGaugeRing)d).OnValueChanged();

    private static void OnAnimatedPercentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PdxGaugeRing)d).UpdateArc();

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PdxGaugeRing)d).UpdateVisualState(useTransitions: true);

    private static void OnTrendChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PdxGaugeRing)d).UpdateTrend();

    private void OnValueChanged()
    {
        if (!_templateApplied)
        {
            return;
        }

        var target = PerformanceGaugeMath.ClampPercent(Value);
        try
        {
            StartTween(target);
            StartPulse();
        }
        catch (Exception ex)
        {
            // 动画只是装饰：DP 回调里绝不能抛，否则会打断 DiagnosticsViewModel.LoadAsync 的采样。
            // 只记一次，避免每秒刷日志。
            if (!_animationFailureLogged)
            {
                _animationFailureLogged = true;
                AppServices.Logger.Warn("仪表环动画不可用，退回即时更新: " + ex.Message);
            }

            AnimatedPercent = target;
            PulseOpacity = 0;
        }
    }

    /// <summary>补间弧长：打断上一次 Storyboard，从当前 AnimatedPercent 滑到新目标。</summary>
    private void StartTween(double target)
    {
        if (Math.Abs(AnimatedPercent - target) < 0.01)
        {
            AnimatedPercent = target;
            return;
        }

        _tween?.Stop();
        var animation = new DoubleAnimation
        {
            To = target,
            Duration = TweenDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, this);
        Storyboard.SetTargetProperty(animation, nameof(AnimatedPercent));

        _tween = new Storyboard();
        _tween.Children.Add(animation);
        _tween.Begin();
    }

    /// <summary>数值更新瞬间的轻微高亮：单个关键帧动画先亮起再淡出（WinUI 不允许同一 Storyboard 里两个动画 targeting 同一属性）。</summary>
    private void StartPulse()
    {
        _pulse?.Stop();
        var pulse = new DoubleAnimationUsingKeyFrames();
        pulse.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            Value = 0.7,
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)),
        });
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            Value = 0,
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(410)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        });
        Storyboard.SetTarget(pulse, this);
        Storyboard.SetTargetProperty(pulse, nameof(PulseOpacity));

        _pulse = new Storyboard();
        _pulse.Children.Add(pulse);
        _pulse.Begin();
    }

    /// <summary>按补间后的 AnimatedPercent 重画前景弧与高亮弧。</summary>
    private void UpdateArc()
    {
        if (_arcPath is null || _glowPath is null)
        {
            return;
        }

        var data = BuildArcGeometry(PerformanceGaugeMath.ClampPercent(AnimatedPercent));
        _arcPath.Data = data;
        _glowPath.Data = data;
    }

    /// <summary>按 TrendPoints 画迷你折线；少于两个点时不画。</summary>
    private void UpdateTrend()
    {
        if (_trendPath is null)
        {
            return;
        }

        var points = TrendPoints;
        if (points is null || points.Count < 2)
        {
            _trendPath.Data = null;
            return;
        }

        var figure = new PathFigure { StartPoint = new Point(points[0].X, points[0].Y) };
        for (var i = 1; i < points.Count; i++)
        {
            figure.Segments.Add(new LineSegment { Point = new Point(points[i].X, points[i].Y) });
        }

        _trendPath.Data = new PathGeometry { Figures = { figure } };
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
