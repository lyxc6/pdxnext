namespace PdxLauncher.Core.Mcp;

/// <summary>MCP 暴露的页面业务数据快照；只含展示字段，不含凭据或临时 UI 状态。</summary>
public sealed record McpPageSnapshot(
    string PageId,
    string DisplayName,
    string GameId,
    IReadOnlyList<McpPageField> Fields,
    IReadOnlyList<McpPageRow> Rows,
    int TotalCount,
    int Offset,
    bool HasMore,
    int? NextOffset);

/// <summary>页面快照中的命名字段。</summary>
public sealed record McpPageField(string Name, string Value);

/// <summary>页面快照中的数据行。</summary>
public sealed record McpPageRow(
    string Id,
    IReadOnlyDictionary<string, string> Values,
    string ExpectedHash);
