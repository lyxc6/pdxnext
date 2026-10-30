namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 「启动时选哪个游戏」自检：记忆上次选中的游戏（settings.json 里的 lastGameId /
/// lastGameInstallRoot），命中不了再退回可管理内容、可支持、第一个。
/// 不联网、不读写真实配置目录。
/// </summary>
public static class GameSelectionChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var games = new[]
        {
            Create("victoria3", @"D:\Games\Victoria 3", GameSupport.Full),
            Create("ck3", @"D:\Games\CK3", GameSupport.Full),
            Create("prison_architect", @"D:\Games\Prison Architect", GameSupport.LaunchOnly),
        };

        CheckEmpty();
        CheckExactMatch(games);
        CheckInstallRootOnly(games);
        CheckGameIdOnly(games);
        CheckFallbacks(games);
        CheckCaseInsensitive(games);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckEmpty()
    {
        ChecksContext.Check(
            "空列表返回 null",
            GameSelection.Select(Array.Empty<GameInstallation>(), "ck3", @"D:\Games\CK3") is null);
    }

    private static void CheckExactMatch(GameInstallation[] games)
    {
        ChecksContext.Equal(
            "记忆命中（gameId + installRoot）",
            "ck3",
            GameSelection.Select(games, "ck3", @"D:\Games\CK3")?.GameId);
    }

    private static void CheckInstallRootOnly(GameInstallation[] games)
    {
        // 游戏换了安装盘：gameId 还在，仍应选它。
        ChecksContext.Equal(
            "只剩 gameId 命中（安装位置变了）",
            "ck3",
            GameSelection.Select(games, "ck3", @"E:\Moved\CK3")?.GameId);
    }

    private static void CheckGameIdOnly(GameInstallation[] games)
    {
        ChecksContext.Equal(
            "只有安装路径命中（gameId 拼写不同）",
            "victoria3",
            GameSelection.Select(games, "unknown", @"D:\Games\Victoria 3")?.GameId);
    }

    private static void CheckFallbacks(GameInstallation[] games)
    {
        ChecksContext.Equal(
            "记忆缺失时选第一个可管理内容的游戏",
            "victoria3",
            GameSelection.Select(games, null, null)?.GameId);

        ChecksContext.Equal(
            "记忆里的游戏已经卸载 → 退回默认",
            "victoria3",
            GameSelection.Select(games, "stellaris", @"D:\Games\Stellaris")?.GameId);

        var onlyLaunch = new[]
        {
            Create("prison_architect", @"D:\Games\Prison Architect", GameSupport.LaunchOnly),
            Create("ck3", @"D:\Games\CK3", GameSupport.Full),
        };

        ChecksContext.Equal(
            "没有记忆时优先可管理内容的游戏（而非列表第一个）",
            "ck3",
            GameSelection.Select(onlyLaunch, null, null)?.GameId);

        var unsupportedFirst = new[]
        {
            Create("unknown", @"D:\Games\Unknown", GameSupport.Unsupported),
            Create("prison_architect", @"D:\Games\Prison Architect", GameSupport.LaunchOnly),
        };

        ChecksContext.Equal(
            "全都不支持内容管理时选第一个可支持的游戏",
            "prison_architect",
            GameSelection.Select(unsupportedFirst, null, null)?.GameId);

        var allUnsupported = new[]
        {
            Create("a", @"D:\Games\A", GameSupport.Unsupported),
            Create("b", @"D:\Games\B", GameSupport.Unsupported),
        };

        ChecksContext.Equal(
            "全部不支持时退回列表第一个",
            "a",
            GameSelection.Select(allUnsupported, null, null)?.GameId);
    }

    private static void CheckCaseInsensitive(GameInstallation[] games)
    {
        ChecksContext.Equal(
            "gameId 比较忽略大小写",
            "ck3",
            GameSelection.Select(games, "CK3", null)?.GameId);

        ChecksContext.Equal(
            "空串记忆项不参与匹配",
            "victoria3",
            GameSelection.Select(games, string.Empty, string.Empty)?.GameId);

        // 老设置里存的是官方启动器的原始 id（CS1 = cities_v2），安装项上已经是正名。
        var withCs1 = new[]
        {
            Create("ck3", @"D:\\Games\\CK3", GameSupport.Full),
            Create("cities_skylines", @"D:\\Games\\Cities Skylines", GameSupport.LaunchOnly),
        };

        ChecksContext.Equal(
            "记忆里是官方原始 id cities_v2 也能命中正名安装项",
            "cities_skylines",
            GameSelection.Select(withCs1, "cities_v2", null)?.GameId);

        ChecksContext.Equal(
            "原始 id + 安装根都匹配时也命中",
            "cities_skylines",
            GameSelection.Select(withCs1, "cities_v2", @"D:\\Games\\Cities Skylines")?.GameId);

        ChecksContext.Equal(
            "原始 id 匹配不到安装根时按 gameId 命中",
            "cities_skylines",
            GameSelection.Select(withCs1, "cities_v2", @"E:\\Moved\\Cities Skylines")?.GameId);
    }

    private static GameInstallation Create(string gameId, string installRoot, GameSupport support)
    {
        return new GameInstallation(
            GameId: gameId,
            DisplayName: gameId,
            Version: "test",
            InstallRoot: installRoot,
            LauncherSettingsPath: Path.Combine(installRoot, "launcher-settings.json"),
            Format: LoadFileFormat.ContentLoadV1,
            GameDataDir: Path.Combine(installRoot, "game"),
            DlcPath: Path.Combine(installRoot, "dlc"),
            ModDir: Path.Combine(installRoot, "mod"),
            ExePath: Path.Combine(installRoot, "binaries", gameId + ".exe"),
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
            Support: support);
    }
}
