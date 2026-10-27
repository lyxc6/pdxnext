namespace PdxLauncher.App.Services;

/// <summary>
/// 页面级「重载协调器」：保证任何时刻只有一个重载在跑，且后到的请求一定覆盖先到的。
/// <para>
/// 之前每个页面各自用一个 <c>bool _isReloading</c> 做重入保护，语义是「正在加载就直接丢弃新请求」。
/// 快速连点游戏库里的卡片时会出问题：第一次点击的重载还在跑，后面几次全被丢掉，
/// 于是界面最后停在第 1 次点的那款游戏上 —— 用户看到的就是「点了没反应 / 显示的还是上一款」。
/// 这里换成「取消旧的、跑最新的」：新请求进来先取消上一次，旧流程在下一个 await 点退出，
/// 只有最新那次会把结果写进 ViewModel。
/// </para>
/// <para>
/// 每个页面持有一个实例，<see cref="_lifetime"/>（页面生命周期令牌）通过 <see cref="RunAsync"/> 传入，
/// 页面卸载时由页面自己 <c>_lifetime.Cancel()</c>，本类不需要知道页面何时关闭。
/// </para>
/// </summary>
public sealed class ReloadCoordinator
{
    private readonly Lock _gate = new();
    private CancellationTokenSource? _current;

    /// <summary>当前是否有重载在跑。</summary>
    public bool IsReloading
    {
        get
        {
            lock (_gate)
            {
                return _current is not null;
            }
        }
    }

    /// <summary>
    /// 执行一次重载；同一实例上的上一次重载会被取消。
    /// </summary>
    /// <param name="work">实际工作；拿到的令牌在页面卸载或新请求到来时取消。</param>
    /// <param name="lifetime">页面生命周期令牌。</param>
    /// <param name="onError">非取消类异常的落点（通常是 <c>Logger.Error</c>）。</param>
    public async Task RunAsync(
        Func<CancellationToken, Task> work,
        CancellationToken lifetime,
        Action<Exception> onError)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(onError);

        CancellationTokenSource request;
        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = _current;
            request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            _current = request;
        }

        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var token = request.Token;
            token.ThrowIfCancellationRequested();
            await work(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 被更新的请求取代，或页面已卸载：两者都不需要上报。
        }
        catch (Exception ex)
        {
            onError(ex);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_current, request))
                {
                    _current = null;
                }
            }

            request.Dispose();
        }
    }
}
