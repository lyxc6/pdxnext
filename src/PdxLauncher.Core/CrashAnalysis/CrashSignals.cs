namespace PdxLauncher.Core.CrashAnalysis;

/// <summary>一份待分析的日志输入。</summary>
public sealed record CrashLogInput(
    string Name,
    string Path,
    string Text,
    long SizeBytes,
    bool FromCrashBundle);

/// <summary>
/// 日志信号汇总：错误块计数、脚本来源分布、错误聚类与时间范围。
/// 只做文本模式匹配，不解释游戏语义；规则层再决定这些信号意味着什么。
/// </summary>
public sealed class CrashSignals
{
    private CrashSignals(
        IReadOnlyList<CrashLogFileInfo> logFiles,
        IReadOnlyList<CrashLogBlock> blocks,
        IReadOnlyDictionary<string, int> sourceCounts,
        IReadOnlyList<CrashLogGroup> groups,
        DateTimeOffset? firstLogTime,
        DateTimeOffset? systemLogStartTime,
        DateTimeOffset? lastErrorTime,
        DateTimeOffset? lastScriptErrorTime)
    {
        LogFiles = logFiles;
        Blocks = blocks;
        SourceCounts = sourceCounts;
        Groups = groups;
        FirstLogTime = firstLogTime;
        SystemLogStartTime = systemLogStartTime;
        LastErrorTime = lastErrorTime;
        LastScriptErrorTime = lastScriptErrorTime;
    }

    /// <summary>参与分析的日志文件统计。</summary>
    public IReadOnlyList<CrashLogFileInfo> LogFiles { get; }

    /// <summary>全部日志块；规则层用它取原始证据。</summary>
    public IReadOnlyList<CrashLogBlock> Blocks { get; }

    /// <summary>脚本来源路径 → 出现该路径的错误块数。</summary>
    public IReadOnlyDictionary<string, int> SourceCounts { get; }

    /// <summary>错误聚类（按出现次数倒序）。</summary>
    public IReadOnlyList<CrashLogGroup> Groups { get; }

    /// <summary>所有日志里最早的时间。</summary>
    public DateTimeOffset? FirstLogTime { get; }

    /// <summary><c>system.log</c> 的第一条时间；用于判断游戏进程何时启动。</summary>
    public DateTimeOffset? SystemLogStartTime { get; }

    /// <summary>最后一条错误信号的时间。</summary>
    public DateTimeOffset? LastErrorTime { get; }

    /// <summary>最后一条脚本错误（<c>Script system error</c> / <c>PostValidate of effect</c>）的时间。</summary>
    public DateTimeOffset? LastScriptErrorTime { get; }

    /// <summary>脚本错误块数（<c>Script system error</c> / <c>PostValidate of effect</c>）。</summary>
    public int ScriptErrorBlocks { get; private set; }

    /// <summary>「手动创建自动放置建筑」出现次数。</summary>
    public int CreateBuildingAutoPlaced { get; private set; }

    /// <summary>变量未赋值就使用的出现次数。</summary>
    public int UnsetScriptVariables { get; private set; }

    /// <summary>孤立事件的出现次数。</summary>
    public int OrphanedEvents { get; private set; }

    /// <summary>脚本 modifier 未在代码中定义的出现次数。</summary>
    public int IncompatibleModifierTypes { get; private set; }

    /// <summary>重复定义的出现次数。</summary>
    public int DuplicateDefinitions { get; private set; }

    /// <summary>地图 locator / 港口节点警告的出现次数。</summary>
    public int MapLocatorIssues { get; private set; }

    /// <summary>缺失本地化键的出现次数（只做噪声统计，不单独下结论）。</summary>
    public int MissingLocalization { get; private set; }

    /// <summary>缺失游戏预期定义的出现次数。</summary>
    public int MissingExpectedDefinitions { get; private set; }

    /// <summary>非 UTF-8 BOM 编码警告的出现次数。</summary>
    public int Utf8BomWarnings { get; private set; }

    /// <summary>汇总输入日志。</summary>
    public static CrashSignals Collect(IReadOnlyList<CrashLogInput> inputs, DateTimeOffset? crashTime)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var logFiles = new List<CrashLogFileInfo>(inputs.Count);
        var blocks = new List<CrashLogBlock>();
        var sourceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var groupDrafts = new Dictionary<string, GroupDraft>(StringComparer.Ordinal);
        var aggregates = new Dictionary<string, BlockAggregate>(StringComparer.Ordinal);
        var counters = new SignalCounters();
        DateTimeOffset? firstLogTime = null;
        DateTimeOffset? systemStart = null;
        DateTimeOffset? lastErrorTime = null;
        DateTimeOffset? lastScriptErrorTime = null;

