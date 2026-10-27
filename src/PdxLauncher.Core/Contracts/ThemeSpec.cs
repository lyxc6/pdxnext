namespace PdxLauncher.Core.Contracts;

/// <summary>
/// 单款游戏的主题描述。图片/字体字段为绝对本地路径（已解析完成），
/// 字号/字重/阴影永远取自安装目录的 theme-settings.json。
/// <para>
/// 语义提醒：官方 theme-settings.json 的 <c>button.color</c> 是「按钮上的文字颜色」
/// （官方 schema 原文 "Color of font on element"），不是按钮底色；底色只由
/// <see cref="ButtonBackground"/> 这张图提供。改名就是为了不再把它当底色用。
/// </para>
/// </summary>
public sealed record ThemeSpec(
    string? Background,
    string? Logo,
    string? AppIcon,
    string? AppTaskbarIcon,
    string? ButtonBackground,
    string? ButtonFont,
    string ButtonTextColor,
    string ButtonHoverTextColor,
    double FontSize,
    int FontWeight,
    string? TextShadow,
    ThemeAssetSource Source);
