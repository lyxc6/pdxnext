namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;

/// <summary>
/// 随程序发布的自带游戏美术。
/// <para>
/// 目前只有 EU5：它的安装目录里没有 launcher-settings.json，也没有任何主题素材，
/// 官方美术只在 Steam CDN 上（<c>library_hero_2x.jpg</c> 是唯一能拿到的原图，
/// <c>library_600x900 / logo.png / header.jpg</c> 全部 404）。
/// 所以把合成好的背景图随程序发布，主题解析时当「层 2.5」用。
/// </para>
/// <para>
/// 文件位置：<c>&lt;程序目录&gt;\Assets\themes\&lt;gameId&gt;\background.jpg</c>；
/// 缺失时返回 null（调用方回落到内置占位图），绝不抛异常。
/// </para>
/// </summary>
internal sealed class BundledThemes : IBundledThemeSource
{
    private const string AssetsFolderName = "Assets";
    private const string ThemeFolderName = "themes";
    private const string BackgroundFileName = "background.jpg";
    private const string Eu5GameId = "eu5";

    /// <inheritdoc />
    public ThemeSpec? TryResolve(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (!string.Equals(game.GameId, Eu5GameId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var background = Path.Combine(
            AppContext.BaseDirectory,
            AssetsFolderName,
            ThemeFolderName,
            Eu5GameId,
            BackgroundFileName);

        if (!File.Exists(background))
        {
            return null;
        }

        return new ThemeSpec(
            background,
            Logo: null,
            AppIcon: null,
            AppTaskbarIcon: null,
            ButtonBackground: null,
            ButtonFont: null,
            ThemeResolver.ButtonTextColorFallback,
            ThemeResolver.ButtonHoverTextColorFallback,
            ThemeResolver.FontSizeFallback,
            ThemeResolver.FontWeightFallback,
            TextShadow: null,
            ThemeAssetSource.Bundled);
    }
}
