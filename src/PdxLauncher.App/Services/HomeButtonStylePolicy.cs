namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Contracts;

/// <summary>主页「继续 / 开始游戏」按钮该用哪套外观。</summary>
internal enum HomeButtonStyleKind
{
    /// <summary>保留启动器自定义皮肤（底图 / 主题字色）。</summary>
    Themed,

    /// <summary>WinUI 原生 DefaultButtonStyle（次要按钮「继续」）。</summary>
    NativeDefault,

    /// <summary>WinUI 原生 AccentButtonStyle（主要按钮「开始游戏」）。</summary>
    NativeAccent,
}

/// <summary>
/// 主页「继续 / 开始游戏」按钮的外观判据：什么时候该用 WinUI 原生按钮样式。
/// <para>
/// 只有真正带主题美术的来源（安装目录 / 联网缓存）才保留启动器自定义皮肤；
/// 内置占位图、随程序发布的自带美术（目前只有 EU5）以及 Steam 库页背景没有官方按钮素材，
/// 继续画幽灵皮肤反而和系统其它按钮不是一套，所以改成原生样式。
/// </para>
/// <para>
/// 判据必须按 <see cref="ThemeAssetSource"/>，<b>不能</b>按 <c>ThemeVisuals.ButtonUsesArt</c>：
/// 安装目录主题也可能没有 <c>button.backgroundImage</c>（走幽灵皮肤），但它仍然是「官方主题」，
/// 不能因为缺一张按钮底图就跳成原生样式。
/// </para>
/// <para>
/// <see cref="ThemeAssetSource.SteamHero"/> 必须算原生：它只从 Steam 库页取一张背景
/// （<c>library_hero.jpg</c>），同样没有按钮素材。EU5 一旦联网就会先拿到 hero 背景，
/// 之后随包素材因「已有背景」提前返回，最终来源停在 <c>SteamHero</c>；若这里不认它，
/// EU5 联网后就会退回幽灵皮肤，正好是本次要消除的形态。离线但有 hero 缓存时同理。
/// </para>
/// <para>
/// 这个类刻意不引用任何 XAML 类型：检查工程把源码链接进去直测决策表，而不是扫 HomePage 的字符串。
/// </para>
/// </summary>
internal static class HomeButtonStylePolicy
{
    /// <summary>原生 Default 样式在 <c>Application.Current.Resources</c> 里的键。</summary>
    public const string DefaultStyleKey = "DefaultButtonStyle";

    /// <summary>原生 Accent 样式在 <c>Application.Current.Resources</c> 里的键。</summary>
    public const string AccentStyleKey = "AccentButtonStyle";

    /// <summary>
    /// 该来源 + 该按钮位置应该用哪套外观。<paramref name="isPrimary"/> 为 true 时是「开始游戏」
    /// （原生 Accent），false 时是「继续」（原生 Default）；主题皮肤不分主次。
    /// </summary>
    public static HomeButtonStyleKind ResolveStyle(ThemeAssetSource source, bool isPrimary)
    {
        if (!UsesNativeStyle(source))
        {
            return HomeButtonStyleKind.Themed;
        }

        return isPrimary ? HomeButtonStyleKind.NativeAccent : HomeButtonStyleKind.NativeDefault;
    }

    /// <summary>原生外观对应的样式资源键；只有真的要走原生时才允许调用。</summary>
    public static string StyleKey(HomeButtonStyleKind style) => style switch
    {
        HomeButtonStyleKind.NativeDefault => DefaultStyleKey,
        HomeButtonStyleKind.NativeAccent => AccentStyleKey,
        _ => throw new ArgumentOutOfRangeException(
            nameof(style),
            style,
            "主题皮肤没有对应的原生按钮样式"),
    };

    /// <summary>该来源的主题是否应该让主页按钮使用 WinUI 原生样式。</summary>
    private static bool UsesNativeStyle(ThemeAssetSource source) => source is
        ThemeAssetSource.BuiltInPlaceholder or ThemeAssetSource.Bundled or ThemeAssetSource.SteamHero;
}
