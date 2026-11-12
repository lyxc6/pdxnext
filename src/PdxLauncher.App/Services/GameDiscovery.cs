namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 冷启动后的游戏发现入口。保证只真正扫描一次（页面先后加载时不会重复扫描），
/// 结果回填到 <see cref="AppServices.Games"/>；扫描失败只记日志，界面显示空状态。
/// 用户点右上角"刷新"时走 <see cref="RefreshAsync"/>，绕过缓存重新扫描。
/// </summary>
public static class GameDiscovery
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _completed;

    /// <summary>发现游戏（幂等，命中缓存直接返回）；永不抛异常（取消除外）。</summary>
    public static Task<IReadOnlyList<GameInstallation>> EnsureAsync(CancellationToken cancellationToken) =>
        LoadAsync(force: false, cancellationToken);

    /// <summary>强制重新扫描游戏库（"刷新"按钮）；失败时保留上一次的游戏列表。</summary>
    public static Task<IReadOnlyList<GameInstallation>> RefreshAsync(CancellationToken cancellationToken) =>
        LoadAsync(force: true, cancellationToken);

    private static async Task<IReadOnlyList<GameInstallation>> LoadAsync(bool force, CancellationToken cancellationToken)
    {
        if (_completed && !force)
        {
            return AppServices.Games;
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (_completed && !force)
            {
                return AppServices.Games;
            }

            // 刷新失败或被取消时不清 _completed：缓存里的旧列表仍然有效，下次 EnsureAsync 直接用它。
            // 游戏发现是同步文件 IO（扫 9 个安装约 200 ms+），必须离开 UI 线程；
            // 否则冷启动首帧会被它按住。
            var games = await Task
                .Run(
                    async () => await AppServices.GameCatalog
                        .DiscoverAsync(cancellationToken)
                        .ConfigureAwait(false),
                    cancellationToken)
                .ConfigureAwait(true);

            // 先把「联网缓存层」合并进去（纯本地、零网络），这样第一帧就能显示上次下载的新背景。
            var themed = await ThemeUpdateService
                .ApplyCachedLayerAsync(games, cancellationToken)
                .ConfigureAwait(true);
            AppServices.SetGames(themed);
            _completed = true;
            AppServices.Logger.Info("游戏发现完成，共 " + games.Count + " 个安装");

            // 再放一个 detached 的联网刷新：下载完成后按游戏逐个替换并广播，不阻塞任何页面。
            ThemeUpdateService.StartBackgroundRefresh();
            return AppServices.Games;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("游戏发现失败", ex);
            _completed = true;
            return AppServices.Games;
        }
        finally
        {
            Gate.Release();
        }
    }
}