        foreach (var input in inputs)
        {
            var parsed = CrashLogParser.ParseLogBlocks(input.Text, crashTime ?? DateTimeOffset.Now);
            blocks.AddRange(parsed);

            int lineCount = CountLines(input.Text);
            int errorCount = 0;
            foreach (var block in parsed)
            {
                if (block.Time is { } time)
                {
                    if (firstLogTime is null || time < firstLogTime)
                    {
                        firstLogTime = time;
                    }

                    if (string.Equals(input.Name, "system.log", StringComparison.OrdinalIgnoreCase)
                        && (systemStart is null || time < systemStart))
                    {
                        systemStart = time;
                    }
                }

                if (CrashLogParser.IsErrorBlock(block))
                {
                    errorCount++;
                }

                if (!CrashLogParser.IsErrorBlock(block) && !MayHaveSignal(block))
                {
                    continue;
                }

                // PDX 游戏会把同一条错误同时写进 error.log / game.log / debug.log。
                // 同一个「时间 + 来源 + 正文」的记录按文件分别计数，最后取各文件里的最大次数：
                // 这样既不会把同一份日志复制三遍算三倍，也不会把同一文件里重复 136 次的错误压成 1 次。
                var dedupeKey = block.Origin
                    + "\u0001"
                    + (block.Time?.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
                    + "\u0001"
                    + block.Message;
                if (!aggregates.TryGetValue(dedupeKey, out var aggregate))
                {
                    aggregate = new BlockAggregate(block);
                    aggregates[dedupeKey] = aggregate;
                }

                aggregate.Add(input.Name);
            }

            logFiles.Add(new CrashLogFileInfo(
                input.Name,
                input.Path,
                input.SizeBytes,
                lineCount,
                errorCount,
                input.FromCrashBundle));
        }

        foreach (var aggregate in aggregates.Values)
        {
            int count = aggregate.MaxCount;
            bool signal = ApplyBlock(
                aggregate.Block,
                sourceCounts,
                counters,
                ref lastErrorTime,
                ref lastScriptErrorTime,
                count);

            if (!signal && !CrashLogParser.IsErrorBlock(aggregate.Block))
            {
                continue;
            }

            var signature = CrashLogParser.Signature(aggregate.Block);
            var source = aggregate.Block.SourcePath ?? string.Empty;
            var key = signature + "\u0001" + source;
            if (!groupDrafts.TryGetValue(key, out var draft))
            {
                draft = new GroupDraft(signature, source, aggregate.Block.Text);
                groupDrafts[key] = draft;
            }

            draft.Count += count;
            if (aggregate.Block.Time is { } groupTime && (draft.LastTime is null || groupTime > draft.LastTime))
            {
                draft.LastTime = groupTime;
            }
        }

        var groups = groupDrafts.Values
            .OrderByDescending(static g => g.Count)
            .ThenBy(static g => g.Signature, StringComparer.Ordinal)
            .Take(12)
            .Select(static g => new CrashLogGroup(
                g.Signature,
                g.Count,
                g.SourcePath.Length > 0 ? g.SourcePath : null,
                null,
                g.LastTime,
                Truncate(g.Sample, 220)))
            .ToArray();

        var signals = new CrashSignals(
            logFiles,
            blocks,
            sourceCounts,
            groups,
            firstLogTime,
            systemStart,
            lastErrorTime,
            lastScriptErrorTime);
        signals.ScriptErrorBlocks = counters.ScriptErrorBlocks;
        signals.CreateBuildingAutoPlaced = counters.CreateBuildingAutoPlaced;
        signals.UnsetScriptVariables = counters.UnsetScriptVariables;
        signals.OrphanedEvents = counters.OrphanedEvents;
        signals.IncompatibleModifierTypes = counters.IncompatibleModifierTypes;
        signals.DuplicateDefinitions = counters.DuplicateDefinitions;
        signals.MapLocatorIssues = counters.MapLocatorIssues;
        signals.MissingLocalization = counters.MissingLocalization;
        signals.MissingExpectedDefinitions = counters.MissingExpectedDefinitions;
        signals.Utf8BomWarnings = counters.Utf8BomWarnings;

        return signals;
    }

