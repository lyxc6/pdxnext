namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 在途请求合并（<see cref="RequestCoalescer"/>）的自检：
/// 同一个键只跑一次工厂、不同键不合并、异常广播给等待者、完成后条目清理干净。
/// <para>
/// 起因：详情页会同时存在「点两次、翻页取消后重建」这类并发调用，
/// 同一个地址被发两遍只会让 Steam 更快触发限流。
/// </para>
/// </summary>
public static class RequestCoalescerChecks
{
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;
        CheckSameKeyRunsOnce();
        CheckDifferentKeysDoNotMerge();
        CheckExceptionReachesFollowers();
        CheckEntriesCleanedUp();
        return ChecksContext.Failures - before;
    }

    /// <summary>两个并发调用同一个键，工厂只执行一次、两边拿到同一个结果。</summary>
    private static void CheckSameKeyRunsOnce()
    {
        var coalescer = new RequestCoalescer();
        var calls = 0;
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<string> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return ReleaseAsync();

            async Task<string> ReleaseAsync()
            {
                await release.Task.ConfigureAwait(false);
                return "payload";
            }
        }

        var first = coalescer.RunAsync("item:1", Factory, CancellationToken.None);
        var second = coalescer.RunAsync("item:1", Factory, CancellationToken.None);

        ChecksContext.Equal("合并：两个调用只算一个在途请求", 1, coalescer.InFlight);

        // 让两个调用者都进入等待，再放开工厂。
        Thread.Sleep(20);
        release.SetResult(true);

        ChecksContext.Equal("合并：工厂只执行一次", 1, Volatile.Read(ref calls));
        ChecksContext.Equal("合并：第一个调用拿到结果", "payload", first.GetAwaiter().GetResult());
        ChecksContext.Equal("合并：第二个调用拿到同一个结果", "payload", second.GetAwaiter().GetResult());
        ChecksContext.Equal("合并：完成后没有残留条目", 0, coalescer.InFlight);
    }

    /// <summary>不同键各自执行，不会被误合并。</summary>
    private static void CheckDifferentKeysDoNotMerge()
    {
        var coalescer = new RequestCoalescer();
        var calls = 0;

        async Task<string> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await Task.Yield();
            return "x";
        }

        var a = coalescer.RunAsync("item:1", Factory, CancellationToken.None).GetAwaiter().GetResult();
        var b = coalescer.RunAsync("item:2", Factory, CancellationToken.None).GetAwaiter().GetResult();

        ChecksContext.Equal("合并：不同键各执行一次", 2, calls);
        ChecksContext.Check("合并：不同键结果独立", a == "x" && b == "x", a + "/" + b);
        ChecksContext.Equal("合并：不同键完成后无残留", 0, coalescer.InFlight);
    }

    /// <summary>工厂抛错时所有等待者都拿到同一个异常，而不是有人挂在等待上。</summary>
    private static void CheckExceptionReachesFollowers()
    {
        var coalescer = new RequestCoalescer();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> Factory(CancellationToken _)
        {
            await release.Task.ConfigureAwait(false);
            throw new InvalidOperationException("boom");
        }

        var first = coalescer.RunAsync("item:9", Factory, CancellationToken.None);
        var second = coalescer.RunAsync("item:9", Factory, CancellationToken.None);
        Thread.Sleep(20);
        release.SetResult(true);

        var firstError = Capture(first);
        var secondError = Capture(second);

        ChecksContext.Check(
            "合并：负责人拿到原始异常",
            firstError is InvalidOperationException { Message: "boom" },
            firstError?.Message ?? "(没有抛异常)");
        ChecksContext.Check(
            "合并：等待者也拿到同一个异常",
            secondError is InvalidOperationException { Message: "boom" },
            secondError?.Message ?? "(没有抛异常)");
        ChecksContext.Equal("合并：失败后无残留条目", 0, coalescer.InFlight);
    }

    /// <summary>完成后同一个键可以重新开始（合并只在途，不做结果缓存）。</summary>
    private static void CheckEntriesCleanedUp()
    {
        var coalescer = new RequestCoalescer();
        var calls = 0;

        Task<int> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(calls);
        }

        var first = coalescer.RunAsync("browse:x", Factory, CancellationToken.None).GetAwaiter().GetResult();
        var second = coalescer.RunAsync("browse:x", Factory, CancellationToken.None).GetAwaiter().GetResult();

        ChecksContext.Equal("合并：任务结束后同名键会重新执行", 2, calls);
        ChecksContext.Equal("合并：两次结果都不为空", true, first == 1 && second == 2);
    }

    private static Exception? Capture(Task task)
    {
        try
        {
            task.GetAwaiter().GetResult();
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ex;
        }
    }
}
