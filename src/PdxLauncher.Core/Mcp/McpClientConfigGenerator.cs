namespace PdxLauncher.Core.Mcp;

using System.Text.Json;
using System.Text.Encodings.Web;

/// <summary>支持的 MCP 客户端配置类型。</summary>
public enum McpClientKind
{
    ClaudeDesktop,
    ClaudeCode,
    Codex,
    Cursor,
    VsCode,
}

/// <summary>按客户端生成 MCP 连接配置片段。</summary>
public static class McpClientConfigGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Create(
        McpClientKind client,
        string exePath,
        string dataRoot,
        bool enabled,
        bool draftWriteEnabled,
        McpAccessKey? accessKey,
        string rawToken)
    {
        var args = new List<string>
        {
            "--data-root", dataRoot,
            "--enabled", enabled ? "true" : "false",
            "--draft-write", draftWriteEnabled ? "true" : "false",
        };

        if (accessKey is not null)
        {
            if (client == McpClientKind.ClaudeDesktop)
            {
                args.Add("--access-key");
                args.Add(rawToken);
            }
            else
            {
                args.Add("--access-key");
                args.Add("${PDXNEXT_MCP_ACCESS_KEY}");
            }
        }

        if (client == McpClientKind.Cursor)
        {
            return JsonSerializer.Serialize(
                new
                {
                    mcpServers = new Dictionary<string, object>
                    {
                        ["pdxnext"] = new Dictionary<string, object>
                        {
                            ["command"] = exePath,
                            ["args"] = args,
                            ["env"] = new Dictionary<string, string>
                            {
                                ["PDXNEXT_MCP_ACCESS_KEY"] = accessKey is null ? string.Empty : rawToken,
                            },
                        },
                    },
                },
                JsonOptions);
        }

        if (client == McpClientKind.VsCode)
        {
            return JsonSerializer.Serialize(
                new
                {
                    servers = new Dictionary<string, object>
                    {
                        ["pdxnext"] = new Dictionary<string, object>
                        {
                            ["type"] = "stdio",
                            ["command"] = exePath,
                            ["args"] = args,
                            ["env"] = new Dictionary<string, string>
                            {
                                ["PDXNEXT_MCP_ACCESS_KEY"] = accessKey is null ? string.Empty : rawToken,
                            },
                        },
                    },
                },
                JsonOptions);
        }

        return JsonSerializer.Serialize(
            new
            {
                mcpServers = new Dictionary<string, object>
                {
                    ["pdxnext"] = new Dictionary<string, object>
                    {
                        ["command"] = exePath,
                        ["args"] = args,
                        ["env"] = new Dictionary<string, string>
                        {
                            ["PDXNEXT_MCP_ACCESS_KEY"] = accessKey is null ? string.Empty : rawToken,
                        },
                    },
                },
            },
            JsonOptions);
    }
}
