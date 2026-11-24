namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mcp;

/// <summary>MCP 工具目录的权限映射与危险操作边界自检。</summary>
public static class McpToolCatalogChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var capability = McpToolCatalog.Find("mcp_describe_capabilities");
        var playsets = McpToolCatalog.Find("get_playsets_page");
        var settings = McpToolCatalog.Find("get_settings_page");
        var createPlan = McpToolCatalog.Find("create_change_plan");
        var applyPlan = McpToolCatalog.Find("apply_change_plan");

        ChecksContext.Check("能力发现工具在目录中", capability is not null);
        ChecksContext.Check("播放集只读工具在目录中", playsets is not null);
        ChecksContext.Check("设置只读工具在目录中", settings is not null);
        ChecksContext.Check("创建计划工具在目录中", createPlan is not null);
        ChecksContext.Check("不存在直接执行工具", applyPlan is null);
        ChecksContext.Check("播放集工具要求读取权限", playsets?.RequiredScope == McpPermissionScopes.PlaySetsRead);
        ChecksContext.Check("设置工具要求读取权限", settings?.RequiredScope == McpPermissionScopes.SettingsRead);
        ChecksContext.Check("创建计划工具要求安全写入权限", createPlan?.RequiredScope == McpPermissionScopes.PlaySetsWriteSafe);
        ChecksContext.Check("工具名无重复", McpToolCatalog.Tools.Count == McpToolCatalog.Tools.Select(tool => tool.Name).Distinct().Count());

        return ChecksContext.Failures == before ? 0 : 1;
    }
}
