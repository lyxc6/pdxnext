namespace PdxLauncher.Core.Configuration;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 「启动时选哪款游戏」的纯函数。抽到 Core 是为了能离线自检
/// （<see cref="Select"/> 不碰文件、不碰 UI，只做优先级判断）。
/// <para>
/// 恢复逻辑：先认「上次选中的游戏」，认不出来才退回原来那条「能管理内容的优先」规则 ——
/// 否则重启永远跳回第一款可管理内容的游戏，用户每次都得手动再选一遍。
/// </para>
/// </summary>
public static class GameSelection
{
    /// <summary>按「上次选中 → 能管理内容 → 能识别 → 第一个」挑一款。</summary>
    /// <param name="games">当前已发现的游戏。</param>
    /// <param name="lastGameId">上次选中的 gameId；null/空串表示没有记忆。</param>
    /// <param name="lastInstallRoot">上次选中游戏的安装根；与 gameId 一起命中才算精确恢复。</param>
    /// <returns>选中的游戏；列表为空时返回 null。</returns>
    public static GameInstallation? Select(
        IReadOnlyList<GameInstallation> games,
        string? lastGameId,
        string? lastInstallRoot)
    {
        ArgumentNullException.ThrowIfNull(games);
        if (games.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(lastGameId))
        {
            // 同一个游戏装了多份时，安装根能唯一定位；对不上就退到「同 gameId 的第一份」。
            if (!string.IsNullOrWhiteSpace(lastInstallRoot))
            {
                var exact = games.FirstOrDefault(g =>
                    string.Equals(g.GameId, lastGameId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(g.InstallRoot, lastInstallRoot, StringComparison.OrdinalIgnoreCase));
                if (exact is not null)
                {
                    return exact;
                }
            }

            var sameGame = games.FirstOrDefault(g =>
                string.Equals(g.GameId, lastGameId, StringComparison.OrdinalIgnoreCase));
            if (sameGame is not null)
            {
                return sameGame;
            }
        }

        if (!string.IsNullOrWhiteSpace(lastInstallRoot))
        {
            // gameId 认不出来了（游戏库改过 id、或多个版本共用目录）时，安装根仍能唯一定位。
            var sameRoot = games.FirstOrDefault(g =>
                string.Equals(g.InstallRoot, lastInstallRoot, StringComparison.OrdinalIgnoreCase));
            if (sameRoot is not null)
            {
                return sameRoot;
            }
        }

        // 记忆不可用（从没选过、游戏已卸载、设置文件是旧版）→ 原来的默认规则。
        return games.FirstOrDefault(static g => g.SupportsContentManagement)
            ?? games.FirstOrDefault(static g => g.IsSupported)
            ?? games[0];
    }
}
