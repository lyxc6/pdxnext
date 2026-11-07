namespace PdxLauncher.Core.CrashAnalysis;

/// <summary>一条诊断结论的严重度。排序时 Critical 在最前，Info 在最后。</summary>
public enum CrashSeverity
{
    /// <summary>很可能直接导致启动失败或崩溃。</summary>
    Critical,

    /// <summary>明显异常，但不一定单独致命。</summary>
    Warning,

    /// <summary>只作提示，通常不会直接导致崩溃。</summary>
    Info,
}

/// <summary>崩溃分析的结论类型。界面按这个枚举选对应的本地化标题/说明/建议。</summary>
public enum CrashFindingCode
{
    ModScriptErrorsNearCrash,
    CreateBuildingAutoPlaced,
    UnsetScriptVariables,
    OrphanedEvents,
    IncompatibleModifierType,
    DuplicateDefinitions,
    MissingModPath,
    NonAsciiModPath,
    ModVersionMismatch,
    LauncherLaunchFailed,
    ExecutableMissing,
    NoGameData,
    NoGameLogs,
    NoCrashBundle,
    StartupCrash,
    RecurringCrash,
    CrashCauseUnknown,
    CorruptGameSettings,
    MapLocatorIncomplete,
}

/// <summary>
/// 一个游戏崩溃包（<c>Documents\Paradox Interactive\&lt;Game&gt;\crashes\&lt;exe&gt;_&lt;MMDDHHmmss&gt;</c>）。
/// <paramref name="Meta"/> 保留 <c>meta.yml</c> 的原始键值，界面只读取需要的字段。
/// </summary>
public sealed record CrashBundleInfo(
    string Directory,
    DateTimeOffset? Timestamp,
    string? ExceptionCode,
    string? ExceptionName,
    string? ExceptionAddress,
    string? ExceptionExcerpt,
    string? AppName,
    string? GameVersion,
    IReadOnlyDictionary<string, string> Meta,
    long MinidumpBytes,
    bool HasLogs)
{
    /// <summary>是否有可展示的异常码。</summary>
    public bool HasException => !string.IsNullOrWhiteSpace(ExceptionCode);

    /// <summary>异常码 + 异常名，例如 <c>C0000005 (EXCEPTION_ACCESS_VIOLATION)</c>。</summary>
    public string ExceptionDisplay => (ExceptionCode, ExceptionName) switch
    {
        ({ Length: > 0 }, { Length: > 0 }) => ExceptionCode + " (" + ExceptionName + ")",
        ({ Length: > 0 }, _) => ExceptionCode!,
        _ => string.Empty,
    };
}

/// <summary>
/// 一条诊断结论。数字、路径、Mod 名称与原始日志行都保留为非本地化数据，
/// 界面按 <see cref="Code"/> 取当前语言的标题/说明/建议。
/// </summary>
public sealed record CrashFinding(
    CrashFindingCode Code,
    CrashSeverity Severity,
    int Count = 1,
    string? ModName = null,
    string? SourcePath = null,
    string? Detail = null,
    string? Path = null,
    IReadOnlyList<string>? Evidence = null);

/// <summary>一个加载文件/安装目录里登记的 Mod 及其在日志里的命中情况。</summary>
public sealed record CrashModInfo(
    string Path,
    string? Name,
    string? Version,
    string? SupportedGameVersion,
    bool Exists,
    bool IsArchive,
    bool EnabledKnown,
    bool NonAsciiPath,
    IReadOnlyList<string> SourceHits)
{
    /// <summary>展示名：.metadata 里的 name，缺省时退回目录名。</summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(Name)
        ? Name!
        : System.IO.Path.GetFileName(Path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
}

/// <summary>崩溃包或实时日志目录里的一份日志文件。</summary>
public sealed record CrashLogFileInfo(
    string Name,
    string Path,
    long SizeBytes,
    int LineCount,
    int ErrorCount,
    bool FromCrashBundle);

/// <summary>错误聚类里的一行。</summary>
public sealed record CrashLogGroup(
    string Signature,
    int Count,
    string? SourcePath,
    string? ModName,
    DateTimeOffset? LastTime,
    string Sample);

/// <summary>本启动器最近一次启动当前游戏的记录。</summary>
public sealed record LauncherAttemptInfo(
    bool Found,
    bool Succeeded,
    DateTimeOffset? Time,
    string? RawLine)
{
    /// <summary>没有记录。</summary>
    public static LauncherAttemptInfo None { get; } = new(false, false, null, null);
}

/// <summary>启动链路：游戏进程启动、崩溃、持续时长与本启动器的启动记录。</summary>
public sealed record CrashLaunchChain(
    DateTimeOffset? GameStartTime,
    DateTimeOffset? CrashTime,
    TimeSpan? Duration,
    LauncherAttemptInfo LauncherAttempt,
    string? CrashDirectory,
    string? LogDirectory,
    bool UsedCrashLogs);

/// <summary>一次崩溃分析的全部只读结果。</summary>
public sealed record CrashAnalysisReport(
    string GameId,
    string GameName,
    string GameVersion,
    string GameDataDir,
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<CrashBundleInfo> Crashes,
    IReadOnlyList<CrashFinding> Findings,
    IReadOnlyList<CrashModInfo> Mods,
    IReadOnlyList<CrashLogFileInfo> LogFiles,
    IReadOnlyList<CrashLogGroup> Groups,
    CrashLaunchChain LaunchChain,
    bool ModListAvailable,
    string? LoadFilePath)
{
    /// <summary>最近一个崩溃包；没有时为 null。</summary>
    public CrashBundleInfo? LatestCrash => Crashes.Count > 0 ? Crashes[0] : null;

    /// <summary>是否找到崩溃转储。</summary>
    public bool HasCrash => LatestCrash is not null;

    /// <summary>最高严重度；没有结论时为 null。</summary>
    public CrashSeverity? HighestSeverity => Findings.Count == 0
        ? null
        : Findings.Min(static finding => finding.Severity);
}
