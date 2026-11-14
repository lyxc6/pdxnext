namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online.Translation.Local;

/// <summary>一次本地模型下载当前处在哪个阶段。</summary>
public enum LocalModelDownloadState
{
    /// <summary>正在下载。</summary>
    Running,

    /// <summary>用户暂停（残片留在磁盘上，下次续传）。</summary>
    Paused,

    /// <summary>下载并安装完成。</summary>
    Completed,

    /// <summary>失败（网络、写盘、校验）。</summary>
    Failed,
}

/// <summary>
/// 本地模型下载的可观察快照。事件在后台线程触发，订阅方负责切回自己的线程。
/// </summary>
/// <param name="Kind">模型种类。</param>
/// <param name="State">阶段。</param>
/// <param name="BytesReceived">已下载字节数。</param>
/// <param name="TotalBytes">总字节数；0 表示还没收到第一份进度。</param>
/// <param name="CurrentFileName">正在下载的文件名。</param>
/// <param name="Error">失败原因（已脱敏）；成功或进行中为 null。</param>
public sealed record LocalModelDownloadSnapshot(
    LocalTranslationModelKind Kind,
    LocalModelDownloadState State,
    long BytesReceived,
    long TotalBytes,
    string? CurrentFileName,
    string? Error)
{
    /// <summary>0 到 100 的整数百分比；总长未知时为 0。</summary>
    public int Percent => TotalBytes > 0
        ? (int)Math.Round(100d * Math.Clamp((double)BytesReceived / TotalBytes, 0d, 1d))
        : 0;

    /// <summary>是否正在下载。</summary>
    public bool IsRunning => State == LocalModelDownloadState.Running;
}

/// <summary>
/// 本地翻译模型下载的归属者：下载从「设置页」挪到 App 层。
/// <para>
/// 起因是一个 1.31 GB 的 Index-Translate 模型：页面不做缓存，离开设置页会取消页面令牌
/// （<c>SettingsPage.OnUnloaded</c>），回来时模型行已经重建——下载既看不到进度也活不下来。
/// 这里按「每种模型一个 job」单飞，页面只订阅快照，切走再回来从 <see cref="Active"/> 接上进度。
/// </para>
/// <para>
/// 暂停 = 取消令牌但<b>保留</b> <c>.part</c>：下次点「下载」由 Core 从断点续传，
/// 所以界面上这个按钮叫「暂停」而不是「取消」——它不会丢掉已经下完的 1 GB。
/// </para>
/// </summary>
public sealed class LocalModelDownloadCoordinator : IDisposable
{
    private readonly Func<
        LocalTranslationModelKind,
        IProgress<LocalTranslationDownloadProgress>,
        CancellationToken,
        Task<LocalTranslationModelDownloadResult>> _download;

    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<LocalTranslationModelKind, Job> _jobs = new();
    private bool _disposed;

    /// <summary>创建协调器。</summary>
    /// <param name="download">真正执行下载的委托（通常是 <c>LocalTranslationRuntime.DownloadAsync</c>）。</param>
    /// <param name="logger">日志。</param>
    public LocalModelDownloadCoordinator(
        Func<
            LocalTranslationModelKind,
            IProgress<LocalTranslationDownloadProgress>,
            CancellationToken,
            Task<LocalTranslationModelDownloadResult>> download,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(download);
        ArgumentNullException.ThrowIfNull(logger);
        _download = download;
        _logger = logger;
    }

    /// <summary>快照有变化（在后台线程触发；订阅方自己 <c>DispatcherQueue.TryEnqueue</c>）。</summary>
    public event EventHandler<LocalModelDownloadSnapshot>? Changed;

    /// <summary>正在下载或已暂停的 job（不含已结束的）。</summary>
    public IReadOnlyList<LocalModelDownloadSnapshot> Active
    {
        get
        {
            lock (_gate)
            {
                return _jobs.Values
                    .Where(job => job.Snapshot.State is LocalModelDownloadState.Running or LocalModelDownloadState.Paused)
                    .Select(job => job.Snapshot)
                    .ToArray();
            }
        }
    }

