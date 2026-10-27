namespace PdxLauncher.App.Services;

using Microsoft.UI.Dispatching;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Themes;

/// <summary>
/// 把 <c>IThemeResolver</c> 接进真正的渲染路径。分两段，互不阻塞：
/// 1) <see cref="ApplyCachedLayerAsync"/>：游戏发现后同步合并「联网缓存层」，零网络、毫秒级；
/// 2) <see cref="StartBackgroundRefresh"/>：窗口出来后 detached 跑的联网刷新，按游戏逐个补丁，
///    当前游戏先完成先换图。刷新只往外写 AppServices.Games，UI 靠 CurrentGameChanged / ThemesChanged 重画。
/// </summary>
internal static class ThemeUpdateService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static int _backgroundStarted;
    private static DispatcherQueue? _dispatcher;

    /// <summary>
    /// 逐款把 <c>game.Theme</c> 换成「缓存层 + 安装目录层」合并后的结果。
    /// 任何一款失败都只记日志并保留安装目录层，绝不抛异常给游戏发现流程。
    /// </summary>
    public static async Task<IReadOnlyList<GameInstallation>> ApplyCachedLayerAsync(
        IReadOnlyList<GameInstallation> games,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(games);
        _dispatcher ??= DispatcherQueue.GetForCurrentThread();

        if (games.Count == 0)
        {
            return games;
        }

        var result = games;
        var resolved = 0;
        foreach (var game in games)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var theme = await AppServices.ThemeResolver
                    .ResolveAsync(game, allowOnlineRefresh: false, cancellationToken)
                    .ConfigureAwait(true);
                var patched = ThemePatch.Apply(result, game.GameId, theme);
                if (!ReferenceEquals(patched, result))
                {
                    result = patched;
                    resolved++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppServices.Logger.Warn("应用主题缓存层失败（gameId=" + game.GameId + "）: " + ex.Message);
            }
        }

        if (resolved > 0)
        {
            AppServices.Logger.Info("主题缓存层已应用: " + resolved + " 个游戏使用了联网缓存素材");
        }

        return result;
    }

    /// <summary>启动一次后台联网刷新（进程内只启动一次；拿不到锁就安静退出，重复调用无副作用）。</summary>
    public static void StartBackgroundRefresh()
    {
        if (Interlocked.Exchange(ref _backgroundStarted, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshAsync(only: null, waitForLock: false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppServices.Logger.Warn("后台主题刷新异常: " + ex.Message);
            }
        });
    }

    /// <summary>
    /// 联网刷新主题。当前游戏（或 <paramref name="only"/> 指定的那一款）排在最前，
    /// 每完成一款就立刻回写并广播，不等其它游戏下载完。
    /// </summary>
    public static Task<int> RefreshAsync(GameInstallation? only, CancellationToken cancellationToken) =>
        RefreshAsync(only, waitForLock: true, cancellationToken);

    private static async Task<int> RefreshAsync(
        GameInstallation? only,
        bool waitForLock,
        CancellationToken cancellationToken)
    {
        if (waitForLock)
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (!await Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            AppServices.Logger.Info("主题刷新已在进行中，跳过本次后台刷新");
            return 0;
        }

        try
        {
            var changed = 0;
            foreach (var game in OrderGames(only))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var allowOnline = AppServices.Settings.OnlineThemeRefreshEnabled;
                    var theme = await AppServices.ThemeResolver
                        .ResolveAsync(game, allowOnline, cancellationToken)
                        .ConfigureAwait(false);

                    var applied = await RunOnUiAsync(() =>
                    {
                        var updated = ThemePatch.Apply(AppServices.Games, game.GameId, theme);
                        if (ReferenceEquals(updated, AppServices.Games))
                        {
                            return false;
                        }

                        AppServices.SetGames(updated);
                        AppServices.NotifyThemesChanged(game.GameId);
                        return true;
                    }).ConfigureAwait(false);

                    if (applied)
                    {
                        changed++;
                        AppServices.Logger.Info("已换用新的主题素材: " + game.GameId + "（来源=" + theme.Source + "）");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppServices.Logger.Warn("联网刷新主题失败（gameId=" + game.GameId + "）: " + ex.Message);
                }
            }

            AppServices.Logger.Info("后台主题刷新完成: 更新 " + changed + " 个游戏");
            return changed;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>刷新顺序：指定的那一款 / 当前游戏优先，其余按发现顺序。</summary>
    private static List<GameInstallation> OrderGames(GameInstallation? only)
    {
        var all = AppServices.Games;
        var ordered = new List<GameInstallation>(all.Count + 1);

        void AddFirst(GameInstallation? candidate)
        {
            if (candidate is null)
            {
                return;
            }

            var match = all.FirstOrDefault(g => string.Equals(g.GameId, candidate.GameId, StringComparison.OrdinalIgnoreCase))
                ?? candidate;
            if (!ordered.Any(g => string.Equals(g.GameId, match.GameId, StringComparison.OrdinalIgnoreCase)))
            {
                ordered.Add(match);
            }
        }

        AddFirst(only);
        AddFirst(AppServices.CurrentGame);
        foreach (var game in all)
        {
            AddFirst(game);
        }

        return ordered;
    }

    /// <summary>把列表回写与事件广播送回 UI 线程；已经在线程上就直接执行。</summary>
    private static Task<bool> RunOnUiAsync(Func<bool> action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            return Task.FromResult(action());
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() =>
            {
                try
                {
                    completion.SetResult(action());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }))
        {
            completion.SetException(new InvalidOperationException("无法把主题更新派发到 UI 线程"));
        }

        return completion.Task;
    }
}
