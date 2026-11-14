namespace PdxLauncher.Checks;

using System.Diagnostics;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 本地模型下载协调器的自检：单飞、进度快照、暂停保留进度、完成后可重开、失败收敛、退出取消。
/// <para>
/// 它决定「切走设置页再回来还能接着看进度」这件事成不成立：单飞漏了会叠请求，
/// 暂停没收敛会让行一直显示「下载中」，忘记 Forget 会让删掉残片后再点「下载」没反应。
/// 这些都只在真机上表现为「按钮坏了」，所以由本类用假下载委托直接钉住（不联网、不写盘）。
/// </para>
/// </summary>
public static class LocalModelDownloadCoordinatorChecks
{
    private const LocalTranslationModelKind Kind = LocalTranslationModelKind.IndexTranslate;

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        CheckSingleFlightAndProgress();
        CheckPauseAndResume();
        CheckCompletedAndFailed();
        CheckUnexpectedExceptionBecomesFailed();
        CheckForgetClearsPausedJob();
        CheckDisposeCancelsRunning();
        return ChecksContext.Failures - before;
    }

    /// <summary>单飞 + 进度快照：连点两下只起一个 job，进度与百分比如实落到快照上。</summary>
    private static void CheckSingleFlightAndProgress()
    {
        var download = new FakeDownload();
        using var coordinator = new LocalModelDownloadCoordinator(download.Run, new NullLogger());
        var seen = new List<LocalModelDownloadSnapshot>();
        coordinator.Changed += (_, snapshot) =>
        {
            lock (seen)
            {
                seen.Add(snapshot);
            }
        };

        ChecksContext.Check("下载协调器: 首次 TryStart 返回 true", coordinator.TryStart(Kind));
        ChecksContext.Check("下载协调器: 第二次 TryStart 返回 false（不叠请求）", !coordinator.TryStart(Kind));
        ChecksContext.Equal("下载协调器: 只拉起了一次下载", 1, download.Calls);
        ChecksContext.Equal(
            "下载协调器: 刚起步时状态是 Running",
            LocalModelDownloadState.Running,
            coordinator.GetSnapshot(Kind)?.State);

        download.LastProgress!.Report(new LocalTranslationDownloadProgress(512, 1024, "model.bin"));
        ChecksContext.Check(
            "下载协调器: 50% 进度落到快照",
            WaitUntil(() => coordinator.GetSnapshot(Kind)?.Percent == 50),
            "实际 " + (coordinator.GetSnapshot(Kind)?.Percent.ToString() ?? "null"));
        ChecksContext.Check(
            "下载协调器: 快照带上了当前文件名",
            coordinator.GetSnapshot(Kind)?.CurrentFileName == "model.bin");
        ChecksContext.Equal("下载协调器: Active 里有这一个 job", 1, coordinator.Active.Count);

        download.Completion.SetResult(new LocalTranslationModelDownloadResult(true, "ok", 512));
        ChecksContext.Check(
            "下载协调器: 完成后状态是 Completed",
            WaitUntil(() => coordinator.GetSnapshot(Kind)?.State == LocalModelDownloadState.Completed));
        ChecksContext.Equal("下载协调器: 完成后 Active 清空", 0, coordinator.Active.Count);

        lock (seen)
        {
            ChecksContext.Check(
                "下载协调器: 进度变化都广播了",
                seen.Any(item => item.State == LocalModelDownloadState.Running && item.Percent == 50),
                "共 " + seen.Count + " 条");
        }
    }

    /// <summary>暂停：令牌被取消、状态收敛成 Paused、进度百分比保留，并且允许再次 TryStart 续传。</summary>
    private static void CheckPauseAndResume()
    {
        var download = new FakeDownload();
        using var coordinator = new LocalModelDownloadCoordinator(download.Run, new NullLogger());

        coordinator.TryStart(Kind);
        download.LastProgress!.Report(new LocalTranslationDownloadProgress(430, 1000, "model.bin"));
        WaitUntil(() => coordinator.GetSnapshot(Kind)?.Percent == 43);

        coordinator.Pause(Kind);
        ChecksContext.Check(
            "下载协调器: 暂停后状态是 Paused",
            WaitUntil(() => coordinator.GetSnapshot(Kind)?.State == LocalModelDownloadState.Paused),
            "实际 " + (coordinator.GetSnapshot(Kind)?.State.ToString() ?? "null"));
        ChecksContext.Check("下载协调器: 暂停会取消下载令牌", download.LastToken.IsCancellationRequested);
        ChecksContext.Equal(
            "下载协调器: 暂停后保留已下载百分比（界面显示「已暂停 N%」）",
            43,
            coordinator.GetSnapshot(Kind)?.Percent);
        ChecksContext.Equal(
            "下载协调器: 暂停的 job 仍在 Active 里（进页时能接上）",
            1,
            coordinator.Active.Count);

        ChecksContext.Check("下载协调器: 暂停后允许再次下载（续传）", coordinator.TryStart(Kind));
        ChecksContext.Equal("下载协调器: 续传是第二次调用下载委托", 2, download.Calls);
        ChecksContext.Equal(
            "下载协调器: 续传后回到 Running",
            LocalModelDownloadState.Running,
            coordinator.GetSnapshot(Kind)?.State);
        ChecksContext.Equal(
            "下载协调器: 续传时进度从断点处接着算（不先跳回 0）",
            43,
            coordinator.GetSnapshot(Kind)?.Percent);
    }

    /// <summary>成功 / 失败都要收敛：失败带原因；两种结束态都允许重新开一个 job。</summary>
    private static void CheckCompletedAndFailed()
    {
        var download = new FakeDownload();
        using var coordinator = new LocalModelDownloadCoordinator(download.Run, new NullLogger());

        coordinator.TryStart(Kind);
        download.Completion.SetResult(new LocalTranslationModelDownloadResult(false, "下载 x 失败：校验失败", 0));
        ChecksContext.Check(
            "下载协调器: 失败状态是 Failed",
            WaitUntil(() => coordinator.GetSnapshot(Kind)?.State == LocalModelDownloadState.Failed));
        ChecksContext.Check(
            "下载协调器: 失败原因带进快照",
            coordinator.GetSnapshot(Kind)?.Error?.Contains("校验失败", StringComparison.Ordinal) == true,
            coordinator.GetSnapshot(Kind)?.Error ?? "(null)");
        ChecksContext.Equal("下载协调器: 失败后 Active 清空", 0, coordinator.Active.Count);
        ChecksContext.Check("下载协调器: 失败后允许重试", coordinator.TryStart(Kind));
    }

    /// <summary>下载委托直接抛异常（不是返回失败结果）时也必须收敛成 Failed，不能把异常抛给点火方。</summary>
    private static void CheckUnexpectedExceptionBecomesFailed()
    {
        static Task<LocalTranslationModelDownloadResult> Throwing(
            LocalTranslationModelKind kind,
            IProgress<LocalTranslationDownloadProgress> progress,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");

        using var coordinator = new LocalModelDownloadCoordinator(Throwing, new NullLogger());
        ChecksContext.Check("下载协调器: 委托抛异常时 TryStart 仍返回 true", coordinator.TryStart(Kind));
        ChecksContext.Check(
            "下载协调器: 委托抛异常收敛成 Failed",
            WaitUntil(() => coordinator.GetSnapshot(Kind)?.State == LocalModelDownloadState.Failed));
        ChecksContext.Check(
            "下载协调器: 异常信息进了快照",
            coordinator.GetSnapshot(Kind)?.Error?.Contains("boom", StringComparison.Ordinal) == true,
            coordinator.GetSnapshot(Kind)?.Error ?? "(null)");
    }

    /// <summary>删除模型后 Forget：残留的「已暂停 N%」不再回放到行上，且能重新下载。</summary>
    private static void CheckForgetClearsPausedJob()
    {
        var download = new FakeDownload();
        using var coordinator = new LocalModelDownloadCoordinator(download.Run, new NullLogger());

        coordinator.TryStart(Kind);
        coordinator.Pause(Kind);
        WaitUntil(() => coordinator.GetSnapshot(Kind)?.State == LocalModelDownloadState.Paused);
        ChecksContext.Check(
            "下载协调器: 暂停的 job 会把「已暂停」留在快照里（进页时会回放）",
            coordinator.GetSnapshot(Kind)?.State == LocalModelDownloadState.Paused);

        coordinator.Forget(Kind);
        ChecksContext.Check("下载协调器: Forget 后快照为空", coordinator.GetSnapshot(Kind) is null);
        ChecksContext.Equal("下载协调器: Forget 后不再出现在 Active 里", 0, coordinator.Active.Count);
        ChecksContext.Check("下载协调器: Forget 后可以重新下载", coordinator.TryStart(Kind));
    }

    /// <summary>退出应用：Dispose 取消在途下载，之后的 TryStart 一律拒绝。</summary>
    private static void CheckDisposeCancelsRunning()
    {
        var download = new FakeDownload();
        var coordinator = new LocalModelDownloadCoordinator(download.Run, new NullLogger());

        coordinator.TryStart(Kind);
        coordinator.Dispose();
        ChecksContext.Check("下载协调器: Dispose 取消在途下载", download.LastToken.IsCancellationRequested);
        ChecksContext.Check("下载协调器: Dispose 后不再接受新下载", !coordinator.TryStart(Kind));
        ChecksContext.Equal("下载协调器: Dispose 后可重复调用", 0, DisposeTwice(coordinator));
    }

    private static int DisposeTwice(LocalModelDownloadCoordinator coordinator)
    {
        try
        {
            coordinator.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  (Dispose 二次调用抛出：" + ex.GetType().Name + ")");
            return 1;
        }
    }

    /// <summary>轮询等待一个条件成立（后台任务收敛用）；超时返回 false。</summary>
    private static bool WaitUntil(Func<bool> condition, int timeoutMilliseconds = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return condition();
    }

    /// <summary>假的下载委托：Run 同步登记进度回调与令牌，测试自己决定何时完成 / 取消。</summary>
    private sealed class FakeDownload
    {
        private int _calls;

        public TaskCompletionSource<LocalTranslationModelDownloadResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken LastToken { get; private set; }

        public IProgress<LocalTranslationDownloadProgress>? LastProgress { get; private set; }

        public int Calls => Volatile.Read(ref _calls);

        public Task<LocalTranslationModelDownloadResult> Run(
            LocalTranslationModelKind kind,
            IProgress<LocalTranslationDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            LastProgress = progress;
            LastToken = cancellationToken;
            return AwaitAsync(cancellationToken);
        }

        private async Task<LocalTranslationModelDownloadResult> AwaitAsync(CancellationToken cancellationToken)
        {
            var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var finished = await Task.WhenAny(Completion.Task, cancelled).ConfigureAwait(false);
            if (!ReferenceEquals(finished, Completion.Task))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return await Completion.Task.ConfigureAwait(false);
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