    /// <summary>取某个模型的快照；没有 job 时返回 null。</summary>
    /// <param name="kind">模型种类。</param>
    public LocalModelDownloadSnapshot? GetSnapshot(LocalTranslationModelKind kind)
    {
        lock (_gate)
        {
            return _jobs.TryGetValue(kind, out var job) ? job.Snapshot : null;
        }
    }

    /// <summary>
    /// 开始（或从残片续传）某个模型。正在下载时返回 false——连点两下「下载」不该叠出两条请求；
    /// 已暂停的 job 不挡路：点「下载」就是续传，换一个新 job 跑，旧快照随之作废。
    /// </summary>
    /// <param name="kind">模型种类。</param>
    public bool TryStart(LocalTranslationModelKind kind)
    {
        Job job;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            LocalModelDownloadSnapshot? carryOver = null;
            if (_jobs.TryGetValue(kind, out var existing))
            {
                if (existing.Snapshot.State == LocalModelDownloadState.Running)
                {
                    return false;
                }

                // 上一次已结束（完成 / 失败）或已暂停：清掉旧 job，重新开一个。
                // 把旧百分比带进新 job，续传时进度条从断点处接着走，不会先跳回 0。
                carryOver = existing.Snapshot;
                _jobs.Remove(kind);
            }

            job = new Job(kind, carryOver);
            _jobs[kind] = job;
        }

