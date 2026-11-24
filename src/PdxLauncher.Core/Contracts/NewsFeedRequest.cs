namespace PdxLauncher.Core.Contracts;

/// <summary>
/// 一次官方公告读取所需的上下文。旧调用点继续用 <c>GetAsync(game, allowOnline, ct)</c>，
/// 这里只承载官方新版 Braze feed 需要的平台、启动次数与已拥有 DLC 信息。
/// </summary>
public sealed record NewsFeedRequest(
    GameInstallation Game,
    string DistributionPlatform,
    int LaunchCount,
    IReadOnlySet<string> OwnedDlcIds,
    bool AllowOnline,
    bool ForceRefresh = false)
{
    /// <summary>缺省上下文：平台来自游戏配置，其余过滤信息不可用。</summary>
    public static NewsFeedRequest FromGame(
        GameInstallation game,
        bool allowOnline,
        bool forceRefresh = false) =>
        new(
            game,
            string.IsNullOrWhiteSpace(game.DistributionPlatform) ? "pdx" : game.DistributionPlatform,
            0,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            allowOnline,
            forceRefresh);
}
