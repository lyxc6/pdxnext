namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mcp;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>MCP 变更计划 SQLite 持久化与状态流转自检。</summary>
public static class McpChangePlanStoreChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var root = PlaysetChecks.CreateTempRoot(fs, "mcp-change-plans");
        try
        {
            var database = new PdxDatabase(fs, new NullLogger(), Path.Combine(root, PdxDatabase.FileName));
            using (database.Open())
            {
            }

            using var probe = SqliteConnection.OpenReadOnly(database.Path);
            ChecksContext.Check("v6 有 mcp_change_plans 表", probe.TableExists("mcp_change_plans"));

            var store = new McpChangePlanStore(database);
            var created = store.CreatePlan(
                new McpChangePlanRequest(
                    "playsets",
                    "rename_play_set",
                    "victoria3",
                    "11111111-1111-1111-1111-111111111111",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "新名称" },
                    "旧摘要"),
                "key-1",
                DateTimeOffset.UnixEpoch);

            ChecksContext.Check("创建后是 draft", created.Status == McpChangePlanStatus.Draft);
            ChecksContext.Check("存储读取草稿", store.Get(created.Id)?.Status == McpChangePlanStatus.Draft);
            ChecksContext.Check("列出包含新计划", store.List().Any(plan => plan.Id == created.Id));

            ChecksContext.Check("推进到 validated", store.UpdateStatus(created.Id, McpChangePlanStatus.Validated, null));
            ChecksContext.Check("存储已是 validated", store.Get(created.Id)?.Status == McpChangePlanStatus.Validated);
            ChecksContext.Check("非法跳级被拒绝", !store.UpdateStatus(created.Id, McpChangePlanStatus.Approved, null));

            ChecksContext.Check("推进到 awaiting_approval", store.UpdateStatus(created.Id, McpChangePlanStatus.AwaitingApproval, null));
            ChecksContext.Check("非 app 不能批准", !store.UpdateStatus(created.Id, McpChangePlanStatus.Approved, "user"));
            ChecksContext.Check("app 可批准", store.UpdateStatus(created.Id, McpChangePlanStatus.Approved, "app"));
            var approved = store.Get(created.Id);
            ChecksContext.Check("批准人已记录", approved?.ApprovedBy == "app");
            ChecksContext.Check("批准后工具不可再改状态", !store.UpdateStatus(created.Id, McpChangePlanStatus.Rejected, "mcp"));
            ChecksContext.Check("App 可标记应用成功", store.UpdateStatus(created.Id, McpChangePlanStatus.Applied, "app"));
            ChecksContext.Check("应用后是终态", store.Get(created.Id)?.Status == McpChangePlanStatus.Applied);
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
