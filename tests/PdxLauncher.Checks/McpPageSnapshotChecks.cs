namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mcp;

/// <summary>MCP 页面快照分页自检。</summary>
public static class McpPageSnapshotChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var rows = Enumerable.Range(0, 5)
            .Select(index => new McpPageRow(index.ToString(), new Dictionary<string, string>(), index.ToString()))
            .ToArray();
        var page = McpPageSnapshotPaging.Slice("playsets", "播放集", "victoria3", rows, 2, 2);

        ChecksContext.Check("页Snapshot页面标识", page.PageId == "playsets");
        ChecksContext.Check("页Snapshot总数", page.TotalCount == 5);
        ChecksContext.Check("页Snapshot当前页条数", page.Rows.Count == 2);
        ChecksContext.Check("页Snapshot有下一页", page.HasMore && page.NextOffset == 4);
        ChecksContext.Check(
            "页Snapshot内容正确",
            page.Rows[0].Id == "2" && page.Rows[1].Id == "3");

        return ChecksContext.Failures == before ? 0 : 1;
    }
}
