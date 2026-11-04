namespace PdxLauncher.App.Services;

using System.Diagnostics;

using Microsoft.UI.Dispatching;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// UI 线程看门狗：在窗口的 DispatcherQueue 上挂一只 100 ms 的重复心跳。
/// <para>
/// 心跳间隔一旦超过 <see cref="UiStallRecorder.WarnThresholdMs"/>，说明 UI 线程被同步工作占住了 ——
/// WinUI 的消息泵停了，窗口既不能拖动也不响应点击。停顿结束后按长度记 Warn / Error，
/// 并带上 <see cref="UiStallRecorder.Snapshot"/> 里的面包屑，用来定位是哪一步卡住了。
/// </para>
/// <para>
/// 必须从 UI 线程创建（<see cref="DispatcherQueue.GetForCurrentThread"/> 或窗口的 DispatcherQueue），
/// 之后 <see cref="Breadcrumb"/> 可以从任意线程调用：非 UI 线程会转投到队列里，避免与记录器竞争。
/// </para>
/// </summary>
public sealed class UiThreadWatchdog : IDisposable
{
    private readonly DispatcherQueue _queue;
    private readonly DispatcherQueueTimer _timer;
    private readonly IAppLogger _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastTick;
    private bool _disposed;

    private UiThreadWatchdog(DispatcherQueue queue, IAppLogger logger, UiStallRecorder recorder)
    {
        _queue = queue;
        _logger = logger;
        Recorder = recorder;
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(UiStallRecorder.TickIntervalMs);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
    }

    /// <summary>底层记录器：自检与诊断页要看最长停顿/面包屑时读它。</summary>
    public UiStallRecorder Recorder { get; }

    /// <summary>
    /// 启动看门狗；当前线程没有 DispatcherQueue 时记一条 Warn 并返回 <c>null</c>（不影响启动）。
    /// </summary>
    /// <param name="logger">日志。</param>
    /// <param name="queue">窗口的 DispatcherQueue；省略时取当前线程的。</param>
    /// <param name="recorder">可注入的记录器（自检用）；省略时新建。</param>
    public static UiThreadWatchdog? Start(
        IAppLogger logger,
        DispatcherQueue? queue = null,
        UiStallRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        queue ??= DispatcherQueue.GetForCurrentThread();
        if (queue is null)
        {
            logger.Warn("UI 线程卡顿监测未启动：当前线程没有 DispatcherQueue");
            return null;
        }

        var watchdog = new UiThreadWatchdog(queue, logger, recorder ?? new UiStallRecorder());
        watchdog._lastTick = watchdog._clock.Elapsed;
        watchdog._timer.Start();
        logger.Info(
            "UI 线程卡顿监测已启动：间隔 " + UiStallRecorder.TickIntervalMs
            + " ms，阈值 " + UiStallRecorder.WarnThresholdMs + " ms");
        return watchdog;
    }

    /// <summary>
    /// 记一条面包屑（「现在走到哪一步」）。任意线程可调：非 UI 线程转投到 UI 队列，
    /// 避免看门狗读快照时与后台线程同时改集合。
    /// </summary>
    /// <param name="message">简短位置描述；空白字符串被忽略。</param>
    public void Breadcrumb(string message)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (_queue.HasThreadAccess)
        {
            Recorder.Breadcrumb(message);
            return;
        }

        _queue.TryEnqueue(() => Recorder.Breadcrumb(message));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Tick -= OnTick;
        _timer.Stop();
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        var now = _clock.Elapsed;
        var intervalMs = (long)(now - _lastTick).TotalMilliseconds;
        _lastTick = now;

        if (Recorder.Observe(intervalMs) is not { } stall)
        {
            return;
        }

        var message = "UI 线程停顿 " + stall.DurationMs + " ms";
        if (stall.Breadcrumbs.Length > 0)
        {
            message += "；最近位置：" + stall.Breadcrumbs;
        }

        if (stall.IsSevere)
        {
            _logger.Error(message);
        }
        else
        {
            _logger.Warn(message);
        }
    }
}
