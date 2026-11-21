namespace PdxLauncher.Core.CrashAnalysis;

using System.Globalization;
using System.Text.RegularExpressions;

using PdxLauncher.Core.Contracts;

/// <summary>
/// PC 侧崩溃取证的纯逻辑：输入崩溃码、事件日志证据、meta.yml 键值与系统快照，
/// 输出保守的 <see cref="CrashFinding"/>。这是纯计算：不碰 IO、不访问网络、不写文件。
/// <para>
/// 规则来自 Mod Doctor 的 system.ps1，但去掉了 CK3 专属假设，并对所有数值做防御性解析：
/// 字段缺失或格式异常时跳过对应结论，绝不凭猜测下结论。
/// </para>
/// </summary>
public static partial class PcForensics
{
    private const long BytesPerGb = 1024L * 1024L * 1024L;
    private const long LowSystemMemoryBytes = 8 * BytesPerGb;
    private const long HeavyModLowMemoryBytes = 15 * BytesPerGb;
    private const long LowVideoMemoryBytes = (long)(3.5 * BytesPerGb);
    private const long LowDiskFreeBytes = 5 * BytesPerGb;
    private static readonly TimeSpan OldDriverThreshold = TimeSpan.FromDays(365);

    // 故障模块名 → (类别, 显示名)。顺序即优先级，命中即停。
    private static readonly (Regex Pattern, string Category, string DisplayName)[] FaultModules =
    [
        (RxNvidia(), "graphics", "NVIDIA graphics driver"),
        (RxAmd(), "graphics", "AMD graphics driver"),
        (RxIntel(), "graphics", "Intel graphics driver"),
        (RxDirectX(), "graphics", "DirectX"),
        (RxSteamOverlay(), "overlay", "Steam overlay"),
        (RxDiscordOverlay(), "overlay", "Discord overlay"),
        (RxRtss(), "overlay", "RivaTuner / MSI Afterburner"),
        (RxObs(), "overlay", "OBS game capture"),
        (RxNvidiaApp(), "overlay", "NVIDIA app overlay"),
        (RxReshade(), "overlay", "ReShade"),
        (RxOverwolf(), "overlay", "Overwolf"),
    ];

