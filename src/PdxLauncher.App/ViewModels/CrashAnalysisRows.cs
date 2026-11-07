namespace PdxLauncher.App.ViewModels;

using System.Globalization;
using System.Text;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.CrashAnalysis;

/// <summary>概览里的一张摘要卡：标题、值、补充说明与自动化 id。</summary>
public sealed class CrashSummaryRowViewModel
{
    public CrashSummaryRowViewModel(string title, string value, string detail, string automationId)
    {
        Title = title;
        Value = value;
        Detail = detail;
        AutomationId = automationId;
    }

    public string Title { get; }

    public string Value { get; }

    public string Detail { get; }

    public string AutomationId { get; }
}

/// <summary>一条诊断结论：严重度、标题、说明、建议与可展开的原始证据。</summary>
public sealed class CrashFindingRowViewModel
{
    public CrashFindingRowViewModel(
        string title,
        string detail,
        string fixText,
        string badgeText,
        CrashSeverity severity,
        string evidenceHeader,
        string evidenceText,
        bool expandByDefault,
        string automationId)
    {
        Title = title;
        Detail = detail;
        FixText = fixText;
        BadgeText = badgeText;
        Severity = severity;
        EvidenceHeader = evidenceHeader;
        EvidenceText = evidenceText;
        ExpandByDefault = expandByDefault;
        AutomationId = automationId;
    }

    public string Title { get; }

    public string Detail { get; }

    public string FixText { get; }

    public string BadgeText { get; }

    public CrashSeverity Severity { get; }

    public string EvidenceHeader { get; }

    public string EvidenceText { get; }

    public bool HasEvidence => !string.IsNullOrWhiteSpace(EvidenceText);

    public bool ExpandByDefault { get; }

    public bool IsCritical => Severity == CrashSeverity.Critical;

    public bool IsWarning => Severity == CrashSeverity.Warning;

    public bool IsInfo => Severity == CrashSeverity.Info;

    public string AutomationId { get; }
}

/// <summary>崩溃记录里的一行。</summary>
public sealed class CrashBundleRowViewModel
{
    public CrashBundleRowViewModel(string title, string detail, string path, string openText, string automationId)
    {
        Title = title;
        Detail = detail;
        Path = path;
        OpenText = openText;
        AutomationId = automationId;
    }

    public string Title { get; }

    public string Detail { get; }

    public string Path { get; }

    public string OpenText { get; }

    public string AutomationId { get; }
}

/// <summary>Mod 清单里的一行。</summary>
public sealed class CrashModRowViewModel
{
    public CrashModRowViewModel(string name, string detail, string path, string automationId)
    {
        Name = name;
        Detail = detail;
        Path = path;
        AutomationId = automationId;
    }

    public string Name { get; }

    public string Detail { get; }

    public string Path { get; }

    public string AutomationId { get; }
}

/// <summary>日志文件下拉里的一项。</summary>
public sealed class CrashLogFileRowViewModel
{
    public CrashLogFileRowViewModel(
        string name,
        string displayText,
        string path,
        long sizeBytes,
        int errorCount,
        string automationId)
    {
        Name = name;
        DisplayText = displayText;
        Path = path;
        SizeBytes = sizeBytes;
        ErrorCount = errorCount;
        AutomationId = automationId;
    }

    public string Name { get; }

    public string DisplayText { get; }

    public string Path { get; }

    public long SizeBytes { get; }

    public int ErrorCount { get; }

    public string AutomationId { get; }
}

/// <summary>错误聚类里的一行。</summary>
public sealed class CrashLogGroupRowViewModel
{
    public CrashLogGroupRowViewModel(string title, string detail, string sample, string automationId)
    {
        Title = title;
        Detail = detail;
        Sample = sample;
        AutomationId = automationId;
    }

    public string Title { get; }

    public string Detail { get; }

    public string Sample { get; }

    public string AutomationId { get; }
}

/// <summary>启动链路里的一行。</summary>
public sealed class CrashLaunchRowViewModel
{
    public CrashLaunchRowViewModel(string title, string value, string detail, string automationId)
    {
        Title = title;
        Value = value;
        Detail = detail;
        AutomationId = automationId;
    }

    public string Title { get; }

    public string Value { get; }

    public string Detail { get; }

    public string AutomationId { get; }
}

