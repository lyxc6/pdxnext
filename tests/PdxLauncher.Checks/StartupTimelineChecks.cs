namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 启动计时的自检：同名幂等、累计耗时单调、上限 32 条、快照是拷贝、空白键忽略。
/// 它错了不会崩，只会在性能页把「启动慢」算错，所以口径由这里钉住。
/// </summary>
public static class StartupTimelineChecks
{
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;

        StartupTimeline.Reset();
        ChecksContext.Equal("启动计时：重置后为空", 0, StartupTimeline.Snapshot().Count);

        StartupTimeline.Mark(StartupTimeline.AppStart);
        StartupTimeline.Mark(StartupTimeline.ServicesReady);

        var marks = StartupTimeline.Snapshot();
        ChecksContext.Equal("启动计时：记录两个里程碑", 2, marks.Count);
        ChecksContext.Equal("启动计时：首条是进程启动", StartupTimeline.AppStart, marks[0].Milestone);
        ChecksContext.Check(
            "启动计时：累计耗时单调不减",
            marks[0].ElapsedMilliseconds <= marks[1].ElapsedMilliseconds,
            marks[0].ElapsedMilliseconds + " -> " + marks[1].ElapsedMilliseconds);
        ChecksContext.Check("启动计时：耗时非负", marks[0].ElapsedMilliseconds >= 0);

        StartupTimeline.Mark(StartupTimeline.AppStart);
        ChecksContext.Equal("启动计时：同名里程碑只记一次", 2, StartupTimeline.Snapshot().Count);

        StartupTimeline.Mark("  ");
        ChecksContext.Equal("启动计时：空白键被忽略", 2, StartupTimeline.Snapshot().Count);

        var first = StartupTimeline.Snapshot();
        var second = StartupTimeline.Snapshot();
        ChecksContext.Check("启动计时：快照是拷贝", !ReferenceEquals(first, second));

        StartupTimeline.Reset();
        for (int index = 0; index < 40; index++)
        {
            StartupTimeline.Mark("milestone-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        ChecksContext.Equal("启动计时：达到上限后停止记录", 32, StartupTimeline.Snapshot().Count);
        StartupTimeline.Reset();
        ChecksContext.Equal("启动计时：可再次重置", 0, StartupTimeline.Snapshot().Count);

        return ChecksContext.Failures == before ? 0 : 1;
    }
}
