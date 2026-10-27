namespace PdxLauncher.Core.Themes;

/// <summary>
/// 系统标题栏 caption 按钮（最小化 / 最大化 / 关闭）的一套配色。
/// AppWindowTitleBar 的颜色属性默认取「浅色主题」值——前景纯黑，
/// 在深色标题栏上聚焦态会把三个按钮画成不可见的黑块（实测：未聚焦 #606060 可见、
/// 聚焦 #000000 全黑）。因此由这里按主题给出显式配色，App 层负责写进窗口。
/// </summary>
/// <param name="Foreground">常规前景（未悬停、未按下）。</param>
/// <param name="ForegroundInactive">窗口失焦时的前景。</param>
/// <param name="ForegroundHover">悬停时的前景。</param>
/// <param name="ForegroundPressed">按下时的前景。</param>
/// <param name="BackgroundHover">悬停时的按钮底色（半透明，保留背景图）。</param>
/// <param name="BackgroundPressed">按下时的按钮底色。</param>
public sealed record TitleBarPalette(
    RgbaColor Foreground,
    RgbaColor ForegroundInactive,
    RgbaColor ForegroundHover,
    RgbaColor ForegroundPressed,
    RgbaColor BackgroundHover,
    RgbaColor BackgroundPressed)
{
    /// <summary>深色主题：白色 glyph；失焦用 #8C8C8C（与左侧标题变暗后的亮度一致）。</summary>
    public static TitleBarPalette Dark { get; } = new(
        RgbaColor.White,
        new RgbaColor(0x8C, 0x8C, 0x8C, 1.0),
        RgbaColor.White,
        new RgbaColor(0xB3, 0xB3, 0xB3, 1.0),
        new RgbaColor(0xFF, 0xFF, 0xFF, 0.06),
        new RgbaColor(0xFF, 0xFF, 0xFF, 0.09));

    /// <summary>浅色主题：黑色 glyph；失焦 #606060 即系统原生浅色非活动值。</summary>
    public static TitleBarPalette Light { get; } = new(
        RgbaColor.Black,
        new RgbaColor(0x60, 0x60, 0x60, 1.0),
        RgbaColor.Black,
        new RgbaColor(0x4D, 0x4D, 0x4D, 1.0),
        new RgbaColor(0x00, 0x00, 0x00, 0.06),
        new RgbaColor(0x00, 0x00, 0x00, 0.09));

    /// <summary>按当前主题取配色。</summary>
    public static TitleBarPalette For(bool isDarkTheme) => isDarkTheme ? Dark : Light;
}
