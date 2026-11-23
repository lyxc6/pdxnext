namespace PdxLauncher.Core.Mcp;

/// <summary>访问密钥在设置页显示的安全摘要，绝不包含明文或哈希。</summary>
public sealed record McpAccessKey(
    string Id,
    string DisplayName,
    string KeyPrefix,
    bool Enabled,
    string AllowedTools,
    DateTimeOffset? ExpiresUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastUsedUtc)
{
    public bool IsExpired => ExpiresUtc is { } expiry && expiry <= DateTimeOffset.UtcNow;

    public string StatusText => !Enabled ? "已禁用" : IsExpired ? "已过期" : "已启用";
}

/// <summary>创建或轮换访问密钥后的一次性结果。</summary>
public sealed record McpCreatedAccessKey(McpAccessKey Key, string RawToken);

/// <summary>服务端校验访问密钥后的授权结果。</summary>
public sealed record McpAuthorizedKey(
    string KeyId,
    string DisplayName,
    IReadOnlyList<string> AllowedTools)
{
    public bool AllowsAllTools => AllowedTools.Contains("*", StringComparer.Ordinal);

    public bool Allows(string toolName) =>
        AllowsAllTools
        || AllowedTools.Contains(toolName, StringComparer.Ordinal)
        || (McpPermissionScopes.IsReadOnlyTool(toolName)
            && (AllowsScope(McpPermissionScopes.PlaySetsRead) || AllowsScope(McpPermissionScopes.SettingsRead)));

    public bool AllowsScope(string scope) =>
        AllowsAllTools || AllowedTools.Contains(scope, StringComparer.Ordinal);
}
