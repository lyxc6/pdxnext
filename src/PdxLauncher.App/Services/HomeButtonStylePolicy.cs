namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 主页「继续 / 开始游戏」按钮的外观判据：什么时候该用 WinUI 原生按钮样式。
/// <para>
/// 只有真正带主题美术的来源（安装目录 / 联网缓存 / Steam 胶囊图）才保留启动器自定义皮肤；
/// 内置占位图与随程序发布的自带美术（目前只有 EU5）没有官方按钮素材，继续画幽灵皮肤
/// 反而和系统其它按钮不是一套，所以改成原生样式。
/// </para>
/// <para>
/// 判据必须按 <see cref="ThemeAssetSource"/>，<b>不能</b>按 <c>ThemeVisuals.ButtonUsesArt</c>：
/// 安装目录主题也可能没有 <c>button.backgroundImage</c>（走幽灵皮肤），但它仍然是「官方主题」，
/// 不能因为缺一张按钮底图就跳成原生样式。
/// </para>
/// <para>
/// 这个类刻意不引用任何 XAML 类型：检查工程把源码链接进去直测判据，而不是扫 HomePage 的字符串。
/// </para>
/// </summary>
internal static class HomeButtonStylePolicy
{
    /// <summary>该来源的主题是否应该让主页按钮使用 WinUI 原生样式。</summary>
    public static bool UsesNativeStyle(ThemeAssetSource source) => source is
        ThemeAssetSource.BuiltInPlaceholder or ThemeAssetSource.Bundled;
}
