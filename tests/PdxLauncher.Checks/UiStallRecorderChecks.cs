namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// UI 线程停顿记录器自检。看门狗本身依赖 DispatcherQueue（控制台里造不出来），
/// 但「多大的间隔算一次停顿、累计怎么算、面包屑留几条」全在这里定死：
/// 判错一次，日志就会在每次正常心跳时刷「停顿 300 ms」，把真正的冻结淹掉。
/// </summary>
public static class UiStallRecorderChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckNormalHeartbeats();
        CheckSingleFreeze();
        CheckConsecutiveSlowTicks();
        CheckBelowThreshold();
        CheckBreadcrumbs();
        CheckBreadcrumbCapacity();
        CheckReset();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckNormalHeartbeats()
    {
        var recorder = new UiStallRecorder();

        for (var i = 0; i < 20; i++)
        {
            ChecksContext.Check("UI 停顿：正常心跳不上报", recorder.Observe(100) is null);
        }

        ChecksContext.Equal("UI 停顿：正常心跳后计数为 0", 0, recorder.StallCount);
        ChecksContext.Equal("UI 停顿：正常心跳后最长停顿为 0", 0, recorder.MaxStallMs);
    }

    private static void CheckSingleFreeze()
    {
        var recorder = new UiStallRecorder();

        ChecksContext.Check("UI 停顿：卡住期间不上报", recorder.Observe(5000) is null);

        var stall = recorder.Observe(100);
        ChecksContext.Check("UI 停顿：恢复心跳时上报一次", stall is not null);
        ChecksContext.Equal("UI 停顿：时长等于累计间隔", 5000L, stall?.DurationMs ?? -1);
        ChecksContext.Check("UI 停顿：5 秒算严重", stall?.IsSevere == true);
        ChecksContext.Equal("UI 停顿：上报后计数 +1", 1, recorder.StallCount);
        ChecksContext.Equal("UI 停顿：最长停顿被记住", 5000L, recorder.MaxStallMs);
    }

    private static void CheckConsecutiveSlowTicks()
    {
        var recorder = new UiStallRecorder();
        recorder.Observe(400);
        recorder.Observe(600);

        var stall = recorder.Observe(100);

        ChecksContext.Equal("UI 停顿：连续慢心跳累计成一次", 1000L, stall?.DurationMs ?? -1);
        ChecksContext.Equal("UI 停顿：累计只算一次", 1, recorder.StallCount);
        ChecksContext.Check("UI 停顿：1000 ms 不算严重", stall?.IsSevere == false);
    }

    private static void CheckBelowThreshold()
    {
        var recorder = new UiStallRecorder();

        recorder.Observe(UiStallRecorder.WarnThresholdMs - 1);
        ChecksContext.Check("UI 停顿：低于阈值不上报", recorder.Observe(100) is null);

        recorder.Observe(UiStallRecorder.WarnThresholdMs);
        ChecksContext.Check("UI 停顿：正好等于阈值算一次", recorder.Observe(100) is not null);
    }

    private static void CheckBreadcrumbs()
    {
        var recorder = new UiStallRecorder();
        recorder.Breadcrumb("工坊 开始加载");
        recorder.Breadcrumb("工坊 补封面 30 张");

        ChecksContext.Equal(
            "UI 停顿：面包屑按先后拼接",
            "工坊 开始加载 ← 工坊 补封面 30 张",
            recorder.Snapshot());

        recorder.Breadcrumb("  ");
        ChecksContext.Equal("UI 停顿：空白面包屑被忽略", 2, recorder.Snapshot().Split(" ← ").Length);
    }

    private static void CheckBreadcrumbCapacity()
    {
        var recorder = new UiStallRecorder();
        for (var i = 1; i <= UiStallRecorder.BreadcrumbCapacity + 3; i++)
        {
            recorder.Breadcrumb("步骤 " + i);
        }

        var lines = recorder.Snapshot().Split(" ← ");
        ChecksContext.Equal("UI 停顿：面包屑数量有上限", UiStallRecorder.BreadcrumbCapacity, lines.Length);
        ChecksContext.Equal("UI 停顿：面包屑丢最旧的", "步骤 4", lines[0]);
        ChecksContext.Equal(
            "UI 停顿：面包屑保留最新的",
            "步骤 " + (UiStallRecorder.BreadcrumbCapacity + 3),
            lines[^1]);
    }

    private static void CheckReset()
    {
        var recorder = new UiStallRecorder();
        recorder.Breadcrumb("工坊");
        recorder.Observe(4000);
        recorder.Observe(100);

        recorder.Reset();

        ChecksContext.Equal("UI 停顿：Reset 清空计数", 0, recorder.StallCount);
        ChecksContext.Equal("UI 停顿：Reset 清空最长停顿", 0L, recorder.MaxStallMs);
        ChecksContext.Equal("UI 停顿：Reset 清空面包屑", string.Empty, recorder.Snapshot());
    }
}
