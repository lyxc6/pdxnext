namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mcp;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>MCP 访问密钥与客户端配置生成自检。</summary>
public static class McpAccessKeyChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var root = PlaysetChecks.CreateTempRoot(fs, "mcp-access-keys");
        try
        {
            var database = new PdxDatabase(fs, new NullLogger(), Path.Combine(root, PdxDatabase.FileName));
            using (database.Open())
            {
            }

            using var probe = SqliteConnection.OpenReadOnly(database.Path);
            ChecksContext.Check("v5 有 mcp_access_keys 表", probe.TableExists("mcp_access_keys"));
            ChecksContext.Check("v5 mcp_calls 有 key_id", HasColumn(probe, "mcp_calls", "key_id"));

            var store = new McpAccessKeyStore(database);
            var created = store.Create(
                "Claude",
                [McpCapabilityCatalog.GamesRead, McpCapabilityCatalog.UploadRead],
                null);
            ChecksContext.Check("生成 pdxmcp_ 前缀 token", created.RawToken.StartsWith("pdxmcp_", StringComparison.Ordinal));
            ChecksContext.Check("摘要里没有明文", !created.Key.DisplayName.Contains(created.RawToken, StringComparison.Ordinal));

            using (var connection = database.Open())
            {
                var storedHash = connection.ExecuteScalar("SELECT key_hash FROM mcp_access_keys WHERE id = ?", created.Key.Id) as string;
                ChecksContext.Check("数据库不存明文 token", storedHash != created.RawToken && storedHash is { Length: > 0 });
            }

            var authorized = store.Authenticate(created.RawToken);
            ChecksContext.Check("正确 token 校验成功", authorized is not null);
            ChecksContext.Check("密钥允许已授权的 list_games", authorized?.Allows("list_games") == true);
            ChecksContext.Check("密钥拒绝未授权的 save_upload_draft", authorized?.Allows("save_upload_draft") == false);
            ChecksContext.Check("错误 token 校验失败", store.Authenticate(created.RawToken + "x") is null);

            var all = store.Create("全部", McpCapabilityCatalog.All, DateTimeOffset.UtcNow.AddDays(1));
            ChecksContext.Check(
                "全部能力允许全部工具",
                store.Authenticate(all.RawToken)?.AllowsAllCapabilities == true);

            ChecksContext.Check("禁用密钥成功", store.SetEnabled(created.Key.Id, false));
            ChecksContext.Check("禁用后校验失败", store.Authenticate(created.RawToken) is null);
            store.SetEnabled(created.Key.Id, true);

            var expired = store.Create("过期", McpCapabilityCatalog.All, DateTimeOffset.UtcNow.AddMinutes(-1));
            ChecksContext.Check("过期密钥校验失败", store.Authenticate(expired.RawToken) is null);

            var rotated = store.Rotate(created.Key.Id);
            ChecksContext.Check("轮换成功", rotated is not null);
            ChecksContext.Check("旧 token 轮换后失效", store.Authenticate(created.RawToken) is null);
            ChecksContext.Check("新 token 轮换后可用", store.Authenticate(rotated!.RawToken) is not null);
            ChecksContext.Check("删除密钥成功", store.Delete(all.Key.Id));

            const string exe = @"C:\Program Files\PdxNext\PdxLauncher.Mcp.exe";
            const string data = @"C:\Program Files\PdxNext\data";
            var claude = McpClientConfigGenerator.Create(
                McpClientKind.ClaudeDesktop, exe, data, true, rotated.Key, rotated.RawToken);
            ChecksContext.Check("Claude Desktop 内联密钥", claude.Contains(rotated.RawToken, StringComparison.Ordinal));
            var codex = McpClientConfigGenerator.Create(
                McpClientKind.Codex, exe, data, true, rotated.Key, rotated.RawToken);
            ChecksContext.Check("Codex 用环境变量占位", codex.Contains("${PDXNEXT_MCP_ACCESS_KEY}", StringComparison.Ordinal));
            var vscode = McpClientConfigGenerator.Create(
                McpClientKind.VsCode, exe, data, true, rotated.Key, rotated.RawToken);
            ChecksContext.Check("VS Code 用 servers.type=stdio", vscode.Contains("\"type\": \"stdio\"", StringComparison.Ordinal));
            var legacy = McpClientConfigGenerator.Create(
                McpClientKind.Cursor, exe, data, true, null, string.Empty);
            ChecksContext.Check("兼容模式无访问密钥", !legacy.Contains("--access-key", StringComparison.Ordinal));
            ChecksContext.Check(
                "兼容模式默认只读",
                legacy.Contains("--legacy-read-only", StringComparison.Ordinal)
                    || legacy.Contains("\"--legacy-read-only\"", StringComparison.Ordinal));
            ChecksContext.Check(
                "带密钥的配置不再传写入开关",
                !claude.Contains("--draft-write", StringComparison.Ordinal));
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        foreach (var row in connection.Query($"PRAGMA table_info({table})"))
        {
            if (row.TryGetValue("name", out var value) && value is string name
                && string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
