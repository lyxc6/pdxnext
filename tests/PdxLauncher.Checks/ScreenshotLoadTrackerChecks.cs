namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 截图逐张结算计数器自检。工坊详情顶部的「正在下载截图…」现在只由它驱动：
/// 少结算一张，提示会永远转下去；多结算一张，提示会在截图还没下完时提前消失；
/// 换了条目却没重新 Begin，Pending 会把上一批的账算到这一批头上。
/// 这些都不会崩，编译器也看不见，所以口径必须钉在这里。
/// </summary>
public static class ScreenshotLoadTrackerChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckEmptyBegin();
        CheckSettleAll();
        CheckOverSettle();
        CheckFinishAll();
        CheckBeginResets();
        CheckReset();
        CheckNegativeBegin();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckEmptyBegin()
    {
        var tracker = new ScreenshotLoadTracker();

        ChecksContext.Check("截图结算：新建后不在途", !tracker.IsBusy);
        ChecksContext.Equal("截图结算：新建后 Total 为 0", 0, tracker.Total);
        ChecksContext.Equal("截图结算：新建后 Pending 为 0", 0, tracker.Pending);

        tracker.Begin(0);
        ChecksContext.Check("截图结算：Begin(0) 不在途", !tracker.IsBusy);
        ChecksContext.Equal("截图结算：Begin(0) 后 Pending 为 0", 0, tracker.Pending);
        ChecksContext.Check("截图结算：没有条目时 Settle 不推进", !tracker.Settle());
    }

    private static void CheckSettleAll()
    {
        var tracker = new ScreenshotLoadTracker();
        tracker.Begin(4);

        ChecksContext.Check("截图结算：Begin(4) 在途", tracker.IsBusy);
        ChecksContext.Equal("截图结算：Begin(4) 后 Pending 为 4", 4, tracker.Pending);

        for (var i = 0; i < 4; i++)
        {
            ChecksContext.Check($"截图结算：第 {i + 1} 张 Settle 推进", tracker.Settle());
        }

        ChecksContext.Check("截图结算：全部结算后不在途", !tracker.IsBusy);
        ChecksContext.Equal("截图结算：全部结算后 Pending 为 0", 0, tracker.Pending);
        ChecksContext.Equal("截图结算：全部结算后 Settled 等于 Total", 4, tracker.Settled);
    }

    private static void CheckOverSettle()
    {
        var tracker = new ScreenshotLoadTracker();
        tracker.Begin(2);
        tracker.Settle();
        tracker.Settle();

        ChecksContext.Check("截图结算：重复 Settle 返回 false", !tracker.Settle());
        ChecksContext.Equal("截图结算：重复 Settle 不让 Settled 越界", 2, tracker.Settled);
        ChecksContext.Equal("截图结算：重复 Settle 后 Pending 仍为 0", 0, tracker.Pending);
    }

    private static void CheckFinishAll()
    {
        var tracker = new ScreenshotLoadTracker();
        tracker.Begin(5);
        tracker.Settle();

        tracker.FinishAll();

        ChecksContext.Check("截图结算：FinishAll 直接收尾", !tracker.IsBusy);
        ChecksContext.Equal("截图结算：FinishAll 把 Settled 拉到 Total", 5, tracker.Settled);
        ChecksContext.Equal("截图结算：FinishAll 后 Pending 为 0", 0, tracker.Pending);
    }

    private static void CheckBeginResets()
    {
        var tracker = new ScreenshotLoadTracker();
        tracker.Begin(3);
        tracker.Settle();
        tracker.Settle();

        tracker.Begin(2);

        ChecksContext.Equal("截图结算：再次 Begin 清零上一批的 Settled", 0, tracker.Settled);
        ChecksContext.Equal("截图结算：再次 Begin 采用新的 Total", 2, tracker.Total);
        ChecksContext.Equal("截图结算：再次 Begin 后 Pending 为新 Total", 2, tracker.Pending);
    }

    private static void CheckReset()
    {
        var tracker = new ScreenshotLoadTracker();
        tracker.Begin(3);
        tracker.Settle();

        tracker.Reset();

        ChecksContext.Equal("截图结算：Reset 清零 Total", 0, tracker.Total);
        ChecksContext.Equal("截图结算：Reset 清零 Settled", 0, tracker.Settled);
        ChecksContext.Check("截图结算：Reset 后不在途", !tracker.IsBusy);
    }

    private static void CheckNegativeBegin()
    {
        var tracker = new ScreenshotLoadTracker();
        tracker.Begin(-3);

        ChecksContext.Equal("截图结算：Begin(负数) 钳成 0", 0, tracker.Total);
        ChecksContext.Equal("截图结算：Begin(负数) 后 Pending 为 0", 0, tracker.Pending);
        ChecksContext.Check("截图结算：Begin(负数) 不在途", !tracker.IsBusy);
    }
}
