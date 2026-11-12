namespace PdxLauncher.Core.CrashAnalysis;

using System.Globalization;

using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;

/// <summary>
/// 把日志信号翻译成「可能解释启动失败的结论」。规则只做保守推断：
/// 时间相邻、文件归属、版本标注这些证据能说明「高度相关」，不把相关性写成因果。
/// </summary>
public static class CrashRules
{
    private const int MaxFindings = 12;
    private static readonly TimeSpan StartupWindow = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan NearCrashWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RecurringWindow = TimeSpan.FromHours(24);

    /// <summary>评估全部规则并返回按严重度排序的结论。</summary>
    public static IReadOnlyList<CrashFinding> Evaluate(
        GameInstallation game,
        bool gameDataExists,
        bool gameLogsExist,
        bool executableExists,
        IReadOnlyList<CrashBundleInfo> crashes,
        CrashSignals signals,
        IReadOnlyList<CrashModInfo> mods,
        bool modListAvailable,
        LauncherAttemptInfo launcherAttempt,
        bool settingsCorrupt,
        string settingsPath,
        string logsPath,
        string crashesPath,
        TimeSpan? startupDuration)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(crashes);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(mods);

        var findings = new List<CrashFinding>();
        var latest = crashes.Count > 0 ? crashes[0] : null;
        var crashTime = latest?.Timestamp;

        if (!gameDataExists)
        {
            findings.Add(new CrashFinding(
                CrashFindingCode.NoGameData,
                CrashSeverity.Warning,
                Path: game.GameDataDir,
                Detail: game.GameDataDir));
        }
        else if (!gameLogsExist)
        {
            findings.Add(new CrashFinding(
                CrashFindingCode.NoGameLogs,
                CrashSeverity.Warning,
                Path: logsPath,
                Detail: logsPath));
        }

        if (!executableExists)
        {
            findings.Add(new CrashFinding(
                CrashFindingCode.ExecutableMissing,
                CrashSeverity.Critical,
                Path: game.ExePath,
                Detail: game.ExePath));
        }

        var missingMods = mods.Where(static mod => !mod.Exists).ToArray();
        if (missingMods.Length > 0)
        {
            findings.Add(new CrashFinding(
                CrashFindingCode.MissingModPath,
                CrashSeverity.Critical,
                missingMods.Length,
                Path: missingMods[0].Path,
                Detail: missingMods[0].Path,
                Evidence: missingMods.Select(static mod => mod.Path).Take(6).ToArray()));
        }

        if (GamePathRules.RequiresAsciiPaths(game.GameId))
        {
            var nonAscii = mods.Where(static mod => mod.NonAsciiPath).ToArray();
            if (nonAscii.Length > 0)
            {
                findings.Add(new CrashFinding(
                    CrashFindingCode.NonAsciiModPath,
                    CrashSeverity.Warning,
                    nonAscii.Length,
                    Path: nonAscii[0].Path,
                    Detail: nonAscii[0].Path,
                    Evidence: nonAscii.Select(static mod => mod.Path).Take(6).ToArray()));
            }
        }

        var mismatch = mods
            .Where(mod => !string.IsNullOrWhiteSpace(mod.SupportedGameVersion)
                && !MatchesSupportedVersion(mod.SupportedGameVersion, game.Version))
            .ToArray();
        if (mismatch.Length > 0)
        {
            findings.Add(new CrashFinding(
                CrashFindingCode.ModVersionMismatch,
                CrashSeverity.Warning,
                mismatch.Length,
                ModName: mismatch[0].DisplayName,
                Path: game.Version,
                Detail: mismatch[0].SupportedGameVersion));
        }

        if (launcherAttempt.Found && !launcherAttempt.Succeeded)
        {
            findings.Add(new CrashFinding(
                CrashFindingCode.LauncherLaunchFailed,
                CrashSeverity.Critical,
                Detail: launcherAttempt.RawLine,
                Evidence: launcherAttempt.RawLine is { Length: > 0 } line ? [line] : null));
        }

        if (settingsCorrupt)
        {
            findings.Add(new CrashFinding(
                CrashFindingCode.CorruptGameSettings,
                CrashSeverity.Warning,
                Path: settingsPath,
                Detail: settingsPath));
        }

