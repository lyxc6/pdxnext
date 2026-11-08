namespace PdxLauncher.Mcp;

using System.Text.Json;
using System.Text.Json.Nodes;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// MCP 的 stdio 传输层：按行读写 JSON-RPC 2.0，只暴露 tools 能力。
/// <para>
/// MCP 的 stdio 传输是一行一个 JSON-RPC 消息（不是 LSP 的 Content-Length 帧）。
/// 这里不解析业务字段，方法分发和错误归类都集中在 <see cref="DispatchAsync"/>。
/// </para>
/// </summary>
internal sealed class McpServer
{
    private const string ProtocolVersion = "2024-11-05";
    private const string ServerName = "pdxnext";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
    };

    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly McpTools _tools;
    private readonly IAppLogger _logger;

    public McpServer(
        TextReader input,
        TextWriter output,
        TextWriter error,
        McpTools tools,
        IAppLogger logger)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (line is null)
            {
                return;
            }

            if (line.Length == 0)
            {
                continue;
            }

            JsonNode? request;
            try
            {
                request = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                WriteError(null, -32700, "Parse error");
                continue;
            }

            if (request is not JsonObject message)
            {
                WriteError(null, -32600, "Invalid Request");
                continue;
            }

            var hasId = message.TryGetPropertyValue("id", out var idNode);
            var id = hasId ? idNode?.DeepClone() : null;

            if (!TryReadMethod(message, out var method))
            {
                if (hasId)
                {
                    WriteError(id, -32600, "Invalid Request: method is required");
                }

                continue;
            }

            try
            {
                var result = await DispatchAsync(method, message["params"] as JsonObject, cancellationToken)
                    .ConfigureAwait(false);
                if (hasId && result is not null)
                {
                    WriteResult(id, result);
                }
            }
            catch (McpProtocolException exception)
            {
                if (hasId)
                {
                    WriteError(id, exception.Code, exception.Message);
                }
            }
            catch (Exception exception)
            {
                _logger.Error("MCP 处理 " + method + " 失败", exception);
                if (hasId)
                {
                    WriteError(id, -32603, exception.Message);
                }
            }
        }
    }

    private async Task<JsonNode?> DispatchAsync(
        string method,
        JsonObject? parameters,
        CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "initialize":
                return BuildInitializeResult(parameters);

            case "notifications/initialized":
            case "notifications/cancelled":
                return null;

            case "ping":
                return new JsonObject();

            case "tools/list":
                return new JsonObject { ["tools"] = _tools.ListTools() };

            case "tools/call":
                return await CallToolAsync(parameters, cancellationToken).ConfigureAwait(false);

            // 只声明 tools 能力；有的客户端仍会先问资源/提示，回空表比报错更兼容。
            case "resources/list":
                return new JsonObject { ["resources"] = new JsonArray() };

            case "prompts/list":
                return new JsonObject { ["prompts"] = new JsonArray() };

            default:
                throw new McpProtocolException(-32601, "Method not found: " + method);
        }
    }

    private JsonObject BuildInitializeResult(JsonObject? parameters)
    {
        var requested = parameters?["protocolVersion"] is JsonValue value
            && value.TryGetValue<string>(out var text)
                ? text
                : null;

        // 客户端请求的版本在支持范围内就回同一个；否则回本服务实现的版本，由客户端决定是否继续。
        var version = requested is "2024-11-05" or "2025-03-26" or "2025-06-18"
            ? requested
            : ProtocolVersion;

        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject(),
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = ServerName,
                ["version"] = typeof(McpServer).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            },
            ["instructions"] =
                "PdxNext 上传草稿服务。先调用 list_games，再 list_mods，随后用 get_upload_draft / "
                + "save_upload_draft 读写某个 mod 的上传表单。保存后回到启动器的 Mod 上传面板即可载入；"
                + "本服务不会发布、不会修改游戏或 mod 文件。",
        };
    }

    private async Task<JsonNode> CallToolAsync(JsonObject? parameters, CancellationToken cancellationToken)
    {
        if (parameters?["name"] is not JsonValue nameValue
            || !nameValue.TryGetValue<string>(out var name)
            || string.IsNullOrWhiteSpace(name))
        {
            throw new McpProtocolException(-32602, "tools/call 缺少 name");
        }

        var arguments = parameters["arguments"] as JsonObject ?? new JsonObject();
        var result = await _tools.CallAsync(name, arguments, cancellationToken).ConfigureAwait(false);
        var content = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = result.Text,
            },
        };

        return new JsonObject
        {
            ["content"] = content,
            ["isError"] = result.IsError,
        };
    }

    private static bool TryReadMethod(JsonObject message, out string method)
    {
        method = string.Empty;
        if (message["method"] is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return false;
        }

        method = text;
        return method.Length > 0;
    }

    private void WriteResult(JsonNode? id, JsonNode result)
    {
        Write(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result,
        });
    }

    private void WriteError(JsonNode? id, int code, string message)
    {
        Write(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        });
    }

    private void Write(JsonObject message)
    {
        try
        {
            _output.WriteLine(message.ToJsonString(WriteOptions));
        }
        catch (Exception exception)
        {
            // stdout 断了（客户端退出）时不用再挣扎，记录后让主循环自然结束。
            _logger.Warn("MCP 输出失败：" + exception.Message);
        }
    }
}

/// <summary>JSON-RPC 层的协议错误（由 <see cref="McpServer"/> 翻成 error 响应）。</summary>
internal sealed class McpProtocolException : Exception
{
    public McpProtocolException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    public int Code { get; }
}
