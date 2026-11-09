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
    /// <summary>后台刷新失败后的重试间隔；与 ThemeResolver 的索引退避保持一致。</summary>
    private static readonly TimeSpan BackgroundRetryDelay = TimeSpan.FromMinutes(10);

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static int _backgroundRunning;
    private static int _retryScheduled;
    private static long _nextBackgroundAttemptTicks;
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

    /// <summary>
    /// 启动一次后台联网刷新。旧的「进程级一次性闩」会让一次网络抖动把整个进程的主题永久锁死，
    /// 现在改为：并发闩只挡住重叠执行，失败后 10 分钟自动重试一次，成功即清退避。
    /// </summary>
    public static void StartBackgroundRefresh()
    {
        if (Interlocked.Exchange(ref _backgroundRunning, 1) == 1)
        {
            return;
        }

        var nextAttempt = Interlocked.Read(ref _nextBackgroundAttemptTicks);
        if (nextAttempt != 0 && Environment.TickCount64 < nextAttempt)
        {
            Interlocked.Exchange(ref _backgroundRunning, 0);
            return;
        }

        _ = Task.Run(async () =>
        {
            var attemptStarted = DateTimeOffset.UtcNow;
            try
            {
                var outcome = await RefreshCoreAsync(
                        only: null,
                        waitForLock: false,
                        forceRefresh: false,
                        onlyThatGame: false,
                        attemptStarted,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                if (outcome.Failed)
                {
                    ScheduleBackgroundRetry();
                }
                else
                {
                    ClearBackgroundRetry();
                }
            }
            catch (Exception ex)
            {
                AppServices.Logger.Warn("后台主题刷新异常: " + ex.Message);
                ScheduleBackgroundRetry();
            }
            finally
            {
                Interlocked.Exchange(ref _backgroundRunning, 0);
            }
        });
    }

    /// <summary>
    /// 联网刷新主题。当前游戏（或 <paramref name="only"/> 指定的那一款）排在最前，
    /// 每完成一款就立刻回写并广播，不等其它游戏下载完。
    /// </summary>
    public static Task<int> RefreshAsync(GameInstallation? only, CancellationToken cancellationToken) =>
        RefreshAsync(only, forceRefresh: false, cancellationToken, onlyThatGame: false);

    /// <summary>
    /// 设置页「立即检查」：<paramref name="forceRefresh"/> 绕过 24 小时 TTL 与 10 分钟退避，
    /// 但仍然拿 Gate，避免和后台刷新并发写索引。
    /// </summary>
    /// <param name="only">指定的游戏。</param>
    /// <param name="forceRefresh">是否绕过 TTL 与退避。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="onlyThatGame">
    /// true = 只刷新 <paramref name="only"/> 这一款；设置页「立即检查」必须传 true，
    /// 否则会把全部已安装游戏串行刷一遍（16 款实测两分多钟）。
    /// </param>
    public static async Task<int> RefreshAsync(
        GameInstallation? only,
        bool forceRefresh,
        CancellationToken cancellationToken,
        bool onlyThatGame = false)
    {
        var outcome = await RefreshCoreAsync(
                only,
                waitForLock: true,
                forceRefresh,
                onlyThatGame,
                DateTimeOffset.UtcNow,
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome.Failed)
        {
            ScheduleBackgroundRetry();
        }
        else
        {
            ClearBackgroundRetry();
        }

        return outcome.Changed;
    }

    private static async Task<RefreshOutcome> RefreshCoreAsync(
        GameInstallation? only,
        bool waitForLock,
        bool forceRefresh,
        bool onlyThatGame,
        DateTimeOffset attemptStarted,
        CancellationToken cancellationToken)
    {
        if (waitForLock)
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (!await Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            AppServices.Logger.Info("主题刷新已在进行中，跳过本次后台刷新");
            return new RefreshOutcome(0, Failed: false);
        }

        try
        {
            var changed = 0;
            var failed = false;
            var games = ThemeRefreshScope.Order(
                AppServices.Games,
                only,
                AppServices.CurrentGame,
                onlyThatGame);
            foreach (var game in games)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var allowOnline = AppServices.Settings.OnlineThemeRefreshEnabled;
                    var theme = await AppServices.ThemeResolver
                        .ResolveAsync(game, allowOnline, cancellationToken, forceRefresh)
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

                    if (allowOnline)
                    {
                        // ResolveAsync 内部会吞掉刷新异常并写回 LastErrorUtc；只有「本轮新产生」
                        // 的失败才触发 10 分钟重试。metadata 无记录（EU5）属于稳定状态，不重试。
                        var diagnostics = AppServices.ThemeResolver.GetDiagnostics(game.GameId);
                        if (!diagnostics.MetadataMissing
                            && diagnostics.LastErrorUtc is { } errorAt
                            && errorAt >= attemptStarted)
                        {
                            failed = true;
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed = true;
                    AppServices.Logger.Warn("联网刷新主题失败（gameId=" + game.GameId + "）: " + ex.Message);
                }
            }

            AppServices.Logger.Info(
                "后台主题刷新完成: 更新 " + changed + " 个游戏"
                + (failed ? "（存在失败，已安排 10 分钟后重试）" : string.Empty));
            return new RefreshOutcome(changed, failed);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void ScheduleBackgroundRetry()
    {
        Interlocked.Exchange(
            ref _nextBackgroundAttemptTicks,
            Environment.TickCount64 + (long)BackgroundRetryDelay.TotalMilliseconds);

        if (Interlocked.Exchange(ref _retryScheduled, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(BackgroundRetryDelay).ConfigureAwait(false);
                Interlocked.Exchange(ref _retryScheduled, 0);
                StartBackgroundRefresh();
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _retryScheduled, 0);
                AppServices.Logger.Warn("后台主题重试调度失败: " + ex.Message);
            }
        });
    }

    private static void ClearBackgroundRetry()
    {
        Interlocked.Exchange(ref _nextBackgroundAttemptTicks, 0);

        // 同时清掉调度标记：否则成功后再过 10 分钟，已排队的 Task.Delay 仍会触发一次
        // 空转刷新（decision=Fresh、无网络请求，但属于无效调度）。
        Interlocked.Exchange(ref _retryScheduled, 0);
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

    private readonly record struct RefreshOutcome(int Changed, bool Failed);
}