        if (latest is null && gameLogsExist)
        {
            findings.Add(new CrashFinding(
                CrashFindingCode.NoCrashBundle,
                CrashSeverity.Warning,
                Path: crashesPath,
                Detail: crashesPath));
        }

        if (signals.ScriptErrorBlocks > 0)
        {
            var topSource = TopSource(signals);
            var topMod = FindModForSource(mods, topSource);
            int gapSeconds = crashTime is { } time && signals.LastScriptErrorTime is { } lastError
                ? (int)Math.Max(0, (time - lastError).TotalSeconds)
                : -1;
            bool nearCrash = crashTime is not null
                && signals.LastScriptErrorTime is not null
                && crashTime.Value - signals.LastScriptErrorTime.Value is { } gap
                && gap >= TimeSpan.Zero
                && gap <= NearCrashWindow;

            findings.Add(new CrashFinding(
                CrashFindingCode.ModScriptErrorsNearCrash,
                nearCrash ? CrashSeverity.Critical : CrashSeverity.Warning,
                signals.ScriptErrorBlocks,
                ModName: topMod?.DisplayName,
                SourcePath: topSource,
                Detail: gapSeconds >= 0 ? gapSeconds.ToString(CultureInfo.InvariantCulture) : null,
                Evidence: signals.FindEvidence(
                    block => block.Text.Contains("Script system error", StringComparison.OrdinalIgnoreCase)
                        || block.Text.Contains("PostValidate of effect", StringComparison.OrdinalIgnoreCase),
                    maxLines: 6)));
        }

        if (signals.CreateBuildingAutoPlaced > 0)
        {
            var source = FindSourceForPhrase(signals, "Attempting to manually create auto-placed building");
            var mod = FindModForSource(mods, source);
            findings.Add(new CrashFinding(
                CrashFindingCode.CreateBuildingAutoPlaced,
                latest is not null ? CrashSeverity.Critical : CrashSeverity.Warning,
                signals.CreateBuildingAutoPlaced,
                ModName: mod?.DisplayName,
                SourcePath: source,
                Evidence: signals.FindEvidence(
                    block => block.Text.Contains(
                        "Attempting to manually create auto-placed building",
                        StringComparison.OrdinalIgnoreCase),
                    maxLines: 6)));
        }

        if (signals.UnsetScriptVariables > 0)
        {
            var source = FindSourceForPhrase(signals, "is used but is never set");
            var mod = FindModForSource(mods, source);
            findings.Add(new CrashFinding(
                CrashFindingCode.UnsetScriptVariables,
                latest is not null ? CrashSeverity.Warning : CrashSeverity.Info,
                signals.UnsetScriptVariables,
                ModName: mod?.DisplayName,
                SourcePath: source,
                Evidence: signals.FindEvidence(
                    block => block.Text.Contains("is used but is never set", StringComparison.OrdinalIgnoreCase),
                    maxLines: 4)));
        }

        if (signals.OrphanedEvents > 0)
        {
            var source = FindSourceForPhrase(signals, "is orphaned");
            var mod = FindModForSource(mods, source);
            findings.Add(new CrashFinding(
                CrashFindingCode.OrphanedEvents,
                CrashSeverity.Info,
                signals.OrphanedEvents,
                ModName: mod?.DisplayName,
                SourcePath: source,
                Evidence: signals.FindEvidence(
                    block => block.Text.Contains("is orphaned", StringComparison.OrdinalIgnoreCase),
                    maxLines: 4)));
        }

        if (signals.IncompatibleModifierTypes > 0)
        {
            var source = FindSourceForPhrase(signals, "defined in script but not in code");
            var mod = FindModForSource(mods, source);
            findings.Add(new CrashFinding(
                CrashFindingCode.IncompatibleModifierType,
                latest is not null ? CrashSeverity.Warning : CrashSeverity.Info,
                signals.IncompatibleModifierTypes,
                ModName: mod?.DisplayName,
                SourcePath: source,
                Evidence: signals.FindEvidence(
                    block => block.Text.Contains("defined in script but not in code", StringComparison.OrdinalIgnoreCase),
                    maxLines: 4)));
        }

        if (signals.DuplicateDefinitions > 0)
        {
            findings.Add(new CrashFinding(
                CrashFindingCode.DuplicateDefinitions,
                CrashSeverity.Warning,
                signals.DuplicateDefinitions,
                Evidence: signals.FindEvidence(
                    block => block.Text.Contains("already defined", StringComparison.OrdinalIgnoreCase),
                    maxLines: 4)));
        }

