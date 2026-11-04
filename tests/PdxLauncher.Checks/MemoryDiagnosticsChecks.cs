namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// Debug 内存埋点的自检：描述行的口径（MB 一位小数、缓存计数拼法）、Log 只写一条带标签的 Info、
/// Capture 在任何环境下都不抛。埋点错了不会崩，只会让 <c>docs/MEMORY-BASELINE.md</c> 的读数不可信，
/// 所以字符串格式与失败兜底要在这里钉住。
/// </summary>
public static class MemoryDiagnosticsChecks
{
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;

        var snapshot = new MemoryDiagnostics.MemorySnapshot(
            ManagedBytes: 44_000_000,
            WorkingSetBytes: 280_000_000,
            PrivateBytes: 210_000_000,
            Gen0: 1,
            Gen1: 2,
            Gen2: 3,
            WorkshopItems: 542,
            WorkshopPages: 11,
            ModThumbnails: 37);

        var text = MemoryDiagnostics.Describe(snapshot);

        ChecksContext.Check("内存诊断：描述含「托管」", text.Contains("托管", StringComparison.Ordinal), text);
        ChecksContext.Check("内存诊断：描述含「工作集」", text.Contains("工作集", StringComparison.Ordinal));
        ChecksContext.Check("内存诊断：描述含「工坊条目」", text.Contains("工坊条目", StringComparison.Ordinal));
        ChecksContext.Check("内存诊断：托管 MB 一位小数（44 MB → 42.0 MB）", text.Contains("42.0 MB", StringComparison.Ordinal));
        ChecksContext.Check("内存诊断：工作集换算（280 MB → 267.0 MB）", text.Contains("267.0 MB", StringComparison.Ordinal));
        ChecksContext.Check(
            "内存诊断：缓存计数拼法（条目 / 分页 / 缩略图）",
            text.Contains("工坊条目 542 / 分页 11 / mod 缩略图 37", StringComparison.Ordinal));
        ChecksContext.Check("内存诊断：GC 次数拼法", text.Contains("GC 0/1/2=1/2/3", StringComparison.Ordinal));
        ChecksContext.Check("内存诊断：描述是单行", !text.Contains('\n'));
        ChecksContext.Equal(
            "内存诊断：完整描述格式逐字不变",
            "内存：托管 42.0 MB / 工作集 267.0 MB / 私有 200.3 MB；GC 0/1/2=1/2/3；工坊条目 542 / 分页 11 / mod 缩略图 37",
            text);
        ChecksContext.Equal("内存诊断：峰值默认 0", 0L, snapshot.PeakWorkingSetBytes);
        ChecksContext.Check(
            "内存诊断：线程/句柄/运行时长默认 0",
            snapshot.ThreadCount == 0 && snapshot.HandleCount == 0 && snapshot.Uptime == TimeSpan.Zero);

        var logger = new RecordingLogger();
        MemoryDiagnostics.Log(logger, "启动完成", () => (7, 3, 5));

        ChecksContext.Equal("内存诊断：Log 只写一条 Info", 1, logger.Infos.Count);
        ChecksContext.Check(
            "内存诊断：Info 以 [tag] 开头",
            logger.Infos.Count == 1 && logger.Infos[0].StartsWith("[启动完成]", StringComparison.Ordinal),
            logger.Infos.Count == 1 ? logger.Infos[0] : "没有 Info");
        ChecksContext.Check(
            "内存诊断：Log 透传缓存计数",
            logger.Infos.Count == 1 && logger.Infos[0].Contains("工坊条目 7 / 分页 3 / mod 缩略图 5", StringComparison.Ordinal));
        ChecksContext.Equal("内存诊断：正常路径不产生 Warn", 0, logger.Warnings.Count);

        try
        {
            var live = MemoryDiagnostics.Capture();
            ChecksContext.Check("内存诊断：Capture 不抛异常", true);
            ChecksContext.Check(
                "内存诊断：未给计数委托时缓存记 0",
                live.WorkshopItems == 0 && live.WorkshopPages == 0 && live.ModThumbnails == 0);
            ChecksContext.Check(
                "内存诊断：真实快照可描述",
                MemoryDiagnostics.Describe(live).StartsWith("内存：托管 ", StringComparison.Ordinal));
            ChecksContext.Check("内存诊断：真实峰值非负", live.PeakWorkingSetBytes > 0);
            ChecksContext.Check("内存诊断：真实线程数非负", live.ThreadCount > 0);
            ChecksContext.Check("内存诊断：真实句柄数非负", live.HandleCount > 0);
            ChecksContext.Check("内存诊断：真实运行时长非负", live.Uptime > TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            ChecksContext.Check("内存诊断：Capture 不抛异常", false, ex.Message);
        }

        var failing = new RecordingLogger();
        MemoryDiagnostics.Log(failing, "失败路径", () => throw new InvalidOperationException("计数不可用"));

        ChecksContext.Check(
            "内存诊断：计数委托抛异常时仍写出快照",
            failing.Infos.Count == 1 && failing.Infos[0].Contains("工坊条目 0", StringComparison.Ordinal));
        ChecksContext.Equal("内存诊断：计数委托失败不产生 Warn", 0, failing.Warnings.Count);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>只记录消息的假日志，用来确认埋点写了几条、写了什么。</summary>
    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Infos { get; } = [];

        public List<string> Warnings { get; } = [];

        public void Info(string message) => Infos.Add(message);

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null) => Warnings.Add(message);
    }
}
