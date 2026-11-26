namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.CrashAnalysis;
using PdxLauncher.Core.Mods.Sorting;

/// <summary>
/// PC 侧崩溃取证、database_conflicts 解析与 replace_path 足迹识别自检。
/// 全部纯内存/fake 输入，不读系统事件日志、不写用户目录。
/// </summary>
public static class PcForensicsChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckExceptionCodes();
        CheckEventClassification();
        CheckSystemState();
        CheckDbConflicts();
        CheckReplacePathParsing();
        CheckReplacePathFootprint(fs);
        return ChecksContext.Failures - before;
    }

    private static void CheckExceptionCodes()
    {
        ChecksContext.Check("C0000017 → 内存不足结论",
            PcForensics.FromExceptionCode("C0000017")?.Code == CrashFindingCode.PcOutOfMemoryCrash);
        ChecksContext.Check("C0000006 → 磁盘读取失败结论",
            PcForensics.FromExceptionCode("0xc0000006")?.Code == CrashFindingCode.PcDiskReadCrash);
        ChecksContext.Check("C000001D → 非法指令结论",
            PcForensics.FromExceptionCode("C000001D")?.Code == CrashFindingCode.PcIllegalInstructionCrash);
        ChecksContext.Check("C0000005 不纳入 PC 专属结论",
            PcForensics.FromExceptionCode("C0000005") is null);
        ChecksContext.Check("空崩溃码不产生结论",
            PcForensics.FromExceptionCode(null) is null);
    }

    private static void CheckEventClassification()
    {
        var events = new[]
        {
            new WindowsEventEvidence(WindowsEventKind.ApplicationFaultModule, 1000, "Application Error", null,
                "nvlddmkm.sys", "faulting module nvlddmkm"),
            new WindowsEventEvidence(WindowsEventKind.ApplicationFaultModule, 1000, "Application Error", null,
                "gameoverlayrenderer.dll", "faulting module gameoverlayrenderer"),
            new WindowsEventEvidence(WindowsEventKind.LowVirtualMemory, 2004, "Resource-Exhaustion-Detector", null,
                null, "low virtual memory"),
            new WindowsEventEvidence(WindowsEventKind.GraphicsDriverReset, 4101, "Display", null,
                null, "display driver stopped responding"),
            new WindowsEventEvidence(WindowsEventKind.HardwareError, 18, "Microsoft-Windows-WHEA-Logger", null,
                null, "hardware error"),
            new WindowsEventEvidence(WindowsEventKind.DiskError, 7, "Disk", null, null, "disk error"),
        };

        var findings = PcForensics.FromEvents(events);
        ChecksContext.Check("故障模块 nvidia → 显卡驱动结论",
            findings.Any(f => f.Code == CrashFindingCode.PcGraphicsDriverCrash));
        ChecksContext.Check("故障模块 Steam 覆盖层 → 覆盖层结论",
            findings.Any(f => f.Code == CrashFindingCode.PcOverlayCrash));
        ChecksContext.Check("低内存事件 → 内存不足结论",
            findings.Any(f => f.Code == CrashFindingCode.PcOutOfMemoryCrash));
        ChecksContext.Check("TDR 事件 → 显卡驱动结论",
            findings.Count(f => f.Code == CrashFindingCode.PcGraphicsDriverCrash) == 1);
        ChecksContext.Check("WHEA → 硬件错误结论",
            findings.Any(f => f.Code == CrashFindingCode.PcHardwareErrorCrash));
        ChecksContext.Check("磁盘事件 → 磁盘读取失败结论",
            findings.Any(f => f.Code == CrashFindingCode.PcDiskReadCrash));
        ChecksContext.Check("强证据参与原因抑制",
            PcForensics.HasStrongCause(CrashFindingCode.PcGraphicsDriverCrash)
            && PcForensics.HasStrongCause(CrashFindingCode.PcOutOfMemoryCrash)
            && !PcForensics.HasStrongCause(CrashFindingCode.PcLowDiskSpace));
        ChecksContext.Check("空事件列表 → 空结论", PcForensics.FromEvents(Array.Empty<WindowsEventEvidence>()).Count == 0);
    }

    private static void CheckSystemState()
    {
        // meta.yml 无单位即 MB：4096 MB ≈ 4 GB，2048 MB ≈ 2 GB。
        var meta = new Dictionary<string, string>
        {
            ["SystemMemory"] = "4096",
            ["GPUDedicatedMemory"] = "2048",
        };
        var system = new PcSystemSnapshot(
            DataDriveFreeBytes: 2L * 1024 * 1024 * 1024,
            GameDriveFreeBytes: null,
            PageFileEnabled: false,
            GraphicsDriverDate: DateTimeOffset.UtcNow.AddYears(-2));

        var findings = PcForensics.FromSystemState(meta, system, modelCount: 4000)
            .Select(f => f.Code).ToHashSet();

        ChecksContext.Check("meta 4GB 内存 → 低内存配置", findings.Contains(CrashFindingCode.PcLowMemoryConfig));
        ChecksContext.Check("meta 2GB 显存 + 重 Mod → 显存不足", findings.Contains(CrashFindingCode.PcLowVideoMemory));
        ChecksContext.Check("页面文件关闭 → 强证据结论", findings.Contains(CrashFindingCode.PcPageFileDisabled));
        ChecksContext.Check("磁盘 2GB → 空间不足", findings.Contains(CrashFindingCode.PcLowDiskSpace));
        ChecksContext.Check("驱动两年 → 驱动过旧", findings.Contains(CrashFindingCode.PcOldGraphicsDriver));

        var unknown = PcForensics.FromSystemState(null, PcSystemSnapshot.Unknown, 0);
        ChecksContext.Check("全部未知 → 无配置结论", unknown.Count == 0);

        // 真机回归：Victoria 3 在 64 GB 机器上写 SystemMemory: 65328（MB），
        // 旧实现按字节解析会判成 64 KB，进而误报「内存不足」。
        var bigMachine = new Dictionary<string, string>
        {
            ["SystemMemory"] = "65328",
            ["GPUDedicatedMemory"] = "0",
        };
        var bigFindings = PcForensics.FromSystemState(bigMachine, system, modelCount: 4000)
            .Select(f => f.Code).ToHashSet();
        ChecksContext.Check(
            "meta 65328（无单位=MB，约 64GB）→ 不报低内存",
            !bigFindings.Contains(CrashFindingCode.PcLowMemoryConfig));
        ChecksContext.Check(
            "meta GPUDedicatedMemory: 0 → 视为未提供，不报显存不足",
            !bigFindings.Contains(CrashFindingCode.PcLowVideoMemory));
        ChecksContext.Check(
            "带单位：8 GB 解析为 8 GiB",
            PcForensics.ParseMemoryBytes("8 GB") == 8L * 1024 * 1024 * 1024);
        ChecksContext.Check("0 视为未提供", PcForensics.ParseMemoryBytes("0") is null);
    }

    private static void CheckDbConflicts()
    {
        const string text = """
            [12:00:00][database]: Overriding entry 'k_accept_something' for database 'common/character_interactions' in 'file: common/character_interactions/foo.txt line 12'
            [12:00:01][database]: Overriding entry 'k_accept_something' for database 'common/character_interactions' in 'file: common/character_interactions/foo.txt line 12'
            [12:00:02][database]: Overriding entry 'k_other' for database 'events' in 'file: events/bar.txt line 3'
            noise line
            """;
        var records = CrashLogParser.ParseDbConflicts(text);
        ChecksContext.Equal("覆盖记录去重后 2 条", 2, records.Count);
        ChecksContext.Equal("键名解析", "k_accept_something", records[0].Key);
        ChecksContext.Equal("数据库归一为小写", "common/character_interactions", records[0].Database);
        ChecksContext.Check("行号解析", records[0].Line == 12);
        ChecksContext.Equal("空文本 → 空", 0, CrashLogParser.ParseDbConflicts(null).Count);
    }

    private static void CheckReplacePathParsing()
    {
        const string descriptor = """
            name="X"
            replace_path="common/landed_titles"
            replace_path = "history/titles"
            replace_path="common/landed_titles"
            """;
        var paths = DescriptorDependencyParser.ParseReplacePaths(descriptor);
        ChecksContext.Equal("replace_path 提取并去重", "common/landed_titles,history/titles", string.Join(",", paths));
        ChecksContext.Equal("空文本 → 空", 0, DescriptorDependencyParser.ParseReplacePaths(null).Count);
    }

    private static void CheckReplacePathFootprint(IFileSystem fs)
    {
        var root = Path.Combine(Path.GetTempPath(), "pdxnext-rp-" + Guid.NewGuid().ToString("N"));
        fs.EnsureDirectory(root);
        try
        {
            fs.EnsureDirectory(Path.Combine(root, "common", "landed_titles"));
            fs.WriteAllTextAtomic(Path.Combine(root, "common", "landed_titles", "custom.txt"), "k = 1");
            fs.WriteAllTextAtomic(Path.Combine(root, "common", "other.txt"), "k = 2");

            var vanilla = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "common/other.txt" };
            var withReplace = ModFootprintScanner.Scan(fs, Guid.NewGuid(), "test", root, null, vanilla,
                new[] { "common/landed_titles" });
            var withoutReplace = ModFootprintScanner.Scan(fs, Guid.NewGuid(), "test", root, null, vanilla,
                Array.Empty<string>());

            ChecksContext.Check("replace_path 下文件计为覆盖",
                withReplace.OverriddenPaths.Contains("common/landed_titles/custom.txt"));
            ChecksContext.Check("replace_path 下文件不再算新增",
                !withReplace.AddedPaths.Contains("common/landed_titles/custom.txt"));
            ChecksContext.Check("无 replace_path 时同文件算新增",
                withoutReplace.AddedPaths.Contains("common/landed_titles/custom.txt"));
            ChecksContext.Check("replaceFiles 计数包含整目录替换",
                withReplace.ReplaceFiles == 1 && withoutReplace.ReplaceFiles == 0);
        }
        finally
        {
            if (fs.DirectoryExists(root))
            {
                fs.Delete(root);
            }
        }
    }
}
