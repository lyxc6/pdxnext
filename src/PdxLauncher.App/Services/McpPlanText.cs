namespace PdxLauncher.App.Services;

using System.Text;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Mcp;

/// <summary>变更计划在设置页的只读展示：目标、字段 diff、状态。</summary>
public static class McpPlanText
{
    /// <summary>DataTemplate 内不能写 <c>L.*</c>（x:Bind 根是数据对象），按钮文案走静态方法。</summary>
    public static string ApproveText() => Localizer.Instance.SettingsMcpApprovePlan;

    public static string RejectText() => Localizer.Instance.SettingsMcpRejectPlan;

    public static string RotateText() => Localizer.Instance.SettingsMcpRotateKey;

    public static string DeleteText() => Localizer.Instance.SettingsMcpDeleteKey;

    /// <summary>DataTemplate 里的「编辑」按钮文案。</summary>
    public static string EditText() => Localizer.Instance.SettingsMcpKeyEdit;

    /// <summary>把 domain / gameId / targetId / patch 压成一句可核对的话。</summary>
    public static string Describe(
        string domain,
        string gameId,
        string targetId,
        IReadOnlyDictionary<string, string>? patch)
    {
        var builder = new StringBuilder();
        builder.Append(domain);
        if (!string.IsNullOrWhiteSpace(gameId))
        {
            builder.Append(" · ").Append(gameId);
        }

        if (!string.IsNullOrWhiteSpace(targetId))
        {
            builder.Append(" · ").Append(targetId);
        }

        if (patch is { Count: > 0 })
        {
            builder.Append(" · ");
            builder.Append(string.Join(
                ", ",
                patch
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Key + "=" + pair.Value)));
        }

        return builder.ToString();
    }

    public static string Status(McpChangePlanStatus status) => status switch
    {
        McpChangePlanStatus.Draft => Localizer.Instance.SettingsMcpPlanDraft,
        McpChangePlanStatus.Validated => Localizer.Instance.SettingsMcpPlanValidated,
        McpChangePlanStatus.AwaitingApproval => Localizer.Instance.SettingsMcpPlanAwaiting,
        McpChangePlanStatus.Approved => Localizer.Instance.SettingsMcpPlanApproved,
        McpChangePlanStatus.Applied => Localizer.Instance.SettingsMcpPlanApplied,
        McpChangePlanStatus.Rejected => Localizer.Instance.SettingsMcpPlanRejected,
        McpChangePlanStatus.Expired => Localizer.Instance.SettingsMcpPlanExpired,
        _ => Localizer.Instance.SettingsMcpPlanFailed,
    };
}
