namespace PdxLauncher.App.Services;

using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PdxLauncher.Core.Contracts;
using Windows.UI.Text;

/// <summary>
/// <see cref="ThemeApplier"/> 的产物：全部是 XAML 可直接赋值的 UI 对象。
/// 只在 App 层流通，绝不进入 ViewModel。
/// <para>
/// 按钮语义照官方启动器：<c>button.backgroundImage</c> 是底图，
/// <c>button.color</c> / <c>buttonHover.color</c> 是「文字色」而不是底色。
/// 没有底图的游戏走「幽灵按钮」分支（半透明白底 + 1px 白描边 + 白字）。
/// </para>
/// </summary>
public sealed record ThemeVisuals(
    ImageSource? BackgroundImage,
    Brush BackgroundBrush,
    ImageSource? LogoImage,
    ImageSource? AppIcon,
    bool ButtonUsesArt,
    Brush ButtonBackground,
    Brush ButtonHoverBackground,
    Brush ButtonBorderBrush,
    double ButtonBorderThickness,
    Brush ButtonForeground,
    Brush ButtonHoverForeground,
    Brush ButtonDisabledForeground,
    FontFamily ButtonFontFamily,
    double ButtonFontSize,
    FontWeight ButtonFontWeight,
    string? TextShadow,
    ThemeAssetSource Source);
