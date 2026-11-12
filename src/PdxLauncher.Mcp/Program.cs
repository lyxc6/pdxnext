namespace PdxLauncher.Mcp;

using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Infrastructure;

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
        if (!TryParseDataRoot(args, out var dataRoot, out var error))
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

            var tools = new McpTools(fileSystem, logger, dataRoot!);
            var server = new McpServer(input, output, Console.Error, tools, logger);
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

    private static bool TryParseDataRoot(string[] args, out string? dataRoot, out string? error)
    {
        dataRoot = null;
        error = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase))
            {
                error = "PdxLauncher.Mcp.exe [--data-root <data directory>]";
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

            error = "不认识的参数：" + arg;
            return false;
        }

        dataRoot ??= PdxDataRoot.Directory;
        return true;
    }
}