    /// <summary>根据崩溃码直接得出的强证据结论。</summary>
    public static CrashFinding? FromExceptionCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        string normalized = code.Trim().ToUpperInvariant();
        if (normalized.StartsWith("0X", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized switch
        {
            "C0000017" => Strong(PcCrashKind.Memory, CrashFindingCode.PcOutOfMemoryCrash, code),
            "C0000006" => Strong(PcCrashKind.Disk, CrashFindingCode.PcDiskReadCrash, code),
            "C000001D" => Strong(PcCrashKind.Hardware, CrashFindingCode.PcIllegalInstructionCrash, code),
            _ => null,
        };
    }

    /// <summary>
    /// 把事件日志证据汇总成结论。显卡驱动/覆盖层/硬件/低内存属于强证据，直接归类为对应 PC 故障。
    /// </summary>
    public static IReadOnlyList<CrashFinding> FromEvents(IReadOnlyList<WindowsEventEvidence> events)
    {
        var result = new List<CrashFinding>();
        if (events is null || events.Count == 0)
        {
            return result;
        }

        foreach (var evidence in events)
        {
            switch (evidence.Kind)
            {
                case WindowsEventKind.ApplicationFaultModule:
                        var module = ClassifyFaultModule(evidence.FaultingModule);
                        if (module is { } classified)
                        {
                            var code = classified.Category == "graphics"
                                ? CrashFindingCode.PcGraphicsDriverCrash
                                : CrashFindingCode.PcOverlayCrash;
                            result.Add(Strong(
                                classified.Category == "graphics" ? PcCrashKind.Graphics : PcCrashKind.Overlay,
                                code,
                                classified.DisplayName,
                                evidence.Message));
                        }
                    break;

                case WindowsEventKind.LowVirtualMemory:
                    result.Add(Strong(PcCrashKind.Memory, CrashFindingCode.PcOutOfMemoryCrash, "Windows low-memory warning", evidence.Message));
                    break;

                case WindowsEventKind.GraphicsDriverReset:
                    result.Add(Strong(PcCrashKind.Graphics, CrashFindingCode.PcGraphicsDriverCrash, "graphics driver reset (TDR)", evidence.Message));
                    break;

                case WindowsEventKind.HardwareError:
                    result.Add(Strong(PcCrashKind.Hardware, CrashFindingCode.PcHardwareErrorCrash, "WHEA hardware error", evidence.Message));
                    break;

                case WindowsEventKind.DiskError:
                    result.Add(Strong(PcCrashKind.Disk, CrashFindingCode.PcDiskReadCrash, "Windows disk error", evidence.Message));
                    break;
            }
        }

        return MergeStrong(result);
    }

    /// <summary>
    /// 从 meta.yml 与系统快照得出配置类提示。这些不是直接崩溃原因（除「页面文件关闭」是强证据），
    /// 所以严重度多为 Warning/Info。
    /// </summary>
    public static IReadOnlyList<CrashFinding> FromSystemState(
        IReadOnlyDictionary<string, string>? meta,
        PcSystemSnapshot system,
        int modelCount)
    {
        var result = new List<CrashFinding>();
        long? systemMemory = ParseByteLike(meta, "SystemMemory");
        long? videoMemory = ParseByteLike(meta, "GPUDedicatedMemory");

        if (systemMemory is { } ram && ram < LowSystemMemoryBytes)
        {
            result.Add(new CrashFinding(
                CrashFindingCode.PcLowMemoryConfig,
                CrashSeverity.Warning,
                Detail: ((int)(ram / BytesPerGb)).ToString(CultureInfo.InvariantCulture)));
        }
        else if (systemMemory is { } ram2 && ram2 < HeavyModLowMemoryBytes && modelCount > 3000)
        {
            result.Add(new CrashFinding(
                CrashFindingCode.PcLowMemoryConfig,
                CrashSeverity.Warning,
                Count: modelCount,
                Detail: ((int)(ram2 / BytesPerGb)).ToString(CultureInfo.InvariantCulture)));
        }

        if (videoMemory is { } vram && vram < LowVideoMemoryBytes && modelCount > 3000)
        {
            result.Add(new CrashFinding(
                CrashFindingCode.PcLowVideoMemory,
                CrashSeverity.Warning,
                Detail: (vram / (double)BytesPerGb).ToString("0.0", CultureInfo.InvariantCulture)));
        }

        if (system.PageFileEnabled == false)
        {
            result.Add(new CrashFinding(
                CrashFindingCode.PcPageFileDisabled,
                CrashSeverity.Critical));
        }

        long? lowDisk = FirstLowDisk(system.DataDriveFreeBytes, system.GameDriveFreeBytes);
        if (lowDisk is { } free)
        {
            result.Add(new CrashFinding(
                CrashFindingCode.PcLowDiskSpace,
                CrashSeverity.Warning,
                Detail: (free / (double)BytesPerGb).ToString("0.0", CultureInfo.InvariantCulture)));
        }

        if (system.GraphicsDriverDate is { } driverDate
            && DateTimeOffset.UtcNow - driverDate > OldDriverThreshold)
        {
            result.Add(new CrashFinding(
                CrashFindingCode.PcOldGraphicsDriver,
                CrashSeverity.Warning,
                Detail: driverDate.ToString("yyyy-MM", CultureInfo.InvariantCulture)));
        }

        return result;
    }

    /// <summary>某个结论是否属于「找到了 PC 侧直接原因」，用于抑制「原因未知」结论。</summary>
    public static bool HasStrongCause(CrashFindingCode code) => code is
        CrashFindingCode.PcOutOfMemoryCrash
        or CrashFindingCode.PcDiskReadCrash
        or CrashFindingCode.PcIllegalInstructionCrash
        or CrashFindingCode.PcGraphicsDriverCrash
        or CrashFindingCode.PcOverlayCrash
        or CrashFindingCode.PcHardwareErrorCrash;

    /// <summary>PC 侧故障的归类，供排序/抑制使用。</summary>
    public enum PcCrashKind
    {
        Memory,
        Disk,
        Hardware,
        Graphics,
        Overlay,
    }

    private static CrashFinding Strong(PcCrashKind kind, CrashFindingCode code, string detail, string? evidence = null) =>
        new(
            code,
            CrashSeverity.Critical,
            Detail: detail,
            Evidence: evidence is { Length: > 0 } text ? [text] : null,
            SourcePath: kind.ToString());

    private static IReadOnlyList<CrashFinding> MergeStrong(List<CrashFinding> findings)
    {
        var seen = new HashSet<CrashFindingCode>();
        var result = new List<CrashFinding>();
        foreach (var finding in findings)
        {
            if (seen.Add(finding.Code))
            {
                result.Add(finding);
            }
        }

        return result;
    }

    private static (string Category, string DisplayName)? ClassifyFaultModule(string? module)
    {
        if (string.IsNullOrWhiteSpace(module))
        {
            return null;
        }

        foreach (var (pattern, category, displayName) in FaultModules)
        {
            if (pattern.IsMatch(module))
            {
                return (category, displayName);
            }
        }

        return null;
    }

    private static long? ParseByteLike(IReadOnlyDictionary<string, string>? meta, string key)
    {
        if (meta is null || !meta.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // meta.yml 里的数字常见写法：8589934592 / "8589934592" / "8 GB" / "8192 MB"。
        var match = Regex.Match(raw, @"(?<number>\d+(?:\.\d+)?)\s*(?<unit>[KMGT]?B)?", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups["number"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        string unit = match.Groups["unit"].Value.ToUpperInvariant();
        double multiplier = unit switch
        {
            "KB" => 1024d,
            "MB" => 1024d * 1024d,
            "GB" => 1024d * 1024d * 1024d,
            "TB" => 1024d * 1024d * 1024d * 1024d,
            _ => 1d, // 无单位按字节
        };

        double bytes = value * multiplier;
        return bytes is >= 0 and <= long.MaxValue ? (long)bytes : null;
    }

    private static long? FirstLowDisk(params long?[] freeBytes)
    {
        foreach (var free in freeBytes)
        {
            if (free is { } value && value < LowDiskFreeBytes)
            {
                return value;
            }
        }

        return null;
    }

    [GeneratedRegex(@"nvwgf2um|nvd3dum|nvoglv|nvlddmkm|nvapi|nvgpucomp", RegexOptions.IgnoreCase)]
    private static partial Regex RxNvidia();

    [GeneratedRegex(@"atiumd|atidxx|amdxx|amdihk|atio6axx|amdxc", RegexOptions.IgnoreCase)]
    private static partial Regex RxAmd();

    [GeneratedRegex(@"igd10|igd11|igd12|igc64|igdumd|igdgmm", RegexOptions.IgnoreCase)]
    private static partial Regex RxIntel();

    [GeneratedRegex(@"^(d3d11|dxgi|d3d12)\.dll|d3d12core", RegexOptions.IgnoreCase)]
    private static partial Regex RxDirectX();

    [GeneratedRegex(@"gameoverlayrenderer", RegexOptions.IgnoreCase)]
    private static partial Regex RxSteamOverlay();

    [GeneratedRegex(@"discordhook|discord_overlay", RegexOptions.IgnoreCase)]
    private static partial Regex RxDiscordOverlay();

    [GeneratedRegex(@"rtsshooks|rtsscore", RegexOptions.IgnoreCase)]
    private static partial Regex RxRtss();

    [GeneratedRegex(@"graphics-hook", RegexOptions.IgnoreCase)]
    private static partial Regex RxObs();

    [GeneratedRegex(@"nvspcap|nvcontainer", RegexOptions.IgnoreCase)]
    private static partial Regex RxNvidiaApp();

    [GeneratedRegex(@"reshade", RegexOptions.IgnoreCase)]
    private static partial Regex RxReshade();

    [GeneratedRegex(@"overwolf|ow-graphics", RegexOptions.IgnoreCase)]
    private static partial Regex RxOverwolf();
}
