namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;

/// <summary>
/// 随程序发布的自带游戏美术。
/// <para>
/// 不再只认 EU5：任何游戏只要安装包带了 <c>Assets\themes\&lt;gameId&gt;\background.jpg</c>，
/// 就把它当作「层 2.5」的背景交给主题解析。这样服务端没有记录、安装目录也没有主题素材的游戏
/// 不会掉进纯色底，同时内置素材可以随安装包一起更新（不必等客户端代码发版）。
/// </para>
/// <para>
/// 文件位置：<c>&lt;程序目录&gt;\Assets\themes\&lt;gameId&gt;\background.jpg</c>；
/// 缺失或 gameId 非法时返回 null（调用方回落到内置占位图），绝不抛异常。
/// </para>
/// </summary>
internal sealed class BundledThemes : IBundledThemeSource
{
    private const string AssetsFolderName = "Assets";
    private const string ThemeFolderName = "themes";
    private const string BackgroundFileName = "background.jpg";

    /// <inheritdoc />
    public ThemeSpec? TryResolve(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (!IsSafeGameId(game.GameId))
        {
            return null;
        }

        var background = Path.Combine(
            AppContext.BaseDirectory,
            AssetsFolderName,
            ThemeFolderName,
            game.GameId,
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

    /// <summary>gameId 来自本地游戏库但仍是外部输入：禁止路径分隔符与上跳目录。</summary>
    private static bool IsSafeGameId(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        if (gameId.Contains("..", StringComparison.Ordinal)
            || gameId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        return gameId is not "." and not "..";
    }
}
