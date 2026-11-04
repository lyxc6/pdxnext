namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Services;

/// <summary>
/// 性能页概览里的一张只读指标卡片（标题、数值、说明与自动化 id）。
/// 文案在 <see cref="DiagnosticsViewModel"/> 里按当前语言拼好；语言切换后整批重建。
/// </summary>
public sealed class DiagnosticMetricViewModel
{
    public DiagnosticMetricViewModel(string key, string title, string value, string detail, string automationId)
    {
        Key = key;
        Title = title;
        Value = value;
        Detail = detail;
        AutomationId = automationId;
    }

    /// <summary>稳定键（工作集 / GC …），用于自动化与排错。</summary>
    public string Key { get; }

    public string Title { get; }

    public string Value { get; }

    public string Detail { get; }

    public string AutomationId { get; }
}

/// <summary>一条优化建议：结论、数字与排查方向。</summary>
public sealed class DiagnosticHintViewModel
{
    public DiagnosticHintViewModel(DiagnosticHintCode code, string title, string detail, string automationId)
    {
        Code = code;
        Title = title;
        Detail = detail;
        AutomationId = automationId;
    }

    public DiagnosticHintCode Code { get; }

    public string Title { get; }

    public string Detail { get; }

    public string AutomationId { get; }
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