        _ = RunAsync(job);
        return true;
    }

    /// <summary>
    /// 暂停某个模型：取消令牌，但已下载的 <c>.part</c> 保留，下次 <see cref="TryStart"/> 续传。
    /// </summary>
    /// <param name="kind">模型种类。</param>
    public void Pause(LocalTranslationModelKind kind)
    {
        Job? job;
        lock (_gate)
        {
            if (!_jobs.TryGetValue(kind, out job)
                || job.Snapshot.State != LocalModelDownloadState.Running)
            {
                return;
            }
        }

        try
        {
            job.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // job 刚好自己跑完并释放了令牌：没有东西可取消。
        }
    }

    /// <summary>
    /// 忘掉某个模型的旧 job（删除模型后调用）。
    /// <para>
    /// 不这么做的话：暂停过一次的 job 会一直挡着 <see cref="TryStart"/>，
    /// 用户把残片删干净之后再点「下载」会没反应——按钮看起来坏了。
    /// 正在下载的 job 不会被忘掉（界面上这时候也点不到删除）。
    /// </para>
    /// </summary>
    /// <param name="kind">模型种类。</param>
    public void Forget(LocalTranslationModelKind kind)
    {
        lock (_gate)
        {
            if (_jobs.TryGetValue(kind, out var job)
                && job.Snapshot.State == LocalModelDownloadState.Running)
            {
                return;
            }

            _jobs.Remove(kind);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        List<Job> jobs;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            jobs = _jobs.Values.ToList();
            _jobs.Clear();
        }

        foreach (var job in jobs)
        {
            try
            {
                // 只取消不释放：令牌的 Dispose 由 RunAsync 的 finally 负责，
                // 在这里抢先释放会让在途的 Task.Delay 注册抛 ObjectDisposedException。
                job.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 已经跑完的 job 不需要再取消。
            }
        }
    }

    /// <summary>
    /// 跑完一次下载并把结果收敛成快照。本方法自己吞掉所有异常（唯一的出口是 <see cref="Changed"/>），
    /// 所以可以安全地用 <c>_ = RunAsync(job)</c> 点火。
    /// </summary>
    private async Task RunAsync(Job job)
    {
        try
        {
            var result = await _download(job.Kind, new JobProgress(this, job), job.Cancellation.Token)
                .ConfigureAwait(false);

            if (result.Success)
            {
                _logger.Info($"本地模型下载完成：{job.Kind}（{result.DownloadedBytes} 字节）");
                Publish(job, job.Snapshot with
                {
                    State = LocalModelDownloadState.Completed,
                    BytesReceived = job.Snapshot.TotalBytes,
                    CurrentFileName = null,
                    Error = null,
                });
            }
            else
            {
                _logger.Warn($"本地模型下载失败：{job.Kind}（{result.Message}）");
                Publish(job, job.Snapshot with
                {
                    State = LocalModelDownloadState.Failed,
                    CurrentFileName = null,
                    Error = result.Message,
                });
            }
        }
        catch (OperationCanceledException)
        {
            // 暂停是正常路径：残片留着续传，不写 error 级日志。
            _logger.Info($"本地模型下载已暂停：{job.Kind}（已下载 {job.Snapshot.BytesReceived} 字节，下次续传）");
            Publish(job, job.Snapshot with
            {
                State = LocalModelDownloadState.Paused,
                CurrentFileName = null,
                Error = null,
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"本地模型下载失败：{job.Kind}", ex);
            Publish(job, job.Snapshot with
            {
                State = LocalModelDownloadState.Failed,
                CurrentFileName = null,
                Error = ex.Message,
            });
        }
        finally
        {
            job.Cancellation.Dispose();
        }
    }

    /// <summary>把进度写成 Running 快照。由下载线程同步调用，所以事件顺序与进度顺序一致。</summary>
    private void OnProgress(Job job, LocalTranslationDownloadProgress progress) =>
        Publish(job, new LocalModelDownloadSnapshot(
            job.Kind,
            LocalModelDownloadState.Running,
            progress.BytesReceived,
            progress.TotalBytes,
            progress.CurrentFileName,
            null));

    /// <summary>
    /// 落快照并广播。旧 job 的迟到进度会被丢弃：否则「暂停后立刻重开」时，
    /// 上一次的残影会把新 job 的进度覆盖回旧数字。
    /// </summary>
    private void Publish(Job job, LocalModelDownloadSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed
                || !_jobs.TryGetValue(job.Kind, out var current)
                || !ReferenceEquals(current, job))
            {
                return;
            }

            job.Snapshot = snapshot;
        }

        // 订阅方（页面）抛异常不能反过来把下载判成失败：进度通知是旁路，
        // 它出错只该记日志，不该打断正在读写 1.31 GB 的那个循环。
        try
        {
            Changed?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            _logger.Error("本地模型下载进度订阅方抛异常", ex);
        }
    }

    /// <summary>一次下载。可变状态都收在这里，避免协调器本体到处散快照字段。</summary>
    private sealed class Job
    {
        public Job(LocalTranslationModelKind kind, LocalModelDownloadSnapshot? carryOver = null)
        {
            Kind = kind;
            Cancellation = new CancellationTokenSource();
            Snapshot = new LocalModelDownloadSnapshot(
                kind,
                LocalModelDownloadState.Running,
                carryOver?.BytesReceived ?? 0,
                carryOver?.TotalBytes ?? 0,
                carryOver?.CurrentFileName,
                null);
        }

        public LocalTranslationModelKind Kind { get; }

        public CancellationTokenSource Cancellation { get; }

        public LocalModelDownloadSnapshot Snapshot { get; set; }
    }

    /// <summary>把 Core 的进度回调同步转给协调器（不经过 <c>Progress&lt;T&gt;</c>，避免回调乱序）。</summary>
    private sealed class JobProgress : IProgress<LocalTranslationDownloadProgress>
    {
        private readonly LocalModelDownloadCoordinator _owner;
        private readonly Job _job;

        public JobProgress(LocalModelDownloadCoordinator owner, Job job)
        {
            _owner = owner;
            _job = job;
        }

        public void Report(LocalTranslationDownloadProgress value) => _owner.OnProgress(_job, value);
    }
}
