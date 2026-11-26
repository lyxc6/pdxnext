namespace PdxLauncher.App.ViewModels;

/// <summary>
/// 设置 → MCP → 访问密钥子页的导航参数。KeyId 为 null 表示新建；
/// 编辑时只带摘要，子页自己从数据库读能力，避免把一次性明文带过页。
/// </summary>
public sealed record McpKeyEditorNavigationRequest(string? KeyId, string DisplayName, string? KeyPrefix);

/// <summary>子页返回设置页时携带的结果；CreatedToken 只用于回显一次性明文。</summary>
public sealed record McpKeyEditorNavigationResult(
    bool Saved,
    string? KeyId,
    string? CreatedToken,
    string? ErrorMessage);
