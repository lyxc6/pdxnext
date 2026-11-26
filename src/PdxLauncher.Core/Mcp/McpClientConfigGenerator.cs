namespace PdxLauncher.Core.Mcp;

using System.Text;
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
        McpAccessKey? accessKey,
        string rawToken,
        bool legacyReadOnly = true)
    {
        var args = new List<string>
        {
            "--data-root", dataRoot,
            "--enabled", enabled ? "true" : "false",
        };

        // 不带密钥的兼容模式默认只读；带着密钥时权限完全由密钥能力决定，不再传写入开关。
        if (accessKey is null)
        {
            args.Add("--legacy-read-only");
            args.Add(legacyReadOnly ? "true" : "false");
        }

        // Claude Code / Codex 用环境变量传密钥；只有 Claude Desktop 的 JSON 支持内联把它当默认值。
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

        // Codex 读 ~/.codex/config.toml 的 [mcp_servers.<name>]，不是 mcpServers JSON；
        // 把 JSON 粘进去会让整份配置解析失败。
        if (client == McpClientKind.Codex)
        {
            return CreateCodexToml(exePath, args, accessKey, rawToken);
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

    /// <summary>生成 Codex 的 TOML 片段；密钥用环境变量占位，避免明文写进配置。</summary>
    private static string CreateCodexToml(string exePath, List<string> args, McpAccessKey? accessKey, string rawToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[mcp_servers.pdxnext]");
        builder.AppendLine("command = " + TomlString(exePath));
        builder.Append("args = [");
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(TomlString(args[i]));
        }

        builder.AppendLine("]");
        if (accessKey is not null)
        {
            builder.AppendLine("[mcp_servers.pdxnext.env]");
            builder.Append("PDXNEXT_MCP_ACCESS_KEY = ").AppendLine(TomlString(rawToken));
        }

        return builder.ToString().TrimEnd();
    }

    private static string TomlString(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
