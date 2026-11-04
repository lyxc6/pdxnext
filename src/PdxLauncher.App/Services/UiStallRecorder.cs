namespace PdxLauncher.App.Services;

/// <summary>
/// UI 线程停顿的判定与面包屑留存。
/// <para>
/// 只做两件事：把心跳间隔换算成「这次停顿有多长」，以及记下最近几个阶段（面包屑），
/// 停顿结束时一并写进日志。不碰定时器、窗口与 DispatcherQueue，因此自检里可以直接构造。
/// </para>
/// <para>
/// 判定口径：心跳每 <see cref="TickIntervalMs"/> 毫秒打一次，间隔超过
/// <see cref="WarnThresholdMs"/> 就开始累计；下一次正常心跳到来时把累计值作为一次停顿上报。
/// 累计而不是逐次上报，是因为一次长冻结只会在恢复后落下**一个**很大的间隔。
/// </para>
/// </summary>
public sealed class UiStallRecorder
{
    /// <summary>看门狗的心跳间隔（毫秒）。越小越准，但空转开销也越大。</summary>
    public const int TickIntervalMs = 100;

    /// <summary>超过这个间隔就算一次停顿（毫秒）。正常心跳抖动在 1–5 ms 量级。</summary>
    public const int WarnThresholdMs = 250;

    /// <summary>超过这个间隔记为 Error：用户已经能感到窗口「死了」。</summary>
    public const int ErrorThresholdMs = 3000;

    /// <summary>面包屑最多保留几条；超出后丢最旧的。</summary>
    public const int BreadcrumbCapacity = 12;

    private readonly Queue<string> _breadcrumbs = new();
    private long _pendingMs;
    private long _maxStallMs;
    private int _stallCount;

    /// <summary>已经上报过的停顿次数。</summary>
    public int StallCount => _stallCount;

    /// <summary>累计上报过的最长一次停顿（毫秒）；正常运行时远小于阈值。</summary>
    public long MaxStallMs => _maxStallMs;

    /// <summary>记一条面包屑：说明「现在走到哪一步」，例如「工坊 拉取第 2 页」。</summary>
    /// <param name="message">简短的位置描述；空白字符串被忽略。</param>
    public void Breadcrumb(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _breadcrumbs.Enqueue(message);
        while (_breadcrumbs.Count > BreadcrumbCapacity)
        {
            _breadcrumbs.Dequeue();
        }
    }

    /// <summary>把面包屑拼成一行，最旧的在前；没有面包屑时返回空串。</summary>
    public string Snapshot() => string.Join(" ← ", _breadcrumbs);

    /// <summary>喂一次心跳间隔。</summary>
    /// <param name="intervalMs">距上次心跳的实际毫秒数。</param>
    /// <returns>停顿刚刚结束时返回这次停顿；仍在累计或一切正常时返回 <c>null</c>。</returns>
    public UiStall? Observe(long intervalMs)
    {
        if (intervalMs >= WarnThresholdMs)
        {
            _pendingMs += intervalMs;
            return null;
        }

        if (_pendingMs < WarnThresholdMs)
        {
            _pendingMs = 0;
            return null;
        }

        var stall = new UiStall(_pendingMs, Snapshot());
        _pendingMs = 0;
        _stallCount++;
        if (stall.DurationMs > _maxStallMs)
        {
            _maxStallMs = stall.DurationMs;
        }

        return stall;
    }

    /// <summary>清掉累计状态（看门狗重启时用）。</summary>
    public void Reset()
    {
        _breadcrumbs.Clear();
        _pendingMs = 0;
        _maxStallMs = 0;
        _stallCount = 0;
    }
}

/// <summary>一次 UI 线程停顿：持续时长 + 停顿结束时的最近阶段。</summary>
/// <param name="DurationMs">停顿累计毫秒数（心跳间隔之和，是真实停顿的下界）。</param>
/// <param name="Breadcrumbs">停顿结束时的面包屑快照。</param>
public readonly record struct UiStall(long DurationMs, string Breadcrumbs)
{
    /// <summary>是否已经严重到该记 Error。</summary>
    public bool IsSevere => DurationMs >= UiStallRecorder.ErrorThresholdMs;
}
