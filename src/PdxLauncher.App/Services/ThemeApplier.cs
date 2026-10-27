namespace PdxLauncher.App.Services;

using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Themes;
using Windows.UI;
using Windows.UI.Text;

/// <summary>
/// 把纯数据的 <see cref="ThemeSpec"/> 翻译成 XAML 可直接赋值的 UI 对象。
/// 规则（docs/DATA-FORMATS.md §6）：颜色/字体/字号/字重/阴影永远取安装目录主题；
/// hsla 解析复用 Core 的 <see cref="HslaColor"/>，不在这里重复实现。
/// <para>
/// 按钮照官方启动器渲染（app.asar 的 primary-*.css）：
/// <c>button.backgroundImage</c> 铺底、<c>button.color</c> 是文字色、<c>buttonHover.color</c>
/// 是悬停文字色。不要再用「按底色算对比色」那一套 —— 那会把奶油色文字色算成深色字，
/// 再盖到深色底图上，就是黑底黑字。
/// </para>
/// </summary>
public sealed class ThemeApplier
{
    /// <summary>兜底深色背景：主题没有背景图也没有颜色时使用，保证浅色文字可读。</summary>
    public static readonly Color PlaceholderBackground = Color.FromArgb(255, 20, 20, 24);

    /// <summary>兜底按钮文字色（官方默认白字；底色由按钮底图或幽灵底提供）。</summary>
    public static readonly Color PlaceholderButtonTextColor = Color.FromArgb(255, 255, 255, 255);

    /// <summary>没有底图时的「幽灵按钮」底色（白 5%）。</summary>
    private static readonly Color GhostButtonBackground = Color.FromArgb(13, 255, 255, 255);

    /// <summary>没有底图时的幽灵按钮悬停底色（白 12%）。</summary>
    private static readonly Color GhostButtonHoverBackground = Color.FromArgb(31, 255, 255, 255);

    private const string FallbackFontFamily = "Segoe UI";

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly FontLoader _fontLoader;

    public ThemeApplier(IFileSystem fileSystem, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _logger = logger;
        _fontLoader = new FontLoader(fileSystem, logger);
    }

    /// <summary>把主题规格转成视觉对象。任何素材缺失都退回占位，不抛异常。</summary>
    public ThemeVisuals Create(ThemeSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var backgroundImage = TryLoadImage(spec.Background);
        var logoImage = TryLoadImage(spec.Logo);
        var appIcon = TryLoadImage(spec.AppIcon);

        // 官方语义：button.backgroundImage 是底图，button.color 是「文字色」。
        var buttonArt = TryCreateImageBrush(spec.ButtonBackground);
        var usesArt = buttonArt is not null;

        var buttonTextColor = ParseColor(spec.ButtonTextColor, PlaceholderButtonTextColor);
        var buttonHoverTextColor = ParseColor(spec.ButtonHoverTextColor, buttonTextColor);
        var buttonDisabledTextColor = Color.FromArgb(102, buttonTextColor.R, buttonTextColor.G, buttonTextColor.B);

        Brush buttonBackground;
        Brush buttonHoverBackground;
        Brush buttonBorderBrush;
        double buttonBorderThickness;

        if (buttonArt is not null)
        {
            // 有主题底图：底色/悬停底色都是这张图（悬停提亮由页面叠一层白做，见 HomePage）。
            buttonBackground = buttonArt;
            buttonHoverBackground = buttonArt;
            buttonBorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            buttonBorderThickness = 0;
        }
        else
        {
            // 没有底图：幽灵按钮，别退化成「灰底 + 深色字」。
            // 描边留空 —— 官方 pureCSS 按钮的 2px 内嵌高光由 HomePage 的内容层统一画，
            // 这里再加 1px 外描边会和它叠成双层边，看起来比官方的重。
            buttonBackground = new SolidColorBrush(GhostButtonBackground);
            buttonHoverBackground = new SolidColorBrush(GhostButtonHoverBackground);
            buttonBorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            buttonBorderThickness = 0;
        }

        var fontFamily = _fontLoader.Load(spec.ButtonFont) ?? new FontFamily(FallbackFontFamily);

        return new ThemeVisuals(
            backgroundImage,
            new SolidColorBrush(PlaceholderBackground),
            logoImage,
            appIcon,
            usesArt,
            buttonBackground,
            buttonHoverBackground,
            buttonBorderBrush,
            buttonBorderThickness,
            new SolidColorBrush(buttonTextColor),
            new SolidColorBrush(buttonHoverTextColor),
            new SolidColorBrush(buttonDisabledTextColor),
            fontFamily,
            spec.FontSize,
            MapFontWeight(spec.FontWeight),
            spec.TextShadow,
            spec.Source);
    }

