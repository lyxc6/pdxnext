namespace PdxLauncher.Mcp;

using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Mcp;
using PdxLauncher.Core.Storage;

/// <summary>
/// PdxNext MCP 服务入口。
/// <para>
/// 这是一个独立的 stdio 程序，不初始化 WinUI、不加载 Steam。stdout 只允许输出
/// JSON-RPC 消息；日志一律写文件（或 stderr），否则 MCP 客户端会解析失败。
/// </para>
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (!TryParseOptions(args, out var dataRoot, out var enabled, out var draftWriteEnabled, out var accessKeyToken, out var error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        var fileSystem = new FileSystem();
        using var logger = new FileLogger(Path.Combine(dataRoot!, "logs", "pdxnext-mcp.log"));

        try
        {
            using var input = new StreamReader(
                Console.OpenStandardInput(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: false);
            using var output = new StreamWriter(
                Console.OpenStandardOutput(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
                NewLine = "\n",
            };

            var tools = new McpTools(fileSystem, logger, dataRoot!, draftWriteEnabled);
            var database = new PdxDatabase(fileSystem, logger, Path.Combine(dataRoot!, PdxDatabase.FileName));
            var history = new McpCallHistoryStore(database);
            var keyStore = new McpAccessKeyStore(database);
            var authorizedKey = string.IsNullOrWhiteSpace(accessKeyToken)
                ? null
                : keyStore.Authenticate(accessKeyToken);
            tools.SetAuthorizedKey(authorizedKey);
            var server = new McpServer(
                input,
                output,
                Console.Error,
                tools,
                logger,
                enabled,
                history,
                authorizedKey);
            server.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception exception)
        {
            logger.Error("MCP 服务异常退出", exception);
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static bool TryParseOptions(
        string[] args,
        out string? dataRoot,
        out bool enabled,
        out bool draftWriteEnabled,
        out string? accessKeyToken,
        out string? error)
    {
        dataRoot = null;
        enabled = true;
        draftWriteEnabled = false;
        accessKeyToken = null;
        error = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase))
            {
                error = "PdxLauncher.Mcp.exe [--data-root <dir>] [--enabled true|false] "
                    + "[--draft-write true|false] [--access-key <token>]";
                return false;
            }

            if (string.Equals(arg, "--data-root", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    error = "--data-root 需要一个目录参数";
                    return false;
                }

                dataRoot = Path.GetFullPath(args[++i]);
                continue;
            }

            if (arg.StartsWith("--data-root=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg["--data-root=".Length..].Trim();
                if (value.Length == 0)
                {
                    error = "--data-root 需要一个目录参数";
                    return false;
                }

                dataRoot = Path.GetFullPath(value);
                continue;
            }

            if (TryParseBoolOption(args, ref i, "--enabled", enabled, out var parsedEnabled, out error))
            {
                enabled = parsedEnabled;
                continue;
            }

            if (TryParseBoolOption(args, ref i, "--draft-write", draftWriteEnabled, out var parsedDraft, out error))
            {
                draftWriteEnabled = parsedDraft;
                continue;
            }

            if (string.Equals(arg, "--access-key", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    error = "--access-key 需要一个 token";
                    return false;
                }

                accessKeyToken = args[++i];
                continue;
            }

            if (arg.StartsWith("--access-key=", StringComparison.OrdinalIgnoreCase))
            {
                accessKeyToken = arg["--access-key=".Length..].Trim();
                if (accessKeyToken.Length == 0)
                {
                    error = "--access-key 需要一个 token";
                    return false;
                }

                continue;
            }

            error = "不认识的参数：" + arg;
            return false;
        }

        dataRoot ??= PdxDataRoot.Directory;
        return true;
    }

    private static bool TryParseBoolOption(
        string[] args,
        ref int index,
        string name,
        bool current,
        out bool value,
        out string? error)
    {
        value = current;
        error = null;
        var arg = args[index];
        string? raw;
        if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= args.Length)
            {
                error = name + " 需要 true 或 false";
                return false;
            }

            raw = args[++index];
        }
        else if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
        {
            raw = arg[(name.Length + 1)..];
        }
        else
        {
            return false;
        }

        if (bool.TryParse(raw, out value))
        {
            return true;
        }

        error = name + " 需要 true 或 false";
        return false;
    }
}
