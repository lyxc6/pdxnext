namespace PdxLauncher.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Services;

/// <summary>
/// 性能概览里的一张环形仪表：标题、中心读数、占比、健康状态、60 秒趋势与自动化 id。
/// <para>
/// 就地更新（而不是每次 Clear+重建）是补间动画的前提：DataTemplate 里的 PdxGaugeRing
/// 只有一直挂在视觉树上，Storyboard 才能从旧弧长滑到新弧长。
/// </para>
/// </summary>
public sealed class GaugeViewModel : ObservableObject
{
    private readonly GaugeTrendBuffer _trend = new();

    private string _valueText;
    private string _detailText;
    private string _trendCaption;
    private double _percent;
    private GaugeStatus _status;
    private IReadOnlyList<TrendPoint> _trendPoints = Array.Empty<TrendPoint>();

    public GaugeViewModel(
        string key,
        string title,
        string valueText,
        string detailText,
        string trendCaption,
        double percent,
        GaugeStatus status,
        string automationId)
    {
        Key = key;
        Title = title;
        _valueText = valueText;
        _detailText = detailText;
        _trendCaption = trendCaption;
        _percent = percent;
        _status = status;
        AutomationId = automationId;
        _trend.Append(percent, DateTimeOffset.Now);
        _trendPoints = GaugeTrendMath.BuildPoints(_trend.Values());
    }

    /// <summary>稳定键（内存 / 缓存 / 日志 / 启动），用于自动化与排错。</summary>
    public string Key { get; }

    public string Title { get; }

    /// <summary>环中心的实测值（如 134 MB）。</summary>
    public string ValueText
    {
        get => _valueText;
        private set => SetProperty(ref _valueText, value);
    }

    /// <summary>环中心的占比说明（如 24%）。</summary>
    public string DetailText
    {
        get => _detailText;
        private set => SetProperty(ref _detailText, value);
    }

    /// <summary>趋势线标注（最近 60 秒），跟当前语言走。</summary>
    public string TrendCaption
    {
        get => _trendCaption;
        private set => SetProperty(ref _trendCaption, value);
    }

    /// <summary>占预算百分比（0 ~ 100），驱动弧长补间。</summary>
    public double Percent
    {
        get => _percent;
        private set => SetProperty(ref _percent, value);
    }

    public GaugeStatus Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>60 秒迷你趋势线的折线点（随每次采样就地重建）。</summary>
    public IReadOnlyList<TrendPoint> TrendPoints
    {
        get => _trendPoints;
        private set => SetProperty(ref _trendPoints, value);
    }

    public string AutomationId { get; }

    /// <summary>用一次新采样就地更新全部显示值；返回是否有变化（决定是否触发高亮脉冲）。</summary>
    public bool Apply(double percent, string valueText, string detailText, string trendCaption, GaugeStatus status)
    {
        var clamped = PerformanceGaugeMath.ClampPercent(percent);
        bool changed = Math.Abs(clamped - Percent) > 0.01
                       || !string.Equals(valueText, ValueText, StringComparison.Ordinal)
                       || status != Status;

        Percent = clamped;
        ValueText = valueText;
        DetailText = detailText;
        TrendCaption = trendCaption;
        Status = status;
        _trend.Append(clamped, DateTimeOffset.Now);
        TrendPoints = GaugeTrendMath.BuildPoints(_trend.Values());
        return changed;
    }
}

/// <summary>
/// 性能页概览里的一张只读指标卡片（标题、数值、说明与自动化 id）。
/// 文案在 <see cref="DiagnosticsViewModel"/> 里按当前语言拼好；切语言后仍整批重建。
/// </summary>
public sealed class DiagnosticMetricViewModel : ObservableObject
{
    private readonly string _key;
    private readonly string _automationId;
    private string _title;
    private string _value;
    private string _detail;

    public DiagnosticMetricViewModel(string key, string title, string value, string detail, string automationId)
    {
        _key = key;
        _title = title;
        _value = value;
        _detail = detail;
        _automationId = automationId;
    }

    /// <summary>稳定键（工作集 / GC …），用于自动化与排错。</summary>
    public string Key => _key;

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }

    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public string AutomationId => _automationId;

    /// <summary>采样后就地更新同一行，避免 ItemsControl 重建卡片造成闪烁。</summary>
    public void Apply(string title, string value, string detail)
    {
        Title = title;
        Value = value;
        Detail = detail;
    }
}

/// <summary>一条优化建议：结论、数字与排查方向。</summary>
public sealed class DiagnosticHintViewModel : ObservableObject
{
    private readonly DiagnosticHintCode _code;
    private readonly string _automationId;
    private string _title;
    private string _detail;

    public DiagnosticHintViewModel(DiagnosticHintCode code, string title, string detail, string automationId)
    {
        _code = code;
        _title = title;
        _detail = detail;
        _automationId = automationId;
    }

    public DiagnosticHintCode Code => _code;

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public string AutomationId => _automationId;

    /// <summary>建议内容变化时只改绑定属性，不把行从视觉树上摘下来。</summary>
    public void Apply(string title, string detail)
    {
        Title = title;
        Detail = detail;
    }
}

/// <summary>网络分区里的一行：一个 host 的请求 / 限流 / 冷却状态。</summary>
public sealed class NetworkHostRowViewModel
{
    public NetworkHostRowViewModel(string host, string statsText, string lastRequestText, string cooldownText, string automationId)
    {
        Host = host;
        StatsText = statsText;
        LastRequestText = lastRequestText;
        CooldownText = cooldownText;
        AutomationId = automationId;
    }

    public string Host { get; }

    public string StatsText { get; }

    public string LastRequestText { get; }

    public string CooldownText { get; }

    public string AutomationId { get; }
}

/// <summary>启动耗时分区的一行：里程碑、累计耗时与距上一段的增量。</summary>
public sealed class StartupMarkRowViewModel
{
    public StartupMarkRowViewModel(string milestone, string label, string elapsedText, string deltaText, string automationId)
    {
        Milestone = milestone;
        Label = label;
        ElapsedText = elapsedText;
        DeltaText = deltaText;
        AutomationId = automationId;
    }

    public string Milestone { get; }

    public string Label { get; }

    public string ElapsedText { get; }

    public string DeltaText { get; }

    public string AutomationId { get; }
}
