namespace PdxLauncher.Core.Mcp;

/// <summary>作用域到工具的授权映射。</summary>
public static class McpScopeToolPolicy
{
    private static readonly HashSet<string> WriteTools = new(StringComparer.Ordinal)
    {
        "save_upload_draft",
        "clear_upload_draft",
        "create_change_plan",
    };

    /// <summary>判断作用域是否允许调用指定工具。</summary>
    public static bool Allows(string scope, string toolName)
    {
        if (string.Equals(scope, "*", StringComparison.Ordinal))
        {
            return true;
        }

        var descriptor = McpToolCatalog.Find(toolName);
        if (descriptor is null)
        {
            return false;
        }

        if (WriteTools.Contains(toolName))
        {
            return string.Equals(descriptor.RequiredScope, scope, StringComparison.Ordinal);
        }

        return descriptor.Risk == McpToolRisk.ReadOnly
            || string.Equals(descriptor.RequiredScope, scope, StringComparison.Ordinal);
    }
}
