namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Steam;

/// <summary>
/// 按当前游戏造「Steam 工坊直连后端」的统一入口。
/// <para>
/// 直连后端有两个层级，对应两个设置开关：
/// </para>
/// <list type="bullet">
/// <item><description>
/// 只读状态（订阅 / 已安装 / 需要更新 / 下载中 / 本地禁用）：由
/// <c>AppSettings.SteamWorkshopStateEnabled</c> 控制，默认开。
/// 代价是每次查询闪一下 Steam 的「游戏中」登记，与现有的 DLC 授权探测同级。
/// </description></item>
/// <item><description>
/// 写操作（订阅 / 退订 / 触发下载 / 本地禁用）：由
/// <c>AppSettings.SteamWorkshopSubscribeEnabled</c> 控制，默认关。
/// </description></item>
/// </list>
/// </summary>
public static class SteamWorkshopBackends
{
    /// <summary>
    /// 建一个用于「读状态 / 触发下载 / 本地禁用」的后端。
    /// 设置里关掉了状态直连、或这款游戏没有直连条件时返回 null。
    /// </summary>
    /// <param name="game">目标游戏。</param>
    /// <returns>后端；不可用时为 null。</returns>
    public static SteamWorkshopHostBackend? CreateForState(GameInstallation? game)
    {
        if (game is null || !AppServices.Settings.SteamWorkshopStateEnabled)
        {
            return null;
        }

        return Create(game);
    }

    /// <summary>
    /// 建一个用于「订阅 / 退订」的后端（写开关控制）。详情页沿用这条路径。
    /// </summary>
    /// <param name="game">目标游戏。</param>
    /// <returns>后端；不可用时为 null。</returns>
    public static SteamWorkshopHostBackend? CreateForSubscribe(GameInstallation? game)
    {
        if (game is null || !AppServices.Settings.SteamWorkshopSubscribeEnabled)
        {
            return null;
        }

        return Create(game);
    }

    private static SteamWorkshopHostBackend? Create(GameInstallation game)
    {
        try
        {
            var appId = WorkshopSourceChooser.ResolveAppId(game.SteamAppId, game.GameId);
            var backend = SteamWorkshopHostBackend.TryCreate(
                new SteamHostClient(AppServices.Logger),
                AppServices.Logger,
                AppServices.FileSystem,
                new SteamApiLocator(AppServices.FileSystem, AppServices.Logger),
                AppServices.Settings.SteamApiDllPath,
                game,
                appId,
                out var reason);

            if (backend is null)
            {
                AppServices.Logger.Warn("Steam 工坊直连不可用（" + reason + "），走降级路径");
            }

            return backend;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("初始化 Steam 工坊直连失败，走降级路径", ex);
            return null;
        }
    }
}
