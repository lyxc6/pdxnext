namespace PdxLauncher.Core.Themes;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 把解析好的主题写回游戏列表。纯函数：不改动入参，只按需生成新的列表 / 新的游戏对象。
/// 用于「发现后合并缓存层」与「联网刷新后热替换」两条路径，便于在 Checks 里直接断言。
/// </summary>
public static class ThemePatch
{
    /// <summary>
    /// 给 <paramref name="gameId"/> 对应的那款游戏换上 <paramref name="theme"/>。
    /// 未命中、或主题值本来就相同（record 值相等）时原样返回同一个列表实例 ——
    /// 这样 AppServices.SetGames 不会因为「没变化」而误发事件；
    /// 其余元素也保持原引用，避免当前选中项被判定成新对象。
    /// </summary>
    public static IReadOnlyList<GameInstallation> Apply(
        IReadOnlyList<GameInstallation> games,
        string gameId,
        ThemeSpec theme)
    {
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(theme);

        if (string.IsNullOrWhiteSpace(gameId))
        {
            return games;
        }

        var index = -1;
        for (var i = 0; i < games.Count; i++)
        {
            if (string.Equals(games[i].GameId, gameId, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index < 0 || ReferenceEquals(games[index].Theme, theme) || games[index].Theme == theme)
        {
            return games;
        }

        var updated = new GameInstallation[games.Count];
        for (var i = 0; i < games.Count; i++)
        {
            updated[i] = i == index ? games[i] with { Theme = theme } : games[i];
        }

        return updated;
    }
}
