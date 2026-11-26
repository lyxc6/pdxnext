namespace PdxLauncher.Core.Mcp;

/// <summary>MCP 工具与业务域的最小权限作用域目录。</summary>
public static class McpPermissionScopes
{
    public const string UploadRead = "upload.read";
    public const string UploadWrite = "upload.write";
    public const string PlaySetsRead = "playsets.read";
    public const string PlaySetsWriteSafe = "playsets.write.safe";
    public const string PlaySetsWriteDangerous = "playsets.write.dangerous";
    public const string SettingsRead = "settings.read";
    public const string SettingsWriteSafe = "settings.write.safe";
    public const string SettingsExperimental = "settings.experimental";
    public const string ChangePlansApprove = "changeplans.approve";

    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.Ordinal)
    {
        "get_playsets_page",
        "get_settings_page",
        "mcp_describe_capabilities",
    };

    /// <summary>旧版本写进密钥的已登记作用域；新密钥改用 <see cref="McpCapabilityCatalog"/>。</summary>
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        UploadRead,
        UploadWrite,
        PlaySetsRead,
        PlaySetsWriteSafe,
        PlaySetsWriteDangerous,
        SettingsRead,
        SettingsWriteSafe,
        SettingsExperimental,
        ChangePlansApprove,
    };

    /// <summary>判断字符串是否是已登记的作用域。</summary>
    public static bool IsValid(string? scope) => scope is not null && Known.Contains(scope);

    /// <summary>只读作用域可调用的工具；写工具与计划工具必须按显式授权放行。</summary>
    public static bool IsReadOnlyTool(string toolName) => ReadOnlyTools.Contains(toolName);
}
