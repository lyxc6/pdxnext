namespace PdxLauncher.Core.Contracts;

/// <summary>GetMetadata GraphQL 返回的单款游戏元数据（仅保留本产品用到的字段）。</summary>
public sealed record GameMetadataDto(
    string Id,
    string? Name,
    string? ExePath,
    IReadOnlyList<string> ExeArgs,
    string? DlcPath,
    string? BrowserDlcUrl,
    string? BrowserModUrl,
    string? FormatVersion,
    ThemeSettingsDto? ThemeSettings);
