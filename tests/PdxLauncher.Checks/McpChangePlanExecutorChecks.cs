namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mcp;

/// <summary>变更计划确认后由 App 执行的安全边界自检。</summary>
public static class McpChangePlanExecutorChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var request = new McpChangePlanRequest(
            "settings",
            "set_setting",
            "settings",
            nameof(AppSettings.UiLanguage),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["key"] = nameof(AppSettings.UiLanguage),
                ["value"] = "en-US",
            },
            "zh-CN");
        var plan = new McpChangePlan(
            "plan-1",
            request.Domain,
            request.Operation,
            request.GameId,
            request.TargetId,
            request.Patch,
            request.ExpectedHash,
            "key-1",
            McpChangePlanStatus.Approved,
            DateTimeOffset.UnixEpoch,
            "app");

        ChecksContext.Check("审批人必须是 app", plan.ApprovedBy == "app");
        var validation = McpChangePlanPolicy.Validate(request);
        ChecksContext.Check("只允许安全设置字段", validation.IsValid, validation.ErrorCode ?? string.Empty);
        ChecksContext.Check("过期计划拒绝执行", McpChangePlanPolicy.IsExpired(plan, DateTimeOffset.UtcNow));

        var unsafeRequest = request with
        {
            TargetId = nameof(AppSettings.McpEnabled),
            Patch = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["key"] = nameof(AppSettings.McpEnabled),
                ["value"] = "true",
            },
        };
        ChecksContext.Check("MCP 总开关禁止通过计划修改", !McpChangePlanPolicy.Validate(unsafeRequest).IsValid);

        var rename = new McpChangePlanRequest(
            "playsets",
            "rename_play_set",
            "victoria3",
            Guid.NewGuid().ToString("D"),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "新名称" },
            "旧摘要");
        ChecksContext.Check("播放集重命名可进入确认", McpChangePlanPolicy.Validate(rename).IsValid);
        ChecksContext.Check("无游戏 id 的播放集计划被拒绝", !McpChangePlanPolicy.Validate(rename with { GameId = string.Empty }).IsValid);

        return ChecksContext.Failures == before ? 0 : 1;
    }
}
