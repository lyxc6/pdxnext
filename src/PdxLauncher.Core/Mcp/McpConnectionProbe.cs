namespace PdxLauncher.Core.Mcp;

using System.Diagnostics;
using System.Text;
using System.Text.Json;

/// <summary>
/// 页面「测试连接」的探针：真实启动一次 MCP 子进程，跑 initialize + tools/list，
/// 用实际结果取代「权限文案冒充健康状态」。
/// <para>
/// 只读：不写草稿、不改设置、不碰游戏目录。失败原因分类成可读文本，
/// 让用户知道是「exe 不存在 / 版本太旧 / 开关关闭 / 数据根错误 / 超时」中的哪一种。
/// </para>
/// </summary>
public static class McpConnectionProbe
{
    private const int DefaultTimeoutMilliseconds = 5000;

    /// <summary>探测结果；<see cref="Success"/> 为 true 时 <see cref="ToolCount"/> 才有意义。</summary>
    public sealed record Result(
        bool Success,
        string Code,
        string Message,
        string? ServerVersion,
        string? ProtocolVersion,
        int ToolCount,
        IReadOnlyList<string> ToolNames)
    {
        public static Result Fail(string code, string message) =>
            new(false, code, message, null, null, 0, Array.Empty<string>());
    }

    /// <summary>执行一次探测。exe 路径不存在时直接返回 <c>EXE_MISSING</c>，不启动进程。</summary>
    public static Result Run(
        string exePath,
        string dataRoot,
        bool enabled,
        int timeoutMilliseconds = DefaultTimeoutMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return Result.Fail("EXE_MISSING", "找不到 PdxLauncher.Mcp.exe：" + exePath);
        }

        var request = new StringBuilder()
            .AppendLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"pdxnext-probe\",\"version\":\"1\"}}}")
            .AppendLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}")
            .AppendLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}")
            .ToString();

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false),
                },
            };
            process.StartInfo.ArgumentList.Add("--data-root");
            process.StartInfo.ArgumentList.Add(dataRoot);
            process.StartInfo.ArgumentList.Add("--enabled");
            process.StartInfo.ArgumentList.Add(enabled ? "true" : "false");
            process.StartInfo.ArgumentList.Add("--legacy-read-only");
            process.StartInfo.ArgumentList.Add("true");

            if (!process.Start())
            {
                return Result.Fail("START_FAILED", "MCP 进程没有启动");
            }

            try
            {
                process.StandardInput.Write(request);
                process.StandardInput.Flush();
                process.StandardInput.Close();

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(Math.Max(500, timeoutMilliseconds)))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // 进程可能已自己退出，忽略。
                    }

                    return Result.Fail("TIMEOUT", "MCP 进程在 " + timeoutMilliseconds + " ms 内没有响应");
                }

                return Parse(stdout, stderr, enabled);
            }
            finally
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // 忽略收尾失败。
                    }
                }
            }
        }
        catch (Exception exception)
        {
            return Result.Fail("START_FAILED", "启动 MCP 进程失败：" + exception.Message);
        }
    }

    private static Result Parse(string stdout, string stderr, bool enabled)
    {
        var lines = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith('{'))
            .ToArray();
        if (lines.Length == 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? "没有返回 JSON-RPC 消息" : stderr.Trim();
            return Result.Fail("NO_RESPONSE", detail);
        }

        string? version = null;
        string? protocol = null;
        var tools = new List<string>();
        var sawToolsList = false;
        foreach (var line in lines)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("error", out var error))
                {
                    var message = error.TryGetProperty("message", out var text)
                        ? text.GetString()
                        : "协议错误";
                    return Result.Fail("PROTOCOL_ERROR", message ?? "协议错误");
                }

                if (!root.TryGetProperty("result", out var result))
                {
                    continue;
                }

                if (result.TryGetProperty("serverInfo", out var serverInfo)
                    && serverInfo.TryGetProperty("version", out var versionText))
                {
                    version = versionText.GetString();
                }

                if (result.TryGetProperty("protocolVersion", out var protocolText))
                {
                    protocol = protocolText.GetString();
                }

                if (result.TryGetProperty("tools", out var toolsArray)
                    && toolsArray.ValueKind == JsonValueKind.Array)
                {
                    sawToolsList = true;
                    foreach (var tool in toolsArray.EnumerateArray())
                    {
                        if (tool.TryGetProperty("name", out var name))
                        {
                            tools.Add(name.GetString() ?? string.Empty);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // 非 JSON 行（日志混入 stdout）直接跳过。
            }
        }

        if (!sawToolsList)
        {
            return Result.Fail("NO_TOOLS_LIST", "进程启动了，但没有返回 tools/list");
        }

        if (!enabled)
        {
            return new Result(true, "DISABLED", "MCP 总开关已关闭，工具表为空（符合预期）", version, protocol, 0, tools);
        }

        if (tools.Count == 0)
        {
            return Result.Fail("EMPTY_TOOLS", "MCP 已连接，但工具表为空；检查数据根与访问密钥");
        }

        return new Result(true, "OK", "连接成功", version, protocol, tools.Count, tools);
    }
}
