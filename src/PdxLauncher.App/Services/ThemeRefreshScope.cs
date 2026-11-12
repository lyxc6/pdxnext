namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 决定一轮主题刷新要碰哪些游戏。
/// <para>
/// 后台刷新需要「当前游戏先完成先换图，其余继续补齐」，所以返回的是排好序的全量；
/// 设置页「立即检查」只针对用户点的那一款，必须只返回它 —— 否则 16 款游戏串行刷新
/// 会把按钮按成两分钟无响应。
/// </para>
/// </summary>
public static class ThemeRefreshScope
{
    /// <summary>按刷新范围排出本轮要处理的游戏。</summary>
    /// <param name="all">本次发现的全部游戏。</param>
    /// <param name="only">设置页指定的游戏；为 null 表示没有指定。</param>
    /// <param name="current">当前游戏；后台刷新时排在指定款之后。</param>
    /// <param name="onlyThatGame">true = 只刷新 <paramref name="only"/> 这一款。</param>
    /// <returns>本轮要刷新的有序列表；<paramref name="onlyThatGame"/> 为 true 且没有指定游戏时返回空。</returns>
    public static IReadOnlyList<GameInstallation> Order(
        IReadOnlyList<GameInstallation> all,
        GameInstallation? only,
        GameInstallation? current,
        bool onlyThatGame)
    {
        ArgumentNullException.ThrowIfNull(all);

        if (onlyThatGame)
        {
            if (only is null)
            {
                return [];
            }

            return [Find(all, only.GameId) ?? only];
        }

        var ordered = new List<GameInstallation>(all.Count + 1);
        AddFirst(ordered, all, only);
        AddFirst(ordered, all, current);
        foreach (var game in all)
        {
            AddFirst(ordered, all, game);
        }

        return ordered;
    }

    private static GameInstallation? Find(IReadOnlyList<GameInstallation> all, string gameId) =>
        all.FirstOrDefault(game => string.Equals(game.GameId, gameId, StringComparison.OrdinalIgnoreCase));

    private static void AddFirst(
        ICollection<GameInstallation> ordered,
        IReadOnlyList<GameInstallation> all,
        GameInstallation? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        var match = Find(all, candidate.GameId) ?? candidate;
        if (ordered.Any(game => string.Equals(game.GameId, match.GameId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ordered.Add(match);
    }
}
