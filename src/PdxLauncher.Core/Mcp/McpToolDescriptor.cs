namespace PdxLauncher.Core.Mcp;

/// <summary>MCP 工具在设置页展示的风险类型。</summary>
public enum McpToolRisk
{
    ReadOnly,
    Write,
}

/// <summary>一个 MCP 工具的目录元数据，供设置页与 MCP 进程共用。</summary>
public sealed record McpToolDescriptor(
    string Name,
    string DisplayName,
    string Description,
    McpToolRisk Risk);

/// <summary>MCP 工具调用结果分类。</summary>
public enum McpCallOutcome
{
    Success,
    ToolError,
    ProtocolError,
    InternalError,
}

/// <summary>一条 MCP 调用历史；只含元数据，不含参数或表单内容。</summary>
public sealed record McpCallHistoryEntry(
    long Id,
    DateTimeOffset StartedUtc,
    long DurationMs,
    string ToolName,
    McpCallOutcome Outcome,
    string? ErrorCode,
    string PermissionMode);

/// <summary>MCP 调用历史的可选筛选条件。</summary>
public sealed record McpCallHistoryQuery(string? ToolName = null, McpCallOutcome? Outcome = null);
