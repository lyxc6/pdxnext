namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 主题刷新范围自检：设置页「立即检查」只碰用户选的那一款，后台刷新仍按
/// 「指定款 / 当前游戏优先 → 其余按发现顺序」排全量。
/// <para>
/// 这组断言存在的原因是一次真机实测：点「立即检查」后 16 款游戏串行下载主题，
/// 按钮看起来像卡死两分多钟。范围判定错了不会崩，只会让这个卡顿悄悄回来。
/// </para>
/// </summary>
public static class ThemeRefreshScopeChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var games = new[]
        {
            Create("victoria3", "Victoria 3"),
            Create("ck3", "Crusader Kings III"),
            Create("stellaris", "Stellaris"),
        };

        CheckOnlyGame(games);
        CheckOnlyGameMissing(games);
        CheckOnlyGameWithoutTarget(games);
        CheckBackgroundOrder(games);
        CheckNoDuplicateWhenOnlyIsCurrent(games);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckOnlyGame(IReadOnlyList<GameInstallation> games)
    {
        var result = ThemeRefreshScope.Order(games, games[0], games[1], onlyThatGame: true);
        ChecksContext.Equal("只刷指定游戏: 数量", 1, result.Count);
        ChecksContext.Equal("只刷指定游戏: gameId", "victoria3", result[0].GameId);
        ChecksContext.Equal(
            "只刷指定游戏: 用发现列表里的实例",
            "Victoria 3",
            result[0].DisplayName);

        var caseInsensitive = ThemeRefreshScope.Order(
            games,
            Create("CK3", "Crusader Kings III"),
            games[0],
            onlyThatGame: true);
        ChecksContext.Equal("只刷指定游戏: gameId 大小写不敏感", "ck3", caseInsensitive[0].GameId);
    }

    private static void CheckOnlyGameMissing(IReadOnlyList<GameInstallation> games)
    {
        var external = Create("eu4", "Europa Universalis IV");
        var result = ThemeRefreshScope.Order(games, external, games[0], onlyThatGame: true);
        ChecksContext.Equal("只刷指定游戏: 未发现时仍只返回这一款", 1, result.Count);
        ChecksContext.Equal("只刷指定游戏: 未发现时保留传入实例", "eu4", result[0].GameId);
    }

    private static void CheckOnlyGameWithoutTarget(IReadOnlyList<GameInstallation> games)
    {
        var result = ThemeRefreshScope.Order(games, only: null, current: games[0], onlyThatGame: true);
        ChecksContext.Equal("只刷指定游戏: 没指定时不刷任何游戏", 0, result.Count);
    }

    private static void CheckBackgroundOrder(IReadOnlyList<GameInstallation> games)
    {
        var result = ThemeRefreshScope.Order(games, games[1], games[2], onlyThatGame: false);
        ChecksContext.Equal("后台刷新: 全量不丢", 3, result.Count);
        ChecksContext.Equal("后台刷新: 指定款排第一", "ck3", result[0].GameId);
        ChecksContext.Equal("后台刷新: 当前游戏排第二", "stellaris", result[1].GameId);
        ChecksContext.Equal("后台刷新: 其余保持发现顺序", "victoria3", result[2].GameId);
    }

    private static void CheckNoDuplicateWhenOnlyIsCurrent(IReadOnlyList<GameInstallation> games)
    {
        var result = ThemeRefreshScope.Order(games, games[0], games[0], onlyThatGame: false);
        ChecksContext.Equal("后台刷新: 指定款与当前游戏相同时不重复", 3, result.Count);
        ChecksContext.Equal("后台刷新: 相同游戏只出现一次", "victoria3", result[0].GameId);
    }

    private static GameInstallation Create(string gameId, string displayName) =>
        new(
            GameId: gameId,
            DisplayName: displayName,
            Version: "test",
            InstallRoot: @"D:\Games\" + gameId,
            LauncherSettingsPath: @"D:\Games\" + gameId + @"\launcher-settings.json",
            Format: LoadFileFormat.ContentLoadV1,
            GameDataDir: @"D:\Games\" + gameId + @"\game",
            DlcPath: @"D:\Games\" + gameId + @"\dlc",
            ModDir: @"D:\Games\" + gameId + @"\mod",
            ExePath: @"D:\Games\" + gameId + @"\binaries\game.exe",
            ExeArgs: Array.Empty<string>(),
            Alternatives: Array.Empty<AlternativeExecutable>(),
            Theme: new ThemeSpec(
                Background: null,
                Logo: null,
                AppIcon: null,
                AppTaskbarIcon: null,
                ButtonBackground: null,
                ButtonFont: null,
                ButtonTextColor: "#FFFFFF",
                ButtonHoverTextColor: "#FFFFFF",
                FontSize: 14,
                FontWeight: 400,
                TextShadow: null,
                Source: ThemeAssetSource.BuiltInPlaceholder),
            BrowserDlcUrl: null,
            BrowserModUrl: null,
            SteamAppId: null,
            Support: GameSupport.Full);
}
