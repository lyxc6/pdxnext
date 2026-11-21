namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mcp;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>MCP 调用历史的 SQLite 往返、筛选、统计与容量裁剪自检。</summary>
public static class McpCallHistoryChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var root = PlaysetChecks.CreateTempRoot(fs, "mcp-call-history");
        try
        {
            var database = new PdxDatabase(fs, new NullLogger(), Path.Combine(root, PdxDatabase.FileName));
            using (database.Open())
            {
            }

            var store = new McpCallHistoryStore(database);
            var now = DateTimeOffset.UtcNow;
            store.Record(new McpCallHistoryEntry(0, now.AddMinutes(-3), 12, "list_games", McpCallOutcome.Success, null, "只读"));
            store.Record(new McpCallHistoryEntry(0, now.AddMinutes(-2), 34, "save_upload_draft", McpCallOutcome.ToolError, "VALIDATION", "可写"));
            store.Record(new McpCallHistoryEntry(0, now.AddMinutes(-1), 8, "list_mods", McpCallOutcome.Success, null, "只读"));

            var all = store.Query();
            ChecksContext.Equal("调用历史按时间倒序", "list_mods", all[0].ToolName);
            ChecksContext.Equal("调用历史条数", 3, all.Count);
            ChecksContext.Equal("成功数统计", 2, store.GetStats().Success);
            ChecksContext.Equal("总数统计", 3, store.GetStats().Total);

            var readOnly = store.Query(new McpCallHistoryQuery(ToolName: "list_games"));
            ChecksContext.Equal("按工具筛选", 1, readOnly.Count);
            var failures = store.Query(new McpCallHistoryQuery(Outcome: McpCallOutcome.ToolError));
            ChecksContext.Equal("按结果筛选", 1, failures.Count);
            ChecksContext.Equal("错误码保留", "VALIDATION", failures[0].ErrorCode ?? "<null>");

            for (var index = 0; index < McpCallHistoryStore.Capacity + 10; index++)
            {
                store.Record(new McpCallHistoryEntry(0, now.AddSeconds(index), 1, "ping", McpCallOutcome.Success, null, "只读"));
            }

            using var connection = database.Open();
            var count = connection.ExecuteScalar("SELECT COUNT(*) FROM mcp_calls") as long? ?? -1L;
            ChecksContext.Equal("调用历史容量裁剪到 500", (long)McpCallHistoryStore.Capacity, count);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