/// <summary>
/// 崩溃分析的行构建与文案映射。所有界面文字在这里按当前语言取；
/// 原始日志行、异常码、路径与 Mod 名称保持原样。
/// </summary>
public static class CrashAnalysisText
{
    /// <summary>概览摘要行。</summary>
    public static IReadOnlyList<CrashSummaryRowViewModel> CreateSummaryRows(
        CrashAnalysisReport report,
        Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(localizer);

        var latest = report.LatestCrash;
        var exceptionValue = latest?.ExceptionDisplay is { Length: > 0 } display
            ? display
            : localizer.CrashSummaryExceptionUnknown;
        var exceptionDetail = latest?.ExceptionCode is { Length: > 0 } code
            ? DescribeException(code, localizer)
            : localizer.CrashSummaryExceptionUnknown;
        if (latest?.ExceptionAddress is { Length: > 0 } address)
        {
            exceptionDetail += " · " + localizer.T("crash.summary.exceptionDetail", address);
        }

        var suspect = FindSuspect(report);
        var modValue = report.ModListAvailable || report.Mods.Count > 0
            ? localizer.T("crash.summary.modsValue", report.Mods.Count(mod => mod.Exists))
            : localizer.CrashSummaryModsUnknown;

        var meta = latest?.Meta;
        var gpu = GetMeta(meta, "GPUName") ?? localizer.CrashTimeUnknown;
        var memory = GetMeta(meta, "SystemMemory") ?? "?";
        var os = GetMeta(meta, "OperatingSystem") ?? localizer.CrashTimeUnknown;

        return
        [
            new(
                localizer.CrashSummaryGame,
                string.IsNullOrWhiteSpace(report.GameVersion) ? report.GameName : report.GameVersion,
                localizer.T("crash.summary.gameDetail", report.GameName, report.GameId),
                "crashSummaryGame"),
            new(
                localizer.CrashSummaryCrashTime,
                FormatTime(latest?.Timestamp, localizer),
                latest is null ? localizer.CrashSummaryNoCrash : exceptionValue,
                "crashSummaryCrashTime"),
            new(
                localizer.CrashSummaryException,
                latest is null ? localizer.CrashTimeUnknown : exceptionValue,
                exceptionDetail,
                "crashSummaryException"),
            new(
                localizer.CrashSummaryDuration,
                FormatDuration(report.LaunchChain.Duration, localizer),
                string.Empty,
                "crashSummaryDuration"),
            new(
                localizer.CrashSummaryMods,
                modValue,
                report.ModListAvailable ? string.Empty : localizer.CrashOverviewModsUnknown,
                "crashSummaryMods"),
            new(
                localizer.CrashSummarySuspect,
                suspect?.ModName ?? localizer.CrashSummarySuspectNone,
                suspect?.SourcePath ?? string.Empty,
                "crashSummarySuspect"),
            new(
                localizer.CrashSummarySystem,
                gpu,
                localizer.T("crash.summary.systemDetail", gpu, memory, os),
                "crashSummarySystem"),
        ];
    }

