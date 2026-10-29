namespace PdxLauncher.Core.Online;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 在途请求合并（single-flight）：同一个 key 的并发请求只真正发一次，其余等待同一个结果。
/// <para>
/// 为什么需要：页面里「快速翻两页又翻回来」「打开详情时详情页与列表页同时要同一份数据」
/// 都会对同一个 URI 发两次请求，而 Steam 是按 IP 计数的——重复的每一次都在给限流添砖。
/// </para>
/// <para>
/// 语义细节：
/// <list type="bullet">
/// <item>只有「同时进行中」才合并；请求完成后 key 立即移除，下一次是真正的新请求（缓存策略不在这里做）。</item>
/// <item>leader 抛出的异常会原样广播给所有等待者——它们等的是同一个结果，不该一半成功一半失败。</item>
/// <item>leader 被取消时等待者一起被取消。这是可接受的代价：同一 key 的调用点本来就是同一交互
/// （同一个页面的两次点击），整页取消时它们本来就该一起结束。</item>
/// </list>
/// </para>
/// </summary>
public sealed class RequestCoalescer
{
    private readonly IAppLogger? _logger;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <param name="logger">可选日志；只用于记录「合并了几个请求」。</param>
    public RequestCoalescer(IAppLogger? logger = null) => _logger = logger;

    /// <summary>当前在途的 key 数量（自检用）。</summary>
    public int InFlight
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// 执行一次可合并的请求。<paramref name="key"/> 必须完整编码请求参数
    /// （URI 或「类型:参数」），否则会把不同请求错并成一个。
    /// </summary>
    /// <typeparam name="T">结果类型。</typeparam>
    /// <param name="key">合并键。</param>
    /// <param name="factory">真正的请求；只有 leader 会被调用。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    public async Task<T> RunAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        TaskCompletionSource<T>? leader = null;
        Task<T> task;
        bool isLeader = false;
        int waiters = 0;

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing)
                && existing.Task is Task<T> inFlight)
            {
                task = inFlight;
                existing.Waiters++;
                waiters = existing.Waiters;
            }
            else
            {
                isLeader = true;
                leader = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                _entries[key] = new Entry(leader.Task);
                task = leader.Task;
            }
        }

        if (!isLeader)
        {
            _logger?.Info("请求合并：" + key + "（第 " + waiters + " 个等待者复用同一个在途请求）");
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var result = await factory(cancellationToken).ConfigureAwait(false);
            leader!.TrySetResult(result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            leader!.TrySetCanceled(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            // 同一个失败也要广播：等待者等的是这份请求的结果，不能让它一直挂着。
            leader!.TrySetException(ex);
            throw;
        }
        finally
        {
            lock (_gate)
            {
                _entries.Remove(key);
            }
        }
    }

    private sealed class Entry
    {
        public Entry(Task task) => Task = task;

        public Task Task { get; }

        public int Waiters { get; set; }
    }
}
