namespace PdxLauncher.App.Services;

/// <summary>设置页 MCP 筛选下拉的一项：<see cref="Value"/> 为 null 表示「全部」。</summary>
public sealed record McpFilterOption(string Label, string? Value)
{
    public override string ToString() => Label;
}
