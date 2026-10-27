namespace PdxLauncher.App.Services;

using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using PdxLauncher.Core.Themes;

/// <summary>
/// 给窗口的系统 caption 按钮（─ □ ✕）上色。
/// AppWindowTitleBar 的按钮颜色属性默认为「浅色主题」值：前景纯黑、失焦 #606060、
/// 悬停白底。深色标题栏上聚焦态就成了纯黑不可见，而失焦态还剩一点灰——
/// 用户看到的「聚焦反而看不清 / 左右明暗反相」就是这么来的。
/// 这里按 <see cref="TitleBarPalette"/> 显式给出四个状态的前景与两种悬停底色。
/// </summary>
public static class TitleBarTheme
{
    /// <summary>
    /// 应用一套按钮配色。返回 false 表示当前环境不支持自定义
    /// （Win10 的 AppWindowTitleBar 不允许改按钮色）或写入失败；
    /// 此时保持系统默认，不抛异常。
    /// </summary>
    public static bool Apply(AppWindow? appWindow, ElementTheme theme)
    {
        if (appWindow is null)
        {
            return false;
        }

        try
        {
            // Win10 上 IsCustomizationSupported() 为 false，设置颜色会被忽略甚至抛异常。
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return false;
            }

            var palette = TitleBarPalette.For(theme == ElementTheme.Dark);
            var titleBar = appWindow.TitleBar;

            // 高对比度由系统按无障碍主题配色，写死前景色反而会让按钮在纯黑/纯白主题上消失。
            if (IsHighContrast())
            {
                ResetToSystemDefaults(titleBar);
                return false;
            }

            // 底色保持透明：标题栏区域画的是应用自己的背景图与压暗层。
            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.InactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            titleBar.ForegroundColor = ThemeApplier.ToColor(palette.Foreground);
            titleBar.InactiveForegroundColor = ThemeApplier.ToColor(palette.ForegroundInactive);

            // 三个按钮共用一套悬停/按下配色：API 没有「只给关闭键」的入口，
            // 于是关闭键不再有原生红色悬停，改为统一的淡白/淡黑高亮。
            titleBar.ButtonForegroundColor = ThemeApplier.ToColor(palette.Foreground);
            titleBar.ButtonInactiveForegroundColor = ThemeApplier.ToColor(palette.ForegroundInactive);
            titleBar.ButtonHoverForegroundColor = ThemeApplier.ToColor(palette.ForegroundHover);
            titleBar.ButtonHoverBackgroundColor = ThemeApplier.ToColor(palette.BackgroundHover);
            titleBar.ButtonPressedForegroundColor = ThemeApplier.ToColor(palette.ForegroundPressed);
            titleBar.ButtonPressedBackgroundColor = ThemeApplier.ToColor(palette.BackgroundPressed);

            return true;
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("设置标题栏按钮配色失败: " + ex.Message);
            }

            return false;
        }
    }

    /// <summary>把全部标题栏颜色写回 null，交还系统按当前（含高对比度）主题自行绘制。</summary>
    private static void ResetToSystemDefaults(AppWindowTitleBar titleBar)
    {
        titleBar.BackgroundColor = null;
        titleBar.InactiveBackgroundColor = null;
        titleBar.ButtonBackgroundColor = null;
        titleBar.ButtonInactiveBackgroundColor = null;
        titleBar.ForegroundColor = null;
        titleBar.InactiveForegroundColor = null;
        titleBar.ButtonForegroundColor = null;
        titleBar.ButtonInactiveForegroundColor = null;
        titleBar.ButtonHoverForegroundColor = null;
        titleBar.ButtonHoverBackgroundColor = null;
        titleBar.ButtonPressedForegroundColor = null;
        titleBar.ButtonPressedBackgroundColor = null;
    }

    /// <summary>读系统「高对比度」开关（SPI_GETHIGHCONTRAST），不依赖 UWP 的 AccessibilitySettings。</summary>
    private static bool IsHighContrast()
    {
        var info = new HighContrastInfo { cbSize = (uint)Marshal.SizeOf<HighContrastInfo>() };
        if (!SystemParametersInfo(SpiGetHighContrast, info.cbSize, ref info, 0))
        {
            return false;
        }

        return (info.dwFlags & HighContrastOn) != 0;
    }

    private const uint SpiGetHighContrast = 0x0042;
    private const uint HighContrastOn = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrastInfo
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr lpszDefaultScheme;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref HighContrastInfo pvParam, uint fWinIni);
}
