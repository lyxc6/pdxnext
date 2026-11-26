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
    /// 当前配置下是否至少有一把密钥（或兼容模式）能调用该工具。
    /// 逐客户端的实际过滤仍由各自的密钥能力决定。
    /// </summary>
    public static string Exposed(string toolName)
    {
        var enabled = AppServices.IsInitialized && AppServices.Settings.McpEnabled;
        return enabled && IsCoveredByAnyKey(toolName)
            ? Localizer.Instance.McpToolExposed
            : Localizer.Instance.McpToolHidden;
    }

    /// <summary>与设置页目录同一口径：有密钥看密钥能力并集，没密钥看兼容模式的只读能力。</summary>
    private static bool IsCoveredByAnyKey(string toolName)
    {
        if (!AppServices.IsInitialized)
        {
            return true;
        }

        var keys = new McpAccessKeyStore(AppServices.Database).List();
        return keys.Count > 0
            ? keys.Any(key => key.CapabilityList.Any(capability =>
                McpCapabilityCatalog.AllowsTool(capability, toolName)))
            : McpCapabilityCatalog.ReadOnly.Any(capability =>
                McpCapabilityCatalog.AllowsTool(capability, toolName));
    }

    /// <summary>
    /// DataTemplate 里说明「哪些能力能调用这个工具」；不再指向已退役的全局写入开关。
    /// </summary>
    public static string CapabilityGrant(string toolName)
    {
        var plans = toolName == "create_change_plan";
        var labels = McpCapabilityCatalog.All
            .Where(capability => McpCapabilityCatalog.AllowsTool(capability, toolName))
            .Where(capability => !plans || capability is McpCapabilityCatalog.PlaySetsPlan or McpCapabilityCatalog.SettingsPlan)
            .Select(McpKeyEditorViewModel.CapabilityLabel)
            .ToArray();
        return labels.Length == 0
            ? Localizer.Instance.SettingsMcpToolAlways
            : string.Join(
                Localizer.Instance.SettingsMcpKeySummarySeparator,
                labels);
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
