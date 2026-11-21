namespace PdxLauncher.Core.Mcp;

using System.Globalization;

using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>
/// MCP 调用历史的 SQLite 存储。MCP 子进程与启动器共用同一个 <c>pdxnext.sqlite</c>，
/// 只写工具名、结果、耗时和权限模式，绝不记录参数、路径或表单内容。
/// </summary>
public sealed class McpCallHistoryStore
{
    /// <summary>保留的最大调用条数。</summary>
    public const int Capacity = 500;

    private readonly PdxDatabase _database;

    public McpCallHistoryStore(PdxDatabase database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>追加一条调用记录，并把历史裁剪到 <see cref="Capacity"/> 条。</summary>
    public long Record(McpCallHistoryEntry entry)
    {
        return _database.Write(connection =>
        {
            connection.Execute(
                "INSERT INTO mcp_calls (started_utc, duration_ms, tool_name, outcome, error_code, permission_mode) "
                    + "VALUES (?, ?, ?, ?, ?, ?)",
                entry.StartedUtc.ToUnixTimeMilliseconds(),
                entry.DurationMs,
                entry.ToolName,
                entry.Outcome.ToString(),
                entry.ErrorCode,
                entry.PermissionMode);

            connection.Execute(
                "DELETE FROM mcp_calls WHERE id NOT IN "
                    + "(SELECT id FROM mcp_calls ORDER BY started_utc DESC, id DESC LIMIT ?)",
                Capacity);

            return connection.ExecuteScalar("SELECT last_insert_rowid()") as long? ?? 0L;
        });
    }

    /// <summary>读取最近调用（最新在前），可按时长与结果筛选。</summary>
    public IReadOnlyList<McpCallHistoryEntry> Query(McpCallHistoryQuery? query = null, int limit = 100)
    {
        query ??= new McpCallHistoryQuery();
        var clauses = new List<string>();
        var parameters = new List<object?>();

        if (!string.IsNullOrWhiteSpace(query.ToolName))
        {
            clauses.Add("tool_name = ?");
            parameters.Add(query.ToolName);
        }

        if (query.Outcome is { } outcome)
        {
            clauses.Add("outcome = ?");
            parameters.Add(outcome.ToString());
        }

        var where = clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
        var sql = "SELECT id, started_utc, duration_ms, tool_name, outcome, error_code, permission_mode "
            + "FROM mcp_calls" + where + " ORDER BY started_utc DESC, id DESC LIMIT ?";
        parameters.Add(Math.Clamp(limit, 1, Capacity));

        using var connection = _database.Open();
        return connection.Query(sql, Project, parameters.ToArray());
    }

    /// <summary>调用总数与成功数；用于概览卡。</summary>
    public (int Total, int Success) GetStats()
    {
        using var connection = _database.Open();
        var total = connection.ExecuteScalar("SELECT COUNT(*) FROM mcp_calls") as long? ?? 0L;
        var success = connection.ExecuteScalar(
            "SELECT COUNT(*) FROM mcp_calls WHERE outcome = ?",
            McpCallOutcome.Success.ToString()) as long? ?? 0L;
        return ((int)total, (int)success);
    }

    private static McpCallHistoryEntry Project(IReadOnlyDictionary<string, object?> row)
    {
        var id = ReadInt64(row, "id");
        var started = ReadInt64(row, "started_utc");
        var duration = ReadInt64(row, "duration_ms");
        var tool = row.TryGetValue("tool_name", out var toolValue) ? toolValue as string ?? string.Empty : string.Empty;
        var outcomeText = row.TryGetValue("outcome", out var outcomeValue) ? outcomeValue as string ?? string.Empty : string.Empty;
        var errorCode = row.TryGetValue("error_code", out var errorValue) ? errorValue as string : null;
        var permission = row.TryGetValue("permission_mode", out var permissionValue)
            ? permissionValue as string ?? string.Empty
            : string.Empty;

        return new McpCallHistoryEntry(
            id,
            DateTimeOffset.FromUnixTimeMilliseconds(started),
            duration,
            tool,
            Enum.TryParse(outcomeText, out McpCallOutcome parsed) ? parsed : McpCallOutcome.InternalError,
            errorCode,
            permission);
    }

    private static long ReadInt64(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is long number ? number : 0L;
}
