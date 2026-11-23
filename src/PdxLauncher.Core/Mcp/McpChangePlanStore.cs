namespace PdxLauncher.Core.Mcp;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using PdxLauncher.Core.Storage;

/// <summary>MCP 变更计划的 SQLite 存储。</summary>
public sealed class McpChangePlanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly PdxDatabase _database;

    public McpChangePlanStore(PdxDatabase database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>创建草稿计划。</summary>
    public McpChangePlan CreatePlan(McpChangePlanRequest request, string keyId, DateTimeOffset now)
    {
        var plan = new McpChangePlan(
            Guid.NewGuid().ToString("N"),
            request.Domain,
            request.Operation,
            request.GameId,
            request.TargetId,
            request.Patch,
            request.ExpectedHash,
            keyId,
            McpChangePlanStatus.Draft,
            now);
        var patch = JsonSerializer.Serialize(request.Patch, JsonOptions);
        _database.Write(connection => connection.Execute(
            "INSERT INTO mcp_change_plans "
                + "(id, domain, operation, game_id, target_id, patch_json, expected_hash, key_id, status, approved_by, created_utc, updated_utc) "
                + "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, NULL, ?, ?)",
            plan.Id,
            plan.Domain,
            plan.Operation,
            plan.GameId,
            plan.TargetId,
            patch,
            plan.ExpectedHash,
            plan.KeyId,
            plan.Status.ToString(),
            plan.CreatedUtc.ToUnixTimeMilliseconds(),
            plan.CreatedUtc.ToUnixTimeMilliseconds()));
        return plan;
    }

    /// <summary>按 id 读取计划。</summary>
    public McpChangePlan? Get(string id)
    {
        using var connection = _database.Open();
        var rows = connection.Query(
            "SELECT id, domain, operation, game_id, target_id, patch_json, expected_hash, key_id, status, approved_by, created_utc "
                + "FROM mcp_change_plans WHERE id = ?",
            Project,
            id);
        return rows.FirstOrDefault();
    }

    /// <summary>读取最近 100 条计划。</summary>
    public IReadOnlyList<McpChangePlan> List()
    {
        using var connection = _database.Open();
        return connection.Query(
            "SELECT id, domain, operation, game_id, target_id, patch_json, expected_hash, key_id, status, approved_by, created_utc "
                + "FROM mcp_change_plans ORDER BY created_utc DESC, id DESC LIMIT 100",
            Project);
    }

    /// <summary>
    /// 推进状态。<paramref name="actor"/> 为 <c>app</c> 才允许推进到 approved/applied/failed，
    /// 其他调用方只能推进到 validated / awaiting_approval / rejected。
    /// </summary>
    public bool UpdateStatus(string id, McpChangePlanStatus status, string? actor)
    {
        var plan = Get(id);
        if (plan is null || !McpChangePlanState.CanTransition(plan.Status, status))
        {
            return false;
        }

        var appActor = string.Equals(actor, "app", StringComparison.OrdinalIgnoreCase);
        if (status is McpChangePlanStatus.Approved or McpChangePlanStatus.Applied or McpChangePlanStatus.Failed
            && !appActor)
        {
            return false;
        }

        return _database.Write(connection => connection.Execute(
            "UPDATE mcp_change_plans SET status = ?, approved_by = ?, updated_utc = ? WHERE id = ?",
            status.ToString(),
            status == McpChangePlanStatus.Approved ? actor : plan.ApprovedBy,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            id) > 0);
    }

    private static McpChangePlan Project(IReadOnlyDictionary<string, object?> row)
    {
        var patchText = row["patch_json"] as string ?? "{}";
        var patch = JsonSerializer.Deserialize<Dictionary<string, string>>(patchText, JsonOptions)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var statusText = row["status"] as string ?? string.Empty;
        var created = row["created_utc"] is long ticks
            ? DateTimeOffset.FromUnixTimeMilliseconds(ticks)
            : DateTimeOffset.UnixEpoch;

        return new McpChangePlan(
            row["id"] as string ?? string.Empty,
            row["domain"] as string ?? string.Empty,
            row["operation"] as string ?? string.Empty,
            row["game_id"] as string ?? string.Empty,
            row["target_id"] as string ?? string.Empty,
            patch,
            row["expected_hash"] as string ?? string.Empty,
            row["key_id"] as string ?? string.Empty,
            Enum.TryParse(statusText, out McpChangePlanStatus status) ? status : McpChangePlanStatus.Failed,
            created,
            row["approved_by"] as string);
    }
}