    /// <summary>按谓词取原始证据行；每条块最多取 <paramref name="linesPerBlock"/> 行，整体最多 <paramref name="maxLines"/> 行。</summary>
    public IReadOnlyList<string> FindEvidence(Func<CrashLogBlock, bool> predicate, int maxLines = 6, int linesPerBlock = 2)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var evidence = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in Blocks)
        {
            if (evidence.Count >= maxLines)
            {
                break;
            }

            if (!predicate(block) || !seen.Add(block.Text))
            {
                continue;
            }

            foreach (var line in block.RawLines.Take(linesPerBlock))
            {
                if (evidence.Count >= maxLines)
                {
                    break;
                }

                evidence.Add(Truncate(line, 240));
            }
        }

        return evidence;
    }

    /// <summary>替换聚类里的 Mod 名称（分析器在脚本路径映射完成后调用）。</summary>
    public IReadOnlyList<CrashLogGroup> WithModNames(IReadOnlyDictionary<string, string> sourceToMod)
    {
        ArgumentNullException.ThrowIfNull(sourceToMod);

        return Groups
            .Select(group => group with
            {
                ModName = group.SourcePath is { } source && sourceToMod.TryGetValue(source, out var name)
                    ? name
                    : null,
            })
            .ToArray();
    }

    private static bool ApplyBlock(
        CrashLogBlock block,
        Dictionary<string, int> sourceCounts,
        SignalCounters counters,
        ref DateTimeOffset? lastErrorTime,
        ref DateTimeOffset? lastScriptErrorTime,
        int multiplier)
    {
        var text = block.Text;
        bool signal = false;

        if (Contains(text, "Script system error") || Contains(text, "PostValidate of effect"))
        {
            counters.ScriptErrorBlocks += multiplier;
            signal = true;
            if (block.Time is { } scriptTime && (lastScriptErrorTime is null || scriptTime > lastScriptErrorTime))
            {
                lastScriptErrorTime = scriptTime;
            }
        }

        int createBuilding = CountOccurrences(text, "Attempting to manually create auto-placed building");
        if (createBuilding > 0)
        {
            counters.CreateBuildingAutoPlaced += createBuilding * multiplier;
            signal = true;
        }

        int unset = CountOccurrences(text, "is used but is never set");
        if (unset > 0)
        {
            counters.UnsetScriptVariables += unset * multiplier;
            signal = true;
        }

        int orphaned = CountOccurrences(text, "is orphaned");
        if (orphaned > 0)
        {
            counters.OrphanedEvents += orphaned * multiplier;
            signal = true;
        }

        int modifier = CountOccurrences(text, "defined in script but not in code");
        if (modifier > 0)
        {
            counters.IncompatibleModifierTypes += modifier * multiplier;
            signal = true;
        }

        int duplicate = CountOccurrences(text, "already defined");
        if (duplicate > 0)
        {
            counters.DuplicateDefinitions += duplicate * multiplier;
            signal = true;
        }

        int mapLocator = CountOccurrences(text, "is incomplete")
            + CountOccurrences(text, "Cannot find a port node");
        if (mapLocator > 0)
        {
            counters.MapLocatorIssues += mapLocator * multiplier;
            signal = true;
        }

        int localization = CountOccurrences(text, "Missing localization key");
        if (localization > 0)
        {
            counters.MissingLocalization += localization * multiplier;
            signal = true;
        }

        int missingExpected = CountOccurrences(text, "Missing expected");
        if (missingExpected > 0)
        {
            counters.MissingExpectedDefinitions += missingExpected * multiplier;
            signal = true;
        }

        int bom = CountOccurrences(text, "should be in utf8-bom encoding");
        if (bom > 0)
        {
            counters.Utf8BomWarnings += bom * multiplier;
            signal = true;
        }

        if (signal)
        {
            if (block.Time is { } time && (lastErrorTime is null || time > lastErrorTime))
            {
                lastErrorTime = time;
            }

            foreach (var path in CrashLogParser.ExtractSourcePaths(text))
            {
                sourceCounts[path] = sourceCounts.TryGetValue(path, out var count)
                    ? count + multiplier
                    : multiplier;
            }
        }

        return signal;
    }

    private static bool MayHaveSignal(CrashLogBlock block)
    {
        var text = block.Text;
        return Contains(text, "Script system error")
            || Contains(text, "PostValidate of effect")
            || Contains(text, "Attempting to manually create auto-placed building")
            || Contains(text, "is used but is never set")
            || Contains(text, "is orphaned")
            || Contains(text, "defined in script but not in code")
            || Contains(text, "already defined")
            || Contains(text, "is incomplete")
            || Contains(text, "Cannot find a port node")
            || Contains(text, "Missing localization key")
            || Contains(text, "Missing expected")
            || Contains(text, "should be in utf8-bom encoding");
    }

    private static int CountOccurrences(string text, string value)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        int count = 0;
        int index = 0;
        while (true)
        {
            int found = text.IndexOf(value, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return count;
            }

            count++;
            index = found + value.Length;
        }
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int count = 0;
        foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length > 0)
            {
                count++;
            }
        }

        return count;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);

    private sealed class SignalCounters
    {
        public int ScriptErrorBlocks { get; set; }

        public int CreateBuildingAutoPlaced { get; set; }

        public int UnsetScriptVariables { get; set; }

        public int OrphanedEvents { get; set; }

        public int IncompatibleModifierTypes { get; set; }

        public int DuplicateDefinitions { get; set; }

        public int MapLocatorIssues { get; set; }

        public int MissingLocalization { get; set; }

        public int MissingExpectedDefinitions { get; set; }

        public int Utf8BomWarnings { get; set; }
    }

    private sealed class BlockAggregate(CrashLogBlock block)
    {
        private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

        public CrashLogBlock Block { get; } = block;

        public int MaxCount => _counts.Count == 0 ? 1 : _counts.Values.Max();

        public void Add(string fileName)
        {
            _counts[fileName] = _counts.TryGetValue(fileName, out var count) ? count + 1 : 1;
        }
    }

    private sealed class GroupDraft(string signature, string sourcePath, string sample)
    {
        public string Signature { get; } = signature;

        public string SourcePath { get; } = sourcePath;

        public string Sample { get; } = sample;

        public int Count { get; set; }

        public DateTimeOffset? LastTime { get; set; }
    }
}