    /// <summary>诊断结论行。</summary>
    public static IReadOnlyList<CrashFindingRowViewModel> CreateFindingRows(
        CrashAnalysisReport report,
        Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(localizer);

        var rows = new List<CrashFindingRowViewModel>(report.Findings.Count);
        for (int index = 0; index < report.Findings.Count; index++)
        {
            var finding = report.Findings[index];
            string badge = finding.Severity switch
            {
                CrashSeverity.Critical => localizer.CrashFindingSeverityCritical,
                CrashSeverity.Warning => localizer.CrashFindingSeverityWarning,
                _ => localizer.CrashFindingSeverityInfo,
            };

            string keyBase = FindingKeyBase(finding.Code);
            string title = localizer.T(keyBase + ".title");
            string detail = localizer.T(DetailKey(finding), DetailArgs(finding, report));
            string fix = localizer.T("crash.finding.fixPrefix", localizer.T(keyBase + ".fix"));
            var evidence = finding.Evidence ?? Array.Empty<string>();
            rows.Add(new CrashFindingRowViewModel(
                title,
                detail,
                fix,
                badge,
                finding.Severity,
                localizer.T("crash.finding.evidenceHeader", evidence.Count),
                string.Join("\n", evidence),
                finding.Severity == CrashSeverity.Critical,
                "crashFinding" + index.ToString(CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    /// <summary>崩溃记录行。</summary>
    public static IReadOnlyList<CrashBundleRowViewModel> CreateBundleRows(
        CrashAnalysisReport report,
        Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(localizer);

        var rows = new List<CrashBundleRowViewModel>(report.Crashes.Count);
        for (int index = 0; index < report.Crashes.Count; index++)
        {
            var bundle = report.Crashes[index];
            string title = bundle.ExceptionDisplay is { Length: > 0 } display
                ? display
                : localizer.CrashCrashesExceptionUnknown;
            string minidump = bundle.MinidumpBytes > 0
                ? localizer.T("crash.crashes.minidump", SizeFormatter.Format(bundle.MinidumpBytes))
                : localizer.CrashCrashesNoMinidump;
            string version = bundle.GameVersion ?? report.GameVersion;
            string detail = bundle.Timestamp is { } time
                ? localizer.T("crash.crashes.rowDetail", FormatTime(time, localizer), version, minidump)
                : localizer.T("crash.crashes.rowDetailNoTime", version, minidump);
            rows.Add(new CrashBundleRowViewModel(
                title,
                detail + "\n" + bundle.Directory,
                bundle.Directory,
                localizer.CrashActionOpenBundle,
                "crashBundle" + index.ToString(CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    /// <summary>Mod 清单行。</summary>
    public static IReadOnlyList<CrashModRowViewModel> CreateModRows(
        CrashAnalysisReport report,
        Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(localizer);

        var rows = new List<CrashModRowViewModel>(report.Mods.Count);
        for (int index = 0; index < report.Mods.Count; index++)
        {
            var mod = report.Mods[index];
            string status = mod switch
            {
                { Exists: false } => localizer.CrashModsStatusMissing,
                { IsArchive: true } => localizer.CrashModsStatusArchive,
                { EnabledKnown: false } => localizer.CrashModsStatusUnknown,
                _ => localizer.CrashModsStatusEnabled,
            };
            string name = string.IsNullOrWhiteSpace(mod.DisplayName)
                ? localizer.CrashModsNameFallback
                : mod.DisplayName;
            rows.Add(new CrashModRowViewModel(
                name,
                localizer.T("crash.mods.rowDetail", mod.Version ?? "-", mod.SupportedGameVersion ?? "-", status),
                mod.Path,
                "crashMod" + index.ToString(CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    /// <summary>日志文件行。</summary>
    public static IReadOnlyList<CrashLogFileRowViewModel> CreateLogFileRows(
        CrashAnalysisReport report,
        Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(localizer);

        var rows = new List<CrashLogFileRowViewModel>(report.LogFiles.Count);
        for (int index = 0; index < report.LogFiles.Count; index++)
        {
            var file = report.LogFiles[index];
            rows.Add(new CrashLogFileRowViewModel(
                file.Name,
                localizer.T("crash.logs.fileItem", file.Name, SizeFormatter.Format(file.SizeBytes), file.ErrorCount),
                file.Path,
                file.SizeBytes,
                file.ErrorCount,
                "crashLogFile" + index.ToString(CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    /// <summary>错误聚类行（最多 8 条）。</summary>
    public static IReadOnlyList<CrashLogGroupRowViewModel> CreateGroupRows(
        CrashAnalysisReport report,
        Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(localizer);

        return report.Groups
            .Take(8)
            .Select((group, index) => new CrashLogGroupRowViewModel(
                group.Signature,
                localizer.T(
                    "crash.logs.groupDetail",
                    group.Count,
                    group.ModName ?? group.SourcePath ?? localizer.CrashLogsGroupNoSource,
                    FormatTime(group.LastTime, localizer)),
                group.Sample,
                "crashGroup" + index.ToString(CultureInfo.InvariantCulture)))
            .ToArray();
    }

    /// <summary>启动链路行。</summary>
    public static IReadOnlyList<CrashLaunchRowViewModel> CreateLaunchRows(
        CrashAnalysisReport report,
        Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(localizer);

        var chain = report.LaunchChain;
        var latest = report.LatestCrash;
        if (latest is null && !chain.LauncherAttempt.Found)
        {
            return Array.Empty<CrashLaunchRowViewModel>();
        }

        var rows = new List<CrashLaunchRowViewModel>();

        rows.Add(new(
            localizer.CrashLaunchGameStart,
            FormatTime(chain.GameStartTime, localizer),
            localizer.CrashLaunchDetailSystem,
            "crashLaunchStart"));
        rows.Add(new(
            localizer.CrashLaunchCrash,
            FormatTime(chain.CrashTime, localizer),
            latest is null
                ? string.Empty
                : localizer.T("crash.launch.detailCrash", latest.ExceptionDisplay),
            "crashLaunchCrash"));
        rows.Add(new(
            localizer.CrashLaunchDuration,
            FormatDuration(chain.Duration, localizer),
            string.Empty,
            "crashLaunchDuration"));

        var attempt = chain.LauncherAttempt;
        string launcherValue = attempt switch
        {
            { Found: false } => localizer.CrashLaunchValueNotRecorded,
            { Succeeded: true } => localizer.CrashLaunchValueSuccess,
            _ => localizer.CrashLaunchValueFailed,
        };
        rows.Add(new(
            localizer.CrashLaunchLauncher,
            launcherValue,
            attempt.RawLine ?? localizer.CrashLaunchDetailLauncher,
            "crashLaunchLauncher"));

        rows.Add(new(
            localizer.CrashLaunchCrashDir,
            chain.CrashDirectory ?? localizer.CrashLaunchValueUnknown,
            latest is null
                ? string.Empty
                : localizer.T(
                    "crash.launch.detailCrashDir",
                    latest.MinidumpBytes > 0 ? SizeFormatter.Format(latest.MinidumpBytes) : localizer.CrashCrashesNoMinidump,
                    chain.UsedCrashLogs ? localizer.CrashLaunchLogsAvailable : localizer.CrashLaunchLogsMissing),
            "crashLaunchCrashDir"));

        return rows;
    }

    /// <summary>复制到剪贴板的纯文本报告。</summary>
    public static string BuildReportText(CrashAnalysisReport report, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(localizer);

        var builder = new StringBuilder(2048);
        builder.AppendLine(localizer.CrashReportHeader);
        builder.AppendLine(localizer.T(
            "crash.report.game",
            report.GameName,
            report.GameVersion,
            report.GameId));
        builder.AppendLine(localizer.T("crash.report.generated", FormatTime(report.AnalyzedAt, localizer)));
        builder.AppendLine(report.LatestCrash is { } latest
            ? localizer.T("crash.report.crash", FormatTime(latest.Timestamp, localizer) + " · " + latest.ExceptionDisplay)
            : localizer.CrashReportCrashNone);
        builder.AppendLine();

        builder.AppendLine(localizer.CrashReportFindings);
        if (report.Findings.Count == 0)
        {
            builder.AppendLine(localizer.CrashReportNoFindings);
        }
        else
        {
            foreach (var finding in report.Findings)
            {
                string keyBase = FindingKeyBase(finding.Code);
                builder.Append("  [").Append(localizer.T(keyBase + ".title")).Append("] ");
                builder.AppendLine(localizer.T(DetailKey(finding), DetailArgs(finding, report)));
                if (finding.Evidence is { Count: > 0 } evidence)
                {
                    builder.AppendLine(localizer.CrashReportEvidence);
                    foreach (string line in evidence)
                    {
                        builder.Append("    ").AppendLine(line);
                    }
                }
            }
        }

        builder.AppendLine();
        var names = report.Mods
            .Where(static mod => mod.Exists)
            .Select(static mod => mod.DisplayName)
            .ToArray();
        builder.AppendLine(localizer.T(
            "crash.report.mods",
            names.Length == 0 ? localizer.CrashSummaryModsUnknown : string.Join(", ", names)));
        return builder.ToString();
    }

    /// <summary>日志尾部：按块切分、可选只保留错误块，并把最新块放在最前。</summary>
    public static string BuildLogTailText(string? raw, bool onlyErrors)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var blocks = new List<string>();
        var current = new StringBuilder();

        foreach (string line in normalized.Split('\n'))
        {
            if (IsLogHeader(line) && current.Length > 0)
            {
                blocks.Add(current.ToString().TrimEnd('\n'));
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append('\n');
            }

            current.Append(line);
        }

        if (current.Length > 0)
        {
            blocks.Add(current.ToString().TrimEnd('\n'));
        }

        var selected = onlyErrors
            ? blocks.Where(IsErrorBlockText).ToArray()
            : blocks.ToArray();
        selected = selected.Reverse().ToArray();
        return string.Join("\n", selected).Trim();
    }

    /// <summary>把时长格式化成本地化文本。</summary>
    public static string FormatDuration(TimeSpan? duration, Localizer localizer)
    {
        if (duration is null)
        {
            return localizer.CrashTimeUnknown;
        }

        int totalSeconds = Math.Max(0, (int)Math.Round(duration.Value.TotalSeconds));
        if (totalSeconds < 60)
        {
            return localizer.T("crash.duration.seconds", totalSeconds);
        }

        if (totalSeconds < 3600)
        {
            return localizer.T("crash.duration.minutes", totalSeconds / 60, totalSeconds % 60);
        }

        return localizer.T("crash.duration.hours", totalSeconds / 3600, totalSeconds % 3600 / 60);
    }

    /// <summary>把崩溃时间格式化成本地化文本。</summary>
    public static string FormatTime(DateTimeOffset? time, Localizer localizer) =>
        time is null
            ? localizer.CrashTimeUnknown
            : time.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private static CrashFinding? FindSuspect(CrashAnalysisReport report) =>
        report.Findings.FirstOrDefault(static finding =>
            finding.ModName is { Length: > 0 }
            && finding.Severity is CrashSeverity.Critical or CrashSeverity.Warning)
        ?? report.Findings.FirstOrDefault(static finding => finding.ModName is { Length: > 0 });

    private static string DescribeException(string code, Localizer localizer)
    {
        string normalized = code.Trim().ToLowerInvariant();
        if (normalized.StartsWith("0x", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized.Length == 0)
        {
            return localizer.CrashExceptionUnknown;
        }

        string key = "crash.exception." + (char.IsLetter(normalized[0]) ? string.Empty : "code") + normalized;
        string text = localizer.T(key);
        return text.StartsWith('⟨') ? localizer.CrashExceptionUnknown : text;
    }

    private static string? GetMeta(IReadOnlyDictionary<string, string>? meta, string key) =>
        meta is not null && meta.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string FindingKeyBase(CrashFindingCode code)
    {
        string name = code.ToString();
        return "crash.finding." + char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static string DetailKey(CrashFinding finding) =>
        finding.Code == CrashFindingCode.ModScriptErrorsNearCrash
        && !int.TryParse(finding.Detail, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            ? FindingKeyBase(finding.Code) + ".detailNoGap"
            : FindingKeyBase(finding.Code) + ".detail";

    private static object?[] DetailArgs(CrashFinding finding, CrashAnalysisReport report)
    {
        string source = finding.ModName ?? finding.SourcePath ?? "-";
        return finding.Code switch
        {
            CrashFindingCode.ModScriptErrorsNearCrash when int.TryParse(
                finding.Detail,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var seconds) =>
                [seconds, finding.Count, source],
            CrashFindingCode.ModScriptErrorsNearCrash =>
                [finding.Count, source],
            CrashFindingCode.CreateBuildingAutoPlaced => [finding.Count, source],
            CrashFindingCode.UnsetScriptVariables => [finding.Count],
            CrashFindingCode.OrphanedEvents => [finding.Count],
            CrashFindingCode.IncompatibleModifierType => [finding.Count, source],
            CrashFindingCode.DuplicateDefinitions => [finding.Count, finding.SourcePath ?? source],
            CrashFindingCode.MissingModPath => [finding.Path ?? "-"],
            CrashFindingCode.NonAsciiModPath => [finding.Count],
            CrashFindingCode.ModVersionMismatch =>
                [finding.ModName ?? "-", finding.Detail ?? "-", report.GameVersion],
            CrashFindingCode.LauncherLaunchFailed => [finding.Detail ?? "-"],
            CrashFindingCode.ExecutableMissing => [finding.Path ?? "-"],
            CrashFindingCode.NoGameData => [finding.Path ?? "-"],
            CrashFindingCode.NoGameLogs => [finding.Path ?? "-"],
            CrashFindingCode.NoCrashBundle => [finding.Path ?? "-"],
            CrashFindingCode.StartupCrash => [finding.Detail ?? "0"],
            CrashFindingCode.RecurringCrash => [finding.Count, finding.Detail ?? "-"],
            CrashFindingCode.CrashCauseUnknown => [finding.Detail ?? "-"],
            CrashFindingCode.CorruptGameSettings => [finding.Path ?? "-"],
            CrashFindingCode.MapLocatorIncomplete => [finding.Count],
            _ => [finding.Count],
        };
    }

    private static bool IsLogHeader(string line) =>
        line.Length > 1 && line[0] == '[' && char.IsAsciiDigit(line[1]);

    private static bool IsErrorBlockText(string block)
    {
        const string keywords = "error|failed|cannot|missing|orphaned|never set|not in code|incomplete|exception|crash|warning";
        foreach (string keyword in keywords.Split('|'))
        {
            if (block.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
