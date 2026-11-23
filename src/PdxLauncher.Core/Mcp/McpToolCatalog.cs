namespace PdxLauncher.Core.Mcp;

/// <summary>MCP 工具目录的唯一事实源，设置页与 MCP 服务共用。</summary>
public static class McpToolCatalog
{
    public static readonly IReadOnlyList<McpToolDescriptor> Tools =
    [
        new("list_games", "列出游戏", "列出已安装的 Paradox 游戏和可上传状态。", McpToolRisk.ReadOnly),
        new("list_mods", "列出本地 Mod", "列出某款游戏下可上传的目录型本地 Mod。", McpToolRisk.ReadOnly),
        new("list_tags", "列出标签", "列出某款游戏上传时可选的 Steam 标签机器名。", McpToolRisk.ReadOnly),
        new("get_upload_draft", "读取上传草稿", "读取某个 Mod 的上传草稿或描述符默认值。", McpToolRisk.ReadOnly),
        new("save_upload_draft", "写入上传草稿", "写入上传表单草稿；不会发布 Mod。", McpToolRisk.Write),
        new("clear_upload_draft", "删除上传草稿", "删除某个 Mod 的上传草稿。", McpToolRisk.Write),
        new("mcp_describe_capabilities", "查看 MCP 能力", "列出当前密钥可用的作用域和工具。", McpToolRisk.ReadOnly),
        new("get_playsets_page", "读取播放集页", "按游戏读取播放集列表；可分页。", McpToolRisk.ReadOnly, McpPermissionScopes.PlaySetsRead),
        new("get_settings_page", "读取设置页", "读取可公开的设置项；不包含密钥、代理凭据和账号信息。", McpToolRisk.ReadOnly, McpPermissionScopes.SettingsRead),
        new("create_change_plan", "创建变更计划", "创建需用户在启动器确认的变更计划；不会直接落盘。", McpToolRisk.Write, McpPermissionScopes.PlaySetsWriteSafe),
    ];

    /// <summary>按工具名查找目录项。</summary>
    public static McpToolDescriptor? Find(string name) =>
        Tools.FirstOrDefault(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));
}
