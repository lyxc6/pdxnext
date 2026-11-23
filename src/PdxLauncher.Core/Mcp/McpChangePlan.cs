namespace PdxLauncher.Core.Mcp;

using System;
using System.Collections.Generic;

/// <summary>AI 提议、用户确认后才可执行的业务变更计划。</summary>
public sealed record McpChangePlanRequest(
    string Domain,
    string Operation,
    string GameId,
    string TargetId,
    IReadOnlyDictionary<string, string> Patch,
    string ExpectedHash);

/// <summary>变更计划状态。</summary>
public enum McpChangePlanStatus
{
    Draft,
    Validated,
    AwaitingApproval,
    Approved,
    Applied,
    Rejected,
    Expired,
    Failed,
}

/// <summary>持久化后的变更计划。</summary>
public sealed record McpChangePlan(
    string Id,
    string Domain,
    string Operation,
    string GameId,
    string TargetId,
    IReadOnlyDictionary<string, string> Patch,
    string ExpectedHash,
    string KeyId,
    McpChangePlanStatus Status,
    DateTimeOffset CreatedUtc,
    string? ApprovedBy = null);

/// <summary>变更计划校验结果。</summary>
public readonly record struct McpChangePlanValidation(bool IsValid, string? ErrorCode);

/// <summary>状态流转规则。</summary>
public static class McpChangePlanState
{
    /// <summary>判断状态是否可以流转。</summary>
    public static bool CanTransition(McpChangePlanStatus from, McpChangePlanStatus to) => (from, to) switch
    {
        (McpChangePlanStatus.Draft, McpChangePlanStatus.Validated) => true,
        (McpChangePlanStatus.Draft, McpChangePlanStatus.Rejected) => true,
        (McpChangePlanStatus.Validated, McpChangePlanStatus.AwaitingApproval) => true,
        (McpChangePlanStatus.Validated, McpChangePlanStatus.Rejected) => true,
        (McpChangePlanStatus.AwaitingApproval, McpChangePlanStatus.Approved) => true,
        (McpChangePlanStatus.AwaitingApproval, McpChangePlanStatus.Rejected) => true,
        (McpChangePlanStatus.Approved, McpChangePlanStatus.Applied) => true,
        (McpChangePlanStatus.Approved, McpChangePlanStatus.Failed) => true,
        _ => false,
    };
}

/// <summary>首期变更计划白名单与有效期规则。</summary>
public static class McpChangePlanPolicy
{
    /// <summary>计划有效期（用户长时间不确认后要求 AI 重新生成）。</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private static readonly Dictionary<string, HashSet<string>> AllowedOperations = new(StringComparer.Ordinal)
    {
        ["playsets"] = new(StringComparer.Ordinal)
        {
            "create_play_set",
            "rename_play_set",
            "set_mod_enabled",
        },
        ["settings"] = new(StringComparer.Ordinal)
        {
            "set_setting",
        },
    };

    private static readonly Dictionary<string, HashSet<string>> AllowedFields = new(StringComparer.Ordinal)
    {
        ["create_play_set"] = new(StringComparer.Ordinal) { "name" },
        ["rename_play_set"] = new(StringComparer.Ordinal) { "name" },
        ["set_mod_enabled"] = new(StringComparer.Ordinal) { "modId", "enabled" },
        ["set_setting"] = new(StringComparer.Ordinal) { "key", "value" },
    };

    private static readonly HashSet<string> ForbiddenSettingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "McpEnabled",
        "McpDraftWriteEnabled",
        "SteamWorkshopSubscribeEnabled",
        "PdxRememberMe",
        "ProxyMode",
        "ProxyUrl",
        "SteamApiDllPath",
    };

    /// <summary>校验计划请求是否落在首期白名单里。</summary>
    public static McpChangePlanValidation Validate(McpChangePlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Domain)
            || string.IsNullOrWhiteSpace(request.Operation)
            || string.IsNullOrWhiteSpace(request.GameId)
            || string.IsNullOrWhiteSpace(request.TargetId))
        {
            return new McpChangePlanValidation(false, "PLAN_FIELDS_MISSING");
        }

        if (!AllowedOperations.TryGetValue(request.Domain, out var operations)
            || !operations.Contains(request.Operation))
        {
            return new McpChangePlanValidation(false, "PLAN_OPERATION_FORBIDDEN");
        }

        if (!AllowedFields.TryGetValue(request.Operation, out var allowedFields))
        {
            return new McpChangePlanValidation(false, "PLAN_OPERATION_FORBIDDEN");
        }

        foreach (var field in request.Patch.Keys)
        {
            if (!allowedFields.Contains(field))
            {
                return new McpChangePlanValidation(false, "PATCH_FIELD_FORBIDDEN");
            }
        }

        if (request.Operation == "set_setting"
            && request.Patch.TryGetValue("key", out var settingKey)
            && ForbiddenSettingKeys.Contains(settingKey))
        {
            return new McpChangePlanValidation(false, "SETTING_FIELD_FORBIDDEN");
        }

        return new McpChangePlanValidation(true, null);
    }

    /// <summary>从已有标识创建初始计划。</summary>
    public static McpChangePlan FromId(
        string id,
        McpChangePlanRequest request,
        string keyId,
        DateTimeOffset now) =>
        new(
            id,
            request.Domain,
            request.Operation,
            request.GameId,
            request.TargetId,
            request.Patch,
            request.ExpectedHash,
            keyId,
            McpChangePlanStatus.Draft,
            now);

    /// <summary>创建新计划。</summary>
    public static McpChangePlan Create(McpChangePlanRequest request, string keyId, DateTimeOffset now) =>
        FromId(Guid.NewGuid().ToString("N"), request, keyId, now);

    /// <summary>判断计划是否超过有效期。</summary>
    public static bool IsExpired(McpChangePlan plan, DateTimeOffset now) =>
        now > plan.CreatedUtc + Lifetime;
}
