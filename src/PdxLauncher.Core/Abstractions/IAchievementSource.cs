namespace PdxLauncher.Core.Abstractions;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 成就数据源。实现可以走 Steam 宿主子进程（S2）或本地缓存，但只能返回
/// <see cref="AchievementSnapshot"/>，不得把平台异常抛给界面。
/// </summary>
public interface IAchievementSource
{
    /// <summary>
    /// 取指定游戏的成就快照。数据源不可用时返回 <see cref="AchievementAvailability.NotSupported"/>，
    /// 连不上平台时返回 <see cref="AchievementAvailability.SteamUnavailable"/>，不要抛异常。
    /// </summary>
    /// <param name="gameId">游戏 id（<c>GameInstallation.GameId</c>，通常是 Steam appid 或 PDX 标识）。</param>
    /// <param name="cancellationToken">页面生命周期 / 切游戏时取消。</param>
    Task<AchievementSnapshot> GetAsync(string gameId, CancellationToken cancellationToken);
}