        if (signals.MapLocatorIssues > 0)
        {
            findings.Add(new CrashFinding(
                CrashFindingCode.MapLocatorIncomplete,
                CrashSeverity.Info,
                signals.MapLocatorIssues,
                Evidence: signals.FindEvidence(
                    block => block.Text.Contains("is incomplete", StringComparison.OrdinalIgnoreCase)
                        || block.Text.Contains("Cannot find a port node", StringComparison.OrdinalIgnoreCase),
                    maxLines: 4)));
        }

        if (latest is not null && startupDuration is { } duration && duration >= TimeSpan.Zero && duration <= StartupWindow)
        {
            findings.Add(new CrashFinding(
                CrashFindingCode.StartupCrash,
                CrashSeverity.Warning,
                (int)Math.Max(1, duration.TotalSeconds),
                Detail: ((int)duration.TotalSeconds).ToString(CultureInfo.InvariantCulture)));
        }

        if (latest is not null && crashes.Count >= 2)
        {
            var recent = crashes
                .Where(crash => crash.Timestamp is { } time
                    && latest.Timestamp is { } latestTime
                    && latestTime - time <= RecurringWindow
                    && string.Equals(crash.ExceptionCode, latest.ExceptionCode, StringComparison.OrdinalIgnoreCase))
                .Count();
            if (recent >= 2)
            {
                findings.Add(new CrashFinding(
                    CrashFindingCode.RecurringCrash,
                    CrashSeverity.Warning,
                    recent,
                    Detail: latest.ExceptionDisplay));
            }
        }

        if (latest is not null && !HasCauseFinding(findings))
        {
            findings.Add(new CrashFinding(
                CrashFindingCode.CrashCauseUnknown,
                CrashSeverity.Warning,
                Detail: latest.ExceptionDisplay));
        }

        return findings
            .GroupBy(static finding => finding.Code)
            .Select(static group => group.First())
            .OrderBy(static finding => finding.Severity)
            .ThenByDescending(static finding => finding.Count)
            .Take(MaxFindings)
            .ToArray();
    }

    /// <summary>
    /// 判断 Mod 标注的支持版本是否覆盖当前游戏版本。
    /// 只处理常见的 <c>1.15.*</c> / <c>1.15.0</c> 写法；带逗号、区间等复杂写法一律视为匹配，避免误报。
    /// </summary>
    public static bool MatchesSupportedVersion(string? supported, string? gameVersion)
    {
        if (string.IsNullOrWhiteSpace(supported) || string.IsNullOrWhiteSpace(gameVersion))
        {
            return true;
        }

        var pattern = supported.Trim();
        if (pattern.Contains(',')
            || pattern.Contains('>')
            || pattern.Contains('<')
            || pattern.Contains(' '))
        {
            return true;
        }

        int star = pattern.IndexOf('*');
        if (star >= 0)
        {
            var prefix = pattern[..star].TrimEnd('.');
            return gameVersion.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return gameVersion.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)
            || pattern.StartsWith(gameVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasCauseFinding(IReadOnlyList<CrashFinding> findings) =>
        findings.Any(static finding => finding.Code is
            CrashFindingCode.ModScriptErrorsNearCrash
            or CrashFindingCode.CreateBuildingAutoPlaced
            or CrashFindingCode.UnsetScriptVariables
            or CrashFindingCode.IncompatibleModifierType
            or CrashFindingCode.MissingModPath
            or CrashFindingCode.LauncherLaunchFailed
            or CrashFindingCode.ExecutableMissing
            or CrashFindingCode.CorruptGameSettings);

    private static string? TopSource(CrashSignals signals) =>
        signals.SourceCounts.Count == 0
            ? null
            : signals.SourceCounts
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
                .First()
                .Key;

    private static string? FindSourceForPhrase(CrashSignals signals, string phrase)
    {
        foreach (var block in signals.Blocks)
        {
            if (block.Text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return block.SourcePath;
            }
        }

        return null;
    }

    private static CrashModInfo? FindModForSource(IReadOnlyList<CrashModInfo> mods, string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        foreach (var mod in mods)
        {
            if (mod.SourceHits.Contains(source, StringComparer.OrdinalIgnoreCase))
            {
                return mod;
            }
        }

        return null;
    }
}
