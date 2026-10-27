namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.Themes;

/// <summary>
/// ThemePatch 自检：把「某款游戏的联网主题」原地换进游戏列表，
/// 且必须保证「内容没变就返回同一个 list 实例」——UI 靠这一点判断要不要重画。
/// </summary>
public static class ThemePatchChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var ck3 = BuildGame("ck3", "Crusader Kings III", "C:\\Games\\CK3");
        var v3 = BuildGame("victoria3", "Victoria 3", "D:\\Games\\V3");
        IReadOnlyList<GameInstallation> games = new[] { ck3, v3 };

        // 未命中：返回同一个实例（调用方据此跳过 SetGames）。
        var untouched = ThemePatch.Apply(games, "stellaris", NewTheme("C:\\cache\\stellaris-background.png"));
        ChecksContext.Check("未命中的 gameId 返回同一实例", ReferenceEquals(games, untouched));

        // 主题值完全相同：也必须返回同一个实例（避免无意义重画）。
        var sameTheme = ThemePatch.Apply(games, "ck3", ck3.Theme);
        ChecksContext.Check("主题未变化时返回同一实例", ReferenceEquals(games, sameTheme));

        // 命中且不同：只替换那一项，其余项保持原引用。
        var newCk3Theme = NewTheme("C:\\cache\\ck3-background-1a2b3c4d.jpg");
        var patched = ThemePatch.Apply(games, "ck3", newCk3Theme);
        ChecksContext.Check("主题变化时返回新实例", !ReferenceEquals(games, patched));
        ChecksContext.Equal("列表长度不变", games.Count, patched.Count);
        ChecksContext.Check("命中的游戏换了主题", ReferenceEquals(newCk3Theme, patched[0].Theme));
        ChecksContext.Check("未命中的游戏保持原对象", ReferenceEquals(v3, patched[1]));
        ChecksContext.Equal<string?>("原列表未被改写", ck3.Theme.Background, games[0].Theme.Background);
        ChecksContext.Equal<string?>("新主题背景生效", newCk3Theme.Background, patched[0].Theme.Background);

        // gameId 大小写不敏感：方便调用方直接拿 UI 里的大写 id。
        var upper = ThemePatch.Apply(games, "CK3", NewTheme("C:\\cache\\ck3-background-ffffffff.jpg"));
        ChecksContext.Equal<string?>("gameId 匹配大小写不敏感", "C:\\cache\\ck3-background-ffffffff.jpg", upper[0].Theme.Background);

        // 空列表 / null 主题的边界。
        var empty = ThemePatch.Apply(Array.Empty<GameInstallation>(), "ck3", newCk3Theme);
        ChecksContext.Equal("空列表不抛异常", 0, empty.Count);

        return ChecksContext.Failures - before;
    }

    private static ThemeSpec NewTheme(string background) =>
        new(
            background,
            null,
            null,
            null,
            null,
            null,
            "#FFFFFF",
            "#FFFFFF",
            24d,
            400,
            null,
            ThemeAssetSource.OnlineCache);

    private static GameInstallation BuildGame(string gameId, string displayName, string root) =>
        new(
            gameId,
            displayName,
            "1.0.0",
            root,
            Path.Combine(root, "launcher", "launcher-settings.json"),
            LoadFileFormat.DlcLoadV0,
            root,
            string.Empty,
            string.Empty,
            Path.Combine(root, "game.exe"),
            Array.Empty<string>(),
            Array.Empty<AlternativeExecutable>(),
            new ThemeSpec(
                Path.Combine(root, "launcher", "assets", "background.png"),
                null,
                null,
                null,
                null,
                null,
                "#FFFFFF",
                "#FFFFFF",
                24d,
                400,
                null,
                ThemeAssetSource.InstallDir),
            null,
            null,
            null,
            GameSupport.Full);
}
