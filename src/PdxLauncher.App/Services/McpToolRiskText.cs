namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Mcp;

/// <summary>MCP 工具风险与授权状态的界面文本。</summary>
public static class McpToolRiskText
{
    public static string FromRisk(McpToolRisk risk) => risk == McpToolRisk.ReadOnly
        ? Localizer.Instance.SettingsMcpToolReadOnly
        : Localizer.Instance.SettingsMcpToolWrite;

    public static string Authorized(McpToolRisk risk) => risk == McpToolRisk.ReadOnly
        ? Localizer.Instance.SettingsMcpToolAuthorized
        : Localizer.Instance.SettingsMcpToolUnauthorized;
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
