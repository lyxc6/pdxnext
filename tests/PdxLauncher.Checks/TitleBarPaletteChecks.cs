namespace PdxLauncher.Checks;

using System.Globalization;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Themes;

/// <summary>
/// 标题栏 caption 按钮配色自检。
/// AppWindowTitleBar 的按钮颜色默认是「浅色主题」值（前景纯黑），深色标题栏上聚焦态
/// 会把 ─ □ ✕ 画成不可见的黑块——这一条用对比度断言钉死，防止以后又退回去。
/// </summary>
public static class TitleBarPaletteChecks
{
    /// <summary>深色窗口里标题栏区域实测近似底色（背景图 + 顶部压暗层）。</summary>
    private static readonly RgbaColor DarkTitleBar = new(0x01, 0x09, 0x0E, 1.0);

    /// <summary>浅色主题的标题栏底色。</summary>
    private static readonly RgbaColor LightTitleBar = new(0xF3, 0xF3, 0xF3, 1.0);

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var dark = TitleBarPalette.For(true);
        var light = TitleBarPalette.For(false);

        ChecksContext.Check("深色主题取 Dark 配色", ReferenceEquals(TitleBarPalette.Dark, dark));
        ChecksContext.Check("浅色主题取 Light 配色", ReferenceEquals(TitleBarPalette.Light, light));

        // 深色标题栏：三个按钮必须是亮的不透明色，否则重演「聚焦态纯黑不可见」。
        var darkForeground = Contrast(dark.Foreground, DarkTitleBar);
        var darkInactive = Contrast(dark.ForegroundInactive, DarkTitleBar);
        var darkPressed = Contrast(dark.ForegroundPressed, DarkTitleBar);
        ChecksContext.Check(
            "深色标题栏前景为不透明白",
            IsOpaque(dark.Foreground) && dark.Foreground.R == 255 && dark.Foreground.G == 255 && dark.Foreground.B == 255,
            dark.Foreground.ToString());
        ChecksContext.Check("深色标题栏前景对比度 ≥ 7:1", darkForeground >= 7.0, Ratio(darkForeground));
        ChecksContext.Check("深色标题栏失焦前景为不透明", IsOpaque(dark.ForegroundInactive), dark.ForegroundInactive.ToString());
        ChecksContext.Check("深色标题栏失焦前景对比度 ≥ 4.5:1", darkInactive >= 4.5, Ratio(darkInactive));
        ChecksContext.Check("深色标题栏按下前景对比度 ≥ 4.5:1", darkPressed >= 4.5, Ratio(darkPressed));

        // 浅色标题栏：反过来，必须是暗色。
        var lightForeground = Contrast(light.Foreground, LightTitleBar);
        var lightInactive = Contrast(light.ForegroundInactive, LightTitleBar);
        ChecksContext.Check(
            "浅色标题栏前景为不透明黑",
            IsOpaque(light.Foreground) && light.Foreground.R == 0 && light.Foreground.G == 0 && light.Foreground.B == 0,
            light.Foreground.ToString());
        ChecksContext.Check("浅色标题栏前景对比度 ≥ 7:1", lightForeground >= 7.0, Ratio(lightForeground));
        ChecksContext.Check("浅色标题栏失焦前景对比度 ≥ 4.5:1", lightInactive >= 4.5, Ratio(lightInactive));

        // 悬停/按下底色：半透明淡到不遮住背景图，但必须非零（要看得见反馈）。
        ChecksContext.Check("深色悬停底色为半透明高亮", IsSubtleOverlay(dark.BackgroundHover), dark.BackgroundHover.ToString());
        ChecksContext.Check("深色按下底色为半透明高亮", IsSubtleOverlay(dark.BackgroundPressed), dark.BackgroundPressed.ToString());
        ChecksContext.Check("浅色悬停底色为半透明高亮", IsSubtleOverlay(light.BackgroundHover), light.BackgroundHover.ToString());
        ChecksContext.Check("浅色按下底色为半透明高亮", IsSubtleOverlay(light.BackgroundPressed), light.BackgroundPressed.ToString());

        // 悬停与按下要给不同的强度，否则按下去没有反馈。
        ChecksContext.Check("深色按下比悬停更明显", dark.BackgroundPressed.A > dark.BackgroundHover.A);
        ChecksContext.Check("浅色按下比悬停更明显", light.BackgroundPressed.A > light.BackgroundHover.A);

        // 两种主题不能混用同一前景色。
        ChecksContext.Check("深浅主题前景色不同", dark.Foreground != light.Foreground);

        return ChecksContext.Failures - before;
    }

    /// <summary>半透明、且不浓到遮住背景图：alpha ∈ (0, 0.15]。</summary>
    private static bool IsSubtleOverlay(RgbaColor color) => color.A > 0.0 && color.A <= 0.15;

    private static bool IsOpaque(RgbaColor color) => color.A >= 1.0;

    /// <summary>WCAG 对比度：(亮 + 0.05) / (暗 + 0.05)。</summary>
    private static double Contrast(RgbaColor foreground, RgbaColor background)
    {
        var first = RelativeLuminance(foreground);
        var second = RelativeLuminance(background);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(RgbaColor color)
    {
        static double Channel(byte value)
        {
            var scaled = value / 255.0;
            return scaled <= 0.03928 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static string Ratio(double value) => value.ToString("0.00", CultureInfo.InvariantCulture) + ":1";
}
