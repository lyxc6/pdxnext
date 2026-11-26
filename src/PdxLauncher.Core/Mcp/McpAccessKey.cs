namespace PdxLauncher.Core.Mcp;

/// <summary>访问密钥在设置页显示的安全摘要，绝不包含明文或哈希。</summary>
public sealed record McpAccessKey(
    string Id,
    string DisplayName,
    string KeyPrefix,
    bool Enabled,
    string Capabilities,
    DateTimeOffset? ExpiresUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastUsedUtc)
{
    public bool IsExpired => ExpiresUtc is { } expiry && expiry <= DateTimeOffset.UtcNow;

    public string StatusText => !Enabled ? "已禁用" : IsExpired ? "已过期" : "已启用";

    /// <summary>把库里逗号分隔的能力值拆成列表并归一化旧作用域值。</summary>
    public IReadOnlyList<string> CapabilityList =>
        McpCapabilityCatalog.Normalize(
            Capabilities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public bool AllowsAllCapabilities => CapabilityList.Count == McpCapabilityCatalog.All.Count;
}

/// <summary>创建或轮换访问密钥后的一次性结果。</summary>
public sealed record McpCreatedAccessKey(McpAccessKey Key, string RawToken);

/// <summary>服务端校验访问密钥后的授权结果。</summary>
public sealed record McpAuthorizedKey(
    string KeyId,
    string DisplayName,
    IReadOnlyList<string> Capabilities)
{
    public bool AllowsAllCapabilities => Capabilities.Count == McpCapabilityCatalog.All.Count;

    /// <summary>按能力判定工具；变更计划再按 domain 细分，防止跨域提权。</summary>
    public bool Allows(string toolName, string? domain = null) =>
        Capabilities.Any(capability => McpCapabilityCatalog.AllowsTool(capability, toolName, domain));

    public bool AllowsCapability(string capability) =>
        Capabilities.Contains(capability, StringComparer.Ordinal);
}

/// <summary>
/// MCP 进程的访问状态。必须区分「没配置密钥」与「配置了但校验失败」：
/// 前者是兼容模式，后者必须拒绝全部工具调用，否则访问密钥形同虚设。
/// </summary>
public enum McpAccessState
{
    /// <summary>兼容模式：没有配置访问密钥，沿用全局开关。</summary>
    Legacy,

    /// <summary>密钥有效，按密钥的工具范围过滤。</summary>
    Authorized,

    /// <summary>传了密钥但不匹配 / 已禁用 / 已过期，必须拒绝。</summary>
    Rejected,
}

/// <summary>按「是否传入密钥 + 校验结果」推导访问状态。</summary>
public static class McpAccessStatePolicy
{
    public static McpAccessState Resolve(bool accessKeySupplied, McpAuthorizedKey? authorizedKey)
    {
        if (!accessKeySupplied)
        {
            return McpAccessState.Legacy;
        }

        return authorizedKey is null ? McpAccessState.Rejected : McpAccessState.Authorized;
    }
}

/// <summary>兼容模式（未传密钥）的授权：只有读取能力，写入必须由密钥显式授予。</summary>
public static class McpLegacyAuthorization
{
    public static readonly McpAuthorizedKey Instance = new(
        "legacy",
        "兼容模式",
        McpCapabilityCatalog.ReadOnly);
}
