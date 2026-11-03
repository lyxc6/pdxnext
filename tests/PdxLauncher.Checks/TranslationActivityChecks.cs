namespace PdxLauncher.Checks;

using System;
using System.Threading;
using System.Threading.Tasks;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 翻译在途计数器自检。标题栏的「正在翻译…」完全由它驱动：
/// 漏减一次，提示会永远转下去；重复减一次，提示会在翻译没结束时提前消失；
/// 并发下少加或多减，表现同样是「有时看不见提示」。这些都不会崩，
/// 编译器也看不见，所以口径必须钉在这里。
/// </summary>
public static class TranslationActivityChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        try
        {
            CheckSingleLease();
            CheckNestedLeases();
            CheckDoubleDispose();
            CheckConcurrentLeases();
        }
        finally
        {
            // 本类不落盘，没有临时目录需要清理。
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckSingleLease()
    {
        var monitor = new TranslationActivityMonitor();
        var changed = 0;
        monitor.Changed += (_, _) => Interlocked.Increment(ref changed);

        ChecksContext.Check("计数：初始不在途", !monitor.IsActive);

        using (var lease = monitor.Begin())
        {
            ChecksContext.Check("计数：Begin 后在途", monitor.IsActive);
            ChecksContext.Equal("计数：0→1 只发一次 Changed", 1, Volatile.Read(ref changed));
            ChecksContext.Check("计数：Lease 非空", lease is not null);
        }

        ChecksContext.Check("计数：Dispose 后回到不在途", !monitor.IsActive);
        ChecksContext.Equal("计数：1→0 再发一次 Changed", 2, Volatile.Read(ref changed));
    }

    private static void CheckNestedLeases()
    {
        var monitor = new TranslationActivityMonitor();
        var changed = 0;
        monitor.Changed += (_, _) => Interlocked.Increment(ref changed);

        var outer = monitor.Begin();
        var inner = monitor.Begin();

        ChecksContext.Check("嵌套：两层在途", monitor.IsActive);
        ChecksContext.Equal("嵌套：两次 Begin 仍只发一次 0→1", 1, Volatile.Read(ref changed));

        inner.Dispose();
        ChecksContext.Check("嵌套：还剩一层时仍在途", monitor.IsActive);
        ChecksContext.Equal("嵌套：1→0 之前不再发 Changed", 1, Volatile.Read(ref changed));

        outer.Dispose();
        ChecksContext.Check("嵌套：两层都释放后不在途", !monitor.IsActive);
        ChecksContext.Equal("嵌套：全部释放才发一次 1→0", 2, Volatile.Read(ref changed));
    }

    private static void CheckDoubleDispose()
    {
        var monitor = new TranslationActivityMonitor();
        var changed = 0;
        monitor.Changed += (_, _) => Interlocked.Increment(ref changed);

        var first = monitor.Begin();
        var second = monitor.Begin();
        first.Dispose();
        first.Dispose(); // 重复 Dispose 不应把计数减到 0 以下，也不应提前发 1→0
        ChecksContext.Check("重复 Dispose：另一个 Lease 仍在途", monitor.IsActive);
        ChecksContext.Equal("重复 Dispose：没有多发 Changed", 1, Volatile.Read(ref changed));

        second.Dispose();
        ChecksContext.Check("重复 Dispose：全部释放后不在途", !monitor.IsActive);
        ChecksContext.Equal("重复 Dispose：最终只发一次 1→0", 2, Volatile.Read(ref changed));

        // 再 Begin 一次仍能正常进入在途（计数没有被减成负数）。
        using (monitor.Begin())
        {
            ChecksContext.Check("重复 Dispose：之后仍可重新进入在途", monitor.IsActive);
        }
    }

    private static void CheckConcurrentLeases()
    {
        var monitor = new TranslationActivityMonitor();
        var transitions = 0;
        monitor.Changed += (_, _) => Interlocked.Increment(ref transitions);

        // 必须让 64 个租约真的同时在途，否则计数会先归零再升起，
        // 事件对数取决于调度（SpinWait 太短时经常 0 个在途重叠）而不是被测逻辑。
        // 用独立线程 + CountdownEvent 而不是 Parallel.For：池线程互相阻塞时要靠
        // 线程注入（约每 500ms 一个）才能凑齐 64 个，会拖几十秒。
        const int ParallelLeases = 64;
        using var allStarted = new CountdownEvent(ParallelLeases);
        using var mayFinish = new ManualResetEventSlim(false);
        var threads = new Thread[ParallelLeases];
        for (var i = 0; i < ParallelLeases; i++)
        {
            threads[i] = new Thread(() =>
            {
                var lease = monitor.Begin();
                allStarted.Signal();
                allStarted.Wait();
                mayFinish.Wait();
                lease.Dispose();
            });
            threads[i].IsBackground = true;
            threads[i].Start();
        }

        allStarted.Wait();

        // 到这里 64 个租约都在途，期间只该发生一次 0→1。
        ChecksContext.Check("并发：64 个租约在途时 IsActive", monitor.IsActive);
        ChecksContext.Equal("并发：64 个租约只发了一次 0→1", 1, Volatile.Read(ref transitions));

        mayFinish.Set();
        foreach (var thread in threads)
        {
            thread.Join();
        }

        ChecksContext.Check("并发：结束后不在途", !monitor.IsActive);
        ChecksContext.Equal("并发：64 个并行 Lease 只发一对 Changed", 2, Volatile.Read(ref transitions));
    }
}