    /// <summary>内置占位主题（无任何素材时使用）。</summary>
    public static ThemeSpec CreatePlaceholderSpec() => new(
        Background: null,
        Logo: null,
        AppIcon: null,
        AppTaskbarIcon: null,
        ButtonBackground: null,
        ButtonFont: null,
        ButtonTextColor: "#FFFFFF",
        ButtonHoverTextColor: "#FFFFFF",
        FontSize: 24.0,
        FontWeight: 400,
        TextShadow: null,
        Source: ThemeAssetSource.BuiltInPlaceholder);

    /// <summary>整数字重 → WinUI <see cref="FontWeight"/>；未知值退回 Normal。</summary>
    public static FontWeight MapFontWeight(int weight) => weight switch
    {
        <= 150 => FontWeights.Thin,
        <= 250 => FontWeights.ExtraLight,
        <= 325 => FontWeights.Light,
        <= 375 => FontWeights.SemiLight,
        <= 450 => FontWeights.Normal,
        <= 550 => FontWeights.Medium,
        <= 650 => FontWeights.SemiBold,
        <= 750 => FontWeights.Bold,
        <= 850 => FontWeights.ExtraBold,
        <= 925 => FontWeights.Black,
        _ => FontWeights.ExtraBlack,
    };

    /// <summary>把与框架无关的颜色转成 WinUI 的 Color（A 从 0..1 映射到 0..255）。</summary>
    public static Color ToColor(RgbaColor rgba)
    {
        var alpha = (byte)Math.Clamp(Math.Round(rgba.A * 255.0), 0.0, 255.0);
        return Color.FromArgb(alpha, rgba.R, rgba.G, rgba.B);
    }

    /// <summary>解析主题颜色字符串（hsla/hsl/rgba/rgb/#RRGGBB）；失败用兜底色。</summary>
    public Color ParseColor(string? text, Color fallback)
    {
        if (!HslaColor.TryParse(text, out var rgba))
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                _logger.Warn("主题颜色无法解析，使用兜底色: " + text);
            }

            return fallback;
        }

        return ToColor(rgba);
    }

    /// <summary>
    /// 加载主题图片：位图走 <see cref="BitmapImage"/>，.svg 走 <see cref="SvgImageSource"/>
    /// （CK3 的字标就是 logo.svg，BitmapImage 不支持矢量）。
    /// </summary>
    private ImageSource? TryLoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (!_fileSystem.FileExists(path))
            {
                _logger.Warn("主题素材不存在: " + path);
                return null;
            }

            var uri = new Uri(path);
            return path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? new SvgImageSource { UriSource = uri }
                : new BitmapImage { UriSource = uri };
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException or IOException)
        {
            _logger.Warn("主题素材加载失败: " + path + " (" + ex.Message + ")");
            return null;
        }
    }

    /// <summary>
    /// 底图刷。<c>Stretch</c> 用 <see cref="Stretch.Fill"/> 而不是 UniformToFill：
    /// 按钮按官方比例固定成 232x66（175x50 放大 1.33 倍），Fill 不会把 V3 的金珠边框裁掉；
    /// Stellaris 那张 28x81 的竖切片是纯色，拉伸也无所谓。
    /// </summary>
    private ImageBrush? TryCreateImageBrush(string? path)
    {
        var source = TryLoadImage(path);
        return source is null ? null : new ImageBrush { ImageSource = source, Stretch = Stretch.Fill };
    }
}
