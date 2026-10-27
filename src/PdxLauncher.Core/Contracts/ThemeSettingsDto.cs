namespace PdxLauncher.Core.Contracts;

/// <summary>themeSettings 节点。图片类字段是远程 URL；文字色/字号为可选的本地覆盖值。</summary>
public sealed record ThemeSettingsDto(
    string? AppIcon,
    string? AppTaskbarIcon,
    string? Background,
    string? Logo,
    string? ButtonBackgroundImage,
    string? ButtonFontPath,
    string? ButtonTextColor,
    string? ButtonHoverTextColor,
    double? FontSize,
    int? FontWeight,
    string? TextShadow);
