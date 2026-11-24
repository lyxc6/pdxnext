namespace PdxLauncher.App.Services;

using System.Diagnostics;
using System.Timers;

using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.CrashAnalysis;

/// <summary>
/// 游戏会话监听：游戏启动时开始增量采集错误日志，进程退出后固化「本次游戏」摘要并通过事件通知 UI。
/// <para>
/// 不写游戏目录、不联网、不启动进程本身。进程退出检测靠 pid 轮询（同时核对进程名，降低 pid 复用误判），
/// 所有异常都吞掉并写入日志，绝不影响启动/页面。
/// </para>
/// </summary>
public sealed class GameSessionMonitorService : IDisposable
{
    private readonly GameSessionLogTracker _tracker;
    private readonly SessionFatalAlert _alert = new();
    private readonly IAppServicesClock _clock;
    private readonly Action<string> _log;
    private readonly System.Timers.Timer _timer;
    private readonly object _gate = new();
    private bool _busy;

    private GameInstallation? _game;
    private int? _pid;
    private string? _exeName;

    /// <summary>本次会话结束时触发（参数为固化后的快照）。订阅方负责 marshal 到 UI 线程。</summary>
    public event EventHandler<GameSessionSnapshot>? SessionEnded;

    /// <summary>发现新的致命初始化错误时触发一次（游戏可能仍在运行）。订阅方负责 marshal 到 UI 线程。</summary>
    public event EventHandler<GameSessionSnapshot>? SessionAlert;

    /// <summary>最近一次结束的会话快照；尚无时为 null。</summary>
    public GameSessionSnapshot? LastSession { get; private set; }

    /// <summary>当前可展示的会话快照：运行中取实时数据，结束后取固化快照。</summary>
    public GameSessionSnapshot? CurrentSnapshot
    {
        get
        {
            lock (_gate)
            {
                return IsRunning ? _tracker.Snapshot() : LastSession;
            }
        }
    }

    /// <summary>是否有正在监听的游戏会话。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>创建监听器。定时在后台线程跑；SessionEnded 订阅方负责 marshal 到 UI 线程。</summary>
    public GameSessionMonitorService(
        GameSessionLogTracker tracker,
        IAppServicesClock clock,
        Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);

        _tracker = tracker;
        _clock = clock;
        _log = log;
        _timer = new System.Timers.Timer(3000)
        {
            AutoReset = true,
        };
        _timer.Elapsed += OnTick;
    }

    /// <summary>游戏启动成功后调用。</summary>
    public void Begin(GameInstallation game, int? processId)
    {
        ArgumentNullException.ThrowIfNull(game);

        lock (_gate)
        {
            if (_tracker.IsRunning)
            {
                // 上一会话还没结束就又启动一局：先收尾旧的，避免相互覆盖。
                Finish(DateTimeOffset.UtcNow);
            }

            _game = game;
            _pid = processId;
            _exeName = string.IsNullOrWhiteSpace(game.ExePath)
                ? null
                : Path.GetFileNameWithoutExtension(game.ExePath);
            _tracker.Begin(game, processId, _clock.Now);
            _alert.Reset();
            IsRunning = true;
            _timer.Start();
            _log("开始监听本次游戏报错：" + game.DisplayName + "（pid=" + (processId?.ToString() ?? "未知") + "）");
        }
    }

    /// <summary>主动结束（例如用户关闭启动器前）；正常情况由进程退出检测自动调用。</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                Finish(_clock.Now);
            }

            _timer.Stop();
        }
    }

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            lock (_gate)
            {
                if (!IsRunning || _game is null)
                {
                    _timer.Stop();
                    return;
                }

                if (IsGameAlive(_pid, _exeName))
                {
                    _tracker.Poll();
                    RaiseAlertIfNeeded(_tracker.Snapshot());
                    return;
                }

                Finish(_clock.Now);
                _timer.Stop();
            }
        }
        catch (Exception exception)
        {
            _log("监听本次游戏报错失败：" + exception.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private void Finish(DateTimeOffset endTime)
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            var snapshot = _tracker.End();
            LastSession = snapshot with { EndTime = endTime };
            RaiseAlertIfNeeded(snapshot);
            IsRunning = false;
            _game = null;
            _pid = null;
            _exeName = null;
            _log("本次游戏结束：发现 " + LastSession.ErrorBlocks + " 条报错");
            SessionEnded?.Invoke(this, LastSession);
        }
        catch (Exception exception)
        {
            _log("固化本次游戏报错摘要失败：" + exception.Message);
            IsRunning = false;
        }
    }

    private void RaiseAlertIfNeeded(GameSessionSnapshot snapshot)
    {
        if (!IsRunning)
        {
            return;
        }

        if (!_alert.ShouldAnnounce(snapshot))
        {
            return;
        }

        SessionAlert?.Invoke(this, snapshot);
    }

    private static bool IsGameAlive(int? pid, string? exeName)
    {
        if (pid is null)
        {
            // 没有 pid 时无法判断进程，交给 45 分钟的兜底（由 Start 侧决定），这里持续采集。
            return true;
        }

        try
        {
            using var process = Process.GetProcessById(pid.Value);
            if (process.HasExited)
            {
                return false;
            }

            // pid 复用防护：进程名对不上就当作原游戏已退出。
            if (!string.IsNullOrWhiteSpace(exeName)
                && !string.Equals(
                    Path.GetFileNameWithoutExtension(process.ProcessName),
                    exeName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }
        catch (ArgumentException)
        {
            return false; // 进程不存在。
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer.Elapsed -= OnTick;
        _timer.Stop();
        _timer.Dispose();
    }
}

/// <summary>监听器需要的本地时间源，便于自检注入。</summary>
public interface IAppServicesClock
{
    /// <summary>当前本地时间。</summary>
    DateTimeOffset Now { get; }
}

/// <summary>使用系统时钟的实现。</summary>
public sealed class SystemAppServicesClock : IAppServicesClock
{
    /// <inheritdoc />
    public DateTimeOffset Now => DateTimeOffset.UtcNow.ToLocalTime();
}
