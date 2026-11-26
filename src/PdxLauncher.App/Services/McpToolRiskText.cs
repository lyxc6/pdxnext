namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Mcp;

/// <summary>MCP 工具风险与授权状态的界面文本。</summary>
public static class McpToolRiskText
{
    public static string FromRisk(McpToolRisk risk) => risk == McpToolRisk.ReadOnly
        ? Localizer.Instance.SettingsMcpToolReadOnly
        : Localizer.Instance.SettingsMcpToolWrite;

    /// <summary>
    /// 工具是否存在一条可访问它的密钥能力。总开关只决定服务是否在线；
    /// 具体工具是否暴露给某个客户端由密钥权限决定，所以这里只能说「目录内可用」。
    /// </summary>
    public static string Exposed(McpToolRisk risk)
    {
        var enabled = AppServices.IsInitialized && AppServices.Settings.McpEnabled;
        return enabled
            ? Localizer.Instance.McpToolExposed
            : Localizer.Instance.McpToolHidden;
    }
}

/// <summary>
/// 设置页密钥列表的能力摘要：把库里逗号分隔的能力值翻成中文能力名，
/// 不再把 <c>upload.read</c> 这类原始字符串直接丢给用户。
/// </summary>
public static class McpCapabilityText
{
    public static string SummaryFor(string capabilities)
    {
        var list = McpCapabilityCatalog.Normalize(
            (capabilities ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (list.Count == 0)
        {
            return Localizer.Instance.SettingsMcpKeySummaryNone;
        }

        if (list.Count == McpCapabilityCatalog.All.Count)
        {
            return Localizer.Instance.SettingsMcpKeySummaryAll;
        }

        return string.Join(
            Localizer.Instance.SettingsMcpKeySummarySeparator,
            list.Select(McpKeyEditorViewModel.CapabilityLabel));
    }

    public static string ExpiryFor(DateTimeOffset? expiresUtc) => expiresUtc is { } expiry
        ? Localizer.Instance.T(Keys.SettingsMcpKeyExpiryShort, expiry.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
        : string.Empty;
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
