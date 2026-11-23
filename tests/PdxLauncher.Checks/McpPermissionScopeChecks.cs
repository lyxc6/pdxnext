namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mcp;

/// <summary>MCP 作用域白名单与密钥授权语义自检。</summary>
public static class McpPermissionScopeChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        ChecksContext.Check("upload.read 是有效作用域", McpPermissionScopes.IsValid("upload.read"));
        ChecksContext.Check(
            "playsets.write.safe 是有效作用域",
            McpPermissionScopes.IsValid("playsets.write.safe"));
        ChecksContext.Check("未知作用域无效", !McpPermissionScopes.IsValid("saves.delete"));

        var all = new McpAuthorizedKey("key-all", "全部", ["*"]);
        var readOnly = new McpAuthorizedKey("key-read", "只读", ["upload.read", "playsets.read"]);
        var playSetWriter = new McpAuthorizedKey("key-playsets", "播放集", ["playsets.read", "playsets.write.safe"]);

        ChecksContext.Check("* 允许全部工具", all.AllowsAllTools);
        ChecksContext.Check("* 包含全部作用域", all.AllowsScope("settings.write.safe"));
        ChecksContext.Check("只读密钥允许读取工具", readOnly.Allows("get_playsets_page"));
        ChecksContext.Check("只读密钥禁止计划工具", !readOnly.Allows("create_change_plan"));
        ChecksContext.Check("只读密钥禁止写入作用域", !readOnly.AllowsScope("playsets.write.safe"));
        ChecksContext.Check("写入密钥允许安全写入作用域", playSetWriter.AllowsScope("playsets.write.safe"));
        ChecksContext.Check("写入密钥禁止危险写入作用域", !playSetWriter.AllowsScope("playsets.write.dangerous"));
        ChecksContext.Check("未知工具默认拒绝", !readOnly.Allows("unknown_tool"));

        return ChecksContext.Failures == before ? 0 : 1;
    }
}
