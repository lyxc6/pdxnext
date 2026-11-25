namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Mcp;

/// <summary>MCP 工具风险与授权状态的界面文本。</summary>
public static class McpToolRiskText
{
    public static string Probe() => "probe";

    public static string FromRisk(McpToolRisk risk) => risk == McpToolRisk.ReadOnly
        ? Localizer.Instance.SettingsMcpToolReadOnly
        : Localizer.Instance.SettingsMcpToolWrite;

    /// <summary>
    /// 工具当前是否暴露给客户端：只读工具始终可用，写工具按「总开关 + 草稿写入」判定；
    /// 不再用「风险类型」直接冒充授权结果。
    /// </summary>
    public static string Exposed(McpToolRisk risk)
    {
        var enabled = AppServices.IsInitialized && AppServices.Settings.McpEnabled;
        var writable = AppServices.IsInitialized && AppServices.Settings.McpDraftWriteEnabled;
        var exposed = enabled && (risk == McpToolRisk.ReadOnly || writable);
        return exposed
            ? Localizer.Instance.McpToolExposed
            : Localizer.Instance.McpToolHidden;
    }
}

/// <summary>MCP 最近调用的紧凑说明文本。</summary>
public static class McpCallText
{
    public static string Format(DateTimeOffset started, long durationMs, McpCallOutcome outcome) =>
        Localizer.Instance.T(
            Keys.SettingsMcpCallDetail,
            started.ToLocalTime().ToString("HH:mm:ss"),
            durationMs,
            OutcomeText(outcome));

    private static string OutcomeText(McpCallOutcome outcome) => outcome switch
    {
        McpCallOutcome.Success => Localizer.Instance.SettingsMcpCallSuccess,
        McpCallOutcome.ToolError => Localizer.Instance.SettingsMcpCallToolError,
        McpCallOutcome.ProtocolError => Localizer.Instance.SettingsMcpCallProtocolError,
        _ => Localizer.Instance.SettingsMcpCallInternalError,
    };
}
