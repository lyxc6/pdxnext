namespace PdxLauncher.Checks;

using System;
using System.Collections.Generic;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mcp;

/// <summary>MCP 变更计划的状态机、字段白名单与校验自检。</summary>
public static class McpChangePlanChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var request = new McpChangePlanRequest(
            "playsets",
            "rename_play_set",
            "victoria3",
            "11111111-1111-1111-1111-111111111111",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "新名称",
            },
            "旧名称摘要");

        var validation = McpChangePlanPolicy.Validate(request);
        ChecksContext.Check("安全播放集重命名校验通过", validation.IsValid);
        ChecksContext.Check("校验通过时无错误码", validation.ErrorCode is null);

        var badField = request with
        {
            Patch = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "新名称",
                ["database"] = "DROP",
            },
        };
        ChecksContext.Check("未知字段被拒绝", !McpChangePlanPolicy.Validate(badField).IsValid);
        ChecksContext.Check("未知字段有错误码", McpChangePlanPolicy.Validate(badField).ErrorCode == "PATCH_FIELD_FORBIDDEN");

        var dangerous = request with { Operation = "delete_play_set" };
        ChecksContext.Check("危险操作被拒绝", !McpChangePlanPolicy.Validate(dangerous).IsValid);

        var noTarget = request with { TargetId = string.Empty };
        ChecksContext.Check("缺少目标被拒绝", !McpChangePlanPolicy.Validate(noTarget).IsValid);

        var createdTime = DateTimeOffset.UnixEpoch;
        var draftStatus = McpChangePlanStatus.Draft;
        var draftPlan = new McpChangePlan(
            "plan-1",
            request.Domain,
            request.Operation,
            request.GameId,
            request.TargetId,
            request.Patch,
            request.ExpectedHash,
            "key-1",
            draftStatus,
            createdTime);
        ChecksContext.Check("新计划处于 draft", draftPlan.Status == McpChangePlanStatus.Draft);
        ChecksContext.Check("计划带来源密钥", draftPlan.KeyId == "key-1");
        ChecksContext.Check("计划保留期望摘要", draftPlan.ExpectedHash == "旧名称摘要");

        ChecksContext.Check("draft 可推进到 validated", McpChangePlanState.CanTransition(draftPlan.Status, McpChangePlanStatus.Validated));
        ChecksContext.Check("validated 可推进到 awaiting_approval", McpChangePlanState.CanTransition(McpChangePlanStatus.Validated, McpChangePlanStatus.AwaitingApproval));
        ChecksContext.Check("awaiting_approval 可推进到 approved", McpChangePlanState.CanTransition(McpChangePlanStatus.AwaitingApproval, McpChangePlanStatus.Approved));
        ChecksContext.Check("draft 不能直接 approved", !McpChangePlanState.CanTransition(draftPlan.Status, McpChangePlanStatus.Approved));
        ChecksContext.Check("applied 是终态", !McpChangePlanState.CanTransition(McpChangePlanStatus.Applied, McpChangePlanStatus.Rejected));

        var approved = new McpChangePlan(
            "plan-1",
            request.Domain,
            request.Operation,
            request.GameId,
            request.TargetId,
            request.Patch,
            request.ExpectedHash,
            "key-1",
            McpChangePlanStatus.Approved,
            createdTime);
        ChecksContext.Check("已批准计划未过期", !McpChangePlanPolicy.IsExpired(approved, createdTime));
        var expired = approved with { CreatedUtc = createdTime.AddHours(-2) };
        ChecksContext.Check("超过有效期的计划过期", McpChangePlanPolicy.IsExpired(expired, createdTime));

        return ChecksContext.Failures == before ? 0 : 1;
    }
}
