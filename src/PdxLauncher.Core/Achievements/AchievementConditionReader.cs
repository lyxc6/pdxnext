namespace PdxLauncher.Achievements;

using System.Globalization;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Steam;

/// <summary>
/// 从游戏本地成就脚本里读取条件。只读安装目录下固定几个文本位置，不写盘、不起进程。
/// 匹配是严格相等的：Steam API 名 == 脚本块名，或 == 块内 localization 字段值，
/// 或 == 本地化提示键；绝不做相似度/模糊匹配。
/// </summary>
public sealed partial class AchievementConditionReader(IFileSystem fileSystem)
{
    /// <summary>单段脚本最多展示多少字符；超出后截断并标注，避免详情撑爆 UI。</summary>
    public const int MaxScriptLength = 2000;

    private const int MaxAliasDepth = 8;

    /// <summary>
    /// 提示键间接层：<c>common/trigger_localization/*.txt</c> 里的
    /// <c>xxx_trigger = { global = ACHIEVEMENT_DESC_xxx }</c>。官方提示词先落在这里的键上，
    /// 再指向真正的本地化条目，只查成就 yml 会一条都拿不到。
    /// </summary>
    private static readonly string[] TriggerLocalizationDirectories =
    [
        "game/common/trigger_localization",
        "common/trigger_localization",
    ];

    /// <summary>
    /// 每个安装目录的脚本索引（块名 / localization 字段 → 块内容），只在第一次 <see cref="Read"/> 时建立。
    /// <para>
    /// 成就页一次刷新要查几百条成就；不做索引时每查一条都要重读并重扫全部成就脚本
    /// （CK3 是 12 份 / 80 KB），累计能把界面拖住好几秒。索引按「读取器实例」缓存，
    /// 数据源每次刷新都会新建一个读取器，因此不会跨刷新读到旧脚本。
    /// </para>
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<ScriptFileIndex>> _indexes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>提示键间接层索引，按安装目录缓存（只在第一次 <see cref="Read"/> 时建立）。</summary>
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _hintKeyIndexes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 索引对应的脚本清单签名（脚本路径 + 体积、trigger_localization 目录时间戳）。
    /// <para>
    /// 索引一旦建立就会一直复用；如果同一读取器使用期间脚本被增删改（自检会在同一目录里追加文件，
    /// 真实场景是玩家装/删 mod），签名变了就必须重建，否则新成就永远查不到条件。
    /// </para>
    /// </summary>
    private readonly Dictionary<string, string> _indexSignatures = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ScriptDirectories =
    [
        "",
        "game/common/achievements",
        "game/main_menu/common/achievements",
        "common",
    ];

    /// <summary>
    /// 读一个成就的条件。文件缺失 / 读失败 / 内容损坏都退化成空结果：
    /// 本类没有 logger，条件只是装饰性详情，绝不因为读不到而让成就页失败。
    /// </summary>
    /// <param name="installRoot">游戏安装根目录。</param>
    /// <param name="apiName">Steam 成就 API 名。</param>
    /// <param name="localization">游戏本地化键 → 文案；大小写不敏感。</param>
    public AchievementConditionInfo Read(
        string installRoot,
        string apiName,
        IReadOnlyDictionary<string, string> localization)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(localization);
        if (string.IsNullOrWhiteSpace(installRoot) || string.IsNullOrWhiteSpace(apiName))
        {
            return AchievementConditionInfo.Empty;
        }

        var strings = localization;
        var name = apiName.Trim();
        try
        {
            var files = GetIndex(installRoot);
            var hintKeys = _hintKeyIndexes[installRoot];
            var hit = FindBlock(files, name);
            if (hit is { } found)
            {
                return BuildInfo(found.Block, name, found.Path, strings, hintKeys);
            }

            // 未命中才关心「脚本是不是变了」：命中路径只做字典查找，218 条成就不会重复枚举目录。
            // 变了就重建一次再查（自检会在同一读取器下往同一安装目录追加脚本）。
            if (IsSignatureFresh(installRoot))
            {
                return AchievementConditionInfo.Empty;
            }

            files = GetIndex(installRoot, force: true);
            hintKeys = _hintKeyIndexes[installRoot];
            return FindBlock(files, name) is { } refreshed
                ? BuildInfo(refreshed.Block, name, refreshed.Path, strings, hintKeys)
                : AchievementConditionInfo.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // 条件只是装饰性详情：索引建不起来就等于没条件，绝不让成就页失败。
            return AchievementConditionInfo.Empty;
        }
    }

    /// <summary>按块名 / localization 字段精确找块，返回块内容与来源文件。</summary>
    private static (string Block, string Path)? FindBlock(IReadOnlyList<ScriptFileIndex> files, string name)
    {
        foreach (var file in files)
        {
            if (file.Blocks.TryGetValue(name, out var block) && block.Length > 0)
            {
                return (block, file.Path);
            }

            if (file.ByLocalization.TryGetValue(name, out var localizedBlock) && localizedBlock.Length > 0)
            {
                return (localizedBlock, file.Path);
            }
        }

        return null;
    }

    /// <summary>索引是否仍与磁盘一致（目录时间戳 + 脚本列表 + 体积）。</summary>
    private bool IsSignatureFresh(string installRoot)
    {
        if (!_indexSignatures.TryGetValue(installRoot, out var cachedSignature))
        {
            return false;
        }

        return string.Equals(cachedSignature, BuildSignature(installRoot), StringComparison.Ordinal);
    }

    /// <summary>取（必要时建立）某个安装目录的脚本索引。</summary>
    private IReadOnlyList<ScriptFileIndex> GetIndex(string installRoot, bool force = false)
    {
        if (!force && _indexes.TryGetValue(installRoot, out var cached))
        {
            return cached;
        }

        var signature = BuildSignature(installRoot);
        var files = new List<ScriptFileIndex>();
        var hintIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in FindScriptPaths(installRoot))
        {
            string text;
            try
            {
                if (!fileSystem.FileExists(path))
                {
                    continue;
                }

                text = fileSystem.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                continue;
            }

            files.Add(BuildFileIndex(path, text));
        }

        foreach (var relative in TriggerLocalizationDirectories)
        {
            var directory = Path.Combine(
                installRoot,
                relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
            foreach (var path in EnumerateFiles(directory, "*.txt"))
            {
                string text;
                try
                {
                    if (!fileSystem.FileExists(path))
                    {
                        continue;
                    }

                    text = fileSystem.ReadAllText(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
                {
                    continue;
                }

                foreach (var (key, block) in ReadTopLevelBlocks(text))
                {
                    var target = ReadFieldValue(block, "global");
                    if (target.Length > 0 && IsKeyToken(target))
                    {
                        hintIndex.TryAdd(key, target);
                    }
                }
            }
        }

        _indexes[installRoot] = files;
        _hintKeyIndexes[installRoot] = hintIndex;
        _indexSignatures[installRoot] = signature;
        return files;
    }

    /// <summary>
    /// 脚本集合的轻量签名：目录时间戳 + 路径 + 体积。
    /// <para>
    /// 只做「一次目录枚举 + 目录 mtime」，不逐文件 stat：218 条成就×每条 13 份脚本会多出上千次系统调用。
    /// 代价是同一秒内被追加内容（大小不变）可能漏判，这在真实场景（游戏更新 / 装删 mod）不会发生。
    /// </para>
    /// </summary>
    private string BuildSignature(string installRoot)
    {
        var parts = new List<string>();
        try
        {
            foreach (var path in FindScriptPaths(installRoot))
            {
                parts.Add(path + "|" + fileSystem.GetFileSize(path));
            }

            foreach (var relative in TriggerLocalizationDirectories)
            {
                var directory = Path.Combine(
                    installRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                parts.Add(DirectoryStamp(directory));
                foreach (var path in EnumerateFiles(directory, "*.txt"))
                {
                    parts.Add(path + "|" + fileSystem.GetFileSize(path));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return string.Empty;
        }

        parts.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join('\n', parts);
    }

    /// <summary>目录修改时间（目录内容增删都会变）；读不到就用空串。</summary>
    private static string DirectoryStamp(string directory)
    {
        try
        {
            return directory + "|" + Directory.GetLastWriteTimeUtc(directory).Ticks.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>枚举顶层 <c>name = { ... }</c> 块（跳过注释与未闭合块）。</summary>
    private static IEnumerable<(string Key, string Block)> ReadTopLevelBlocks(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (!TryFindAssignment(text, index, out var key, out var valueStart))
            {
                yield break;
            }

            if (valueStart >= text.Length || text[valueStart] != '{')
            {
                index = valueStart;
                SkipToNextLine(text, ref index);
                continue;
            }

            if (!TryReadBlock(text, valueStart, out var block, out var nextIndex))
            {
                yield break;
            }

            yield return (key, block);
            index = nextIndex;
        }
    }

    /// <summary>
    /// 扫一份脚本，收集顶层 <c>name = { ... }</c> 块，以及每个块 <c>localization</c> 字段的取值。
    /// 只接受行首的顶层赋值：<c>happened</c> / <c>possible</c> 内部的键不能冒充成就块。
    /// </summary>
    private static ScriptFileIndex BuildFileIndex(string path, string text)
    {
        var blocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byLocalization = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        while (index < text.Length)
        {
            if (!TryFindAssignment(text, index, out var key, out var valueStart))
            {
                break;
            }

            if (valueStart >= text.Length || text[valueStart] != '{')
            {
                index = valueStart;
                SkipToNextLine(text, ref index);
                continue;
            }

            if (!TryReadBlock(text, valueStart, out var block, out var nextIndex))
            {
                // 未闭合块直接丢弃，后面的块也不再解析：与旧的扫描口径一致。
                break;
            }

            // 整块跳过后再找下一个赋值，所以 happened / possible 内部的键不会被当成成就块。
            blocks.TryAdd(key, block);
            var localizationKey = ReadFieldValue(block, "localization");
            if (localizationKey.Length > 0)
            {
                byLocalization.TryAdd(localizationKey, block);
            }

            index = nextIndex;
        }

        return new ScriptFileIndex(path, blocks, byLocalization);
    }

    /// <summary>一份脚本的索引：块名 → 块内容，块 localization 字段 → 块内容。</summary>
    private sealed record ScriptFileIndex(
        string Path,
        IReadOnlyDictionary<string, string> Blocks,
        IReadOnlyDictionary<string, string> ByLocalization);

    private static string ReadFieldValue(string block, string field)
    {
        var index = 0;
        while (index < block.Length)
        {
            if (!TryFindAssignment(block, index, out var key, out var valueStart))
            {
                break;
            }

            if (string.Equals(key, field, StringComparison.OrdinalIgnoreCase))
            {
                var end = valueStart;
                SkipToLineEnd(block, ref end);
                return block[valueStart..end].Trim().Trim('"', '\'').Trim();
            }

            index = valueStart;
            if (index >= block.Length || block[index] != '{')
            {
                SkipToNextLine(block, ref index);
                continue;
            }

            if (!TryReadBlock(block, index, out _, out var nextIndex))
            {
                return string.Empty;
            }

            index = nextIndex;
        }

        return string.Empty;
    }

    private IEnumerable<string> FindScriptPaths(string installRoot)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in ScriptDirectories)
        {
            if (relative.Length == 0)
            {
                foreach (var path in EnumerateFiles(installRoot, "achievements.txt"))
                {
                    if (seen.Add(path))
                    {
                        yield return path;
                    }
                }

                continue;
            }

            var normalized = relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var directory = Path.Combine(installRoot, normalized);
            if (relative == "common")
            {
                foreach (var path in EnumerateFiles(directory, "achievements.txt")
                    .Concat(EnumerateFiles(Path.Combine(directory, "achievements"), "*.txt")))
                {
                    if (seen.Add(path))
                    {
                        yield return path;
                    }
                }

                continue;
            }

            foreach (var path in EnumerateFiles(directory, "*.txt"))
            {
                if (seen.Add(path))
                {
                    yield return path;
                }
            }
        }
    }

    private IEnumerable<string> EnumerateFiles(string directory, string pattern)
    {
        try
        {
            return fileSystem.EnumerateFiles(directory, pattern, recursive: false).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return [];
        }
    }

    private static bool TryFindAssignment(string text, int start, out string key, out int valueStart)
    {
        key = string.Empty;
        valueStart = start;
        var index = start;
        while (index < text.Length)
        {
            SkipIgnorable(text, ref index);
            var keyStart = index;
            while (index < text.Length && IsKeyCharacter(text[index]))
            {
                index++;
            }

            if (index == keyStart)
            {
                SkipToNextLine(text, ref index);
                continue;
            }

            var candidate = text[keyStart..index];
            SkipIgnorable(text, ref index);
            if (index >= text.Length || text[index] != '=')
            {
                // 不是赋值（例如 happened 块里的裸 token），跳到下一行继续找顶层块。
                index = keyStart;
                SkipToNextLine(text, ref index);
                continue;
            }

            index++;
            SkipIgnorable(text, ref index);
            key = candidate;
            valueStart = index;
            return true;
        }

        return false;
    }

    private static bool TryReadBlock(string text, int openBrace, out string content, out int nextIndex)
    {
        content = string.Empty;
        nextIndex = openBrace;
        var depth = 0;
        var inQuote = false;
        var quoteCharacter = '\0';
        var index = openBrace;
        while (index < text.Length)
        {
            var character = text[index];
            if (inQuote)
            {
                if (character == '\\')
                {
                    index += 2;
                    continue;
                }

                if (character == quoteCharacter)
                {
                    inQuote = false;
                }

                index++;
                continue;
            }

            if (character is '"' or '\'')
            {
                inQuote = true;
                quoteCharacter = character;
                index++;
                continue;
            }

            if (character == '#')
            {
                SkipToNextLine(text, ref index);
                continue;
            }

            if (character == '{')
            {
                depth++;
            }
            else if (character == '}')
            {
                depth--;
                if (depth == 0)
                {
                    content = text[(openBrace + 1)..index];
                    nextIndex = index + 1;
                    return true;
                }
            }

            index++;
        }

        return false;
    }

    private static AchievementConditionInfo BuildInfo(
        string block,
        string apiName,
        string sourceFile,
        IReadOnlyDictionary<string, string> localization,
        IReadOnlyDictionary<string, string> hintKeys)
    {
        var happened = FindFieldText(block, "happened", out var happenedComplete);
        var possible = FindFieldText(block, "possible", out _);

        // 官方提示词有两条来源：成就脚本里的 text / tooltip 键（可能指向 trigger_localization），
        // 以及游戏给这条成就写的 ACHIEVEMENT_DESC_<api>。前者优先，后者兜底。
        var summary = string.Empty;
        foreach (var hintKey in FindTooltipKeys(happened))
        {
            summary = ResolveSummary(hintKey, localization, hintKeys);
            if (summary.Length > 0)
            {
                break;
            }
        }

        if (summary.Length == 0)
        {
            summary = ResolveSummary(SteamAchievementLocalization.DescriptionKeyPrefix + apiName, localization, hintKeys);
        }

        return new AchievementConditionInfo(
            summary,
            happenedComplete ? Truncate(happened) : string.Empty,
            Truncate(possible),
            sourceFile,
            summary.Length > 0);
    }

    private static string FindFieldText(string block, string field, out bool complete)
    {
        complete = false;
        var index = 0;
        while (index < block.Length)
        {
            if (!TryFindAssignment(block, index, out var key, out var valueStart))
            {
                break;
            }

            index = valueStart;
            if (!string.Equals(key, field, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index < block.Length && block[index] == '{')
            {
                if (!TryReadBlock(block, index, out var content, out var nextIndex))
                {
                    return string.Empty;
                }

                complete = true;
                return content;
            }

            var end = index;
            SkipToNextLine(block, ref end);
            complete = true;
            return block[index..end];
        }

        return string.Empty;
    }

    /// <summary>提示键可出现在 happened 内任意 wrapper 里，不限制 wrapper 名称与嵌套层数。</summary>
    private static IReadOnlyList<string> FindTooltipKeys(string happened)
    {
        var keys = new List<string>();
        var index = 0;
        while (index < happened.Length)
        {
            if (!TryFindAssignment(happened, index, out var key, out var valueStart))
            {
                break;
            }

            index = valueStart;
            if (key is not ("text" or "tooltip") || index >= happened.Length)
            {
                continue;
            }

            var end = valueStart;
            SkipToLineEnd(happened, ref end);
            var value = happened[valueStart..end].Trim().Trim('"', '\'').Trim();
            if (value.Length > 0 && IsKeyToken(value) && !keys.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                keys.Add(value);
            }
        }

        return keys;
    }

    /// <summary>
    /// 段落里残留的 <c>$变量$</c> 换成展示文案（示例：CK3 的提示词引用活动名）。
    /// 内部键、查不到的变量直接剥掉，绝不把 <c>$xxx$</c> 原样交给界面。
    /// </summary>
    private static string ResolveVariables(
        string text,
        IReadOnlyDictionary<string, string> localization)
    {
        if (text.IndexOf('$') < 0)
        {
            return text;
        }

        return VariableRef().Replace(text, match =>
        {
            var key = match.Groups["key"].Value.Trim();
            if (key.Length == 0 || key.StartsWith("ACHIEVEMENT_", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (localization.TryGetValue(key, out var direct) && IsDisplayValue(direct))
            {
                return direct;
            }

            foreach (var prefix in VariableKeyPrefixes)
            {
                if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && localization.TryGetValue(prefix + key, out var prefixed)
                    && IsDisplayValue(prefixed))
                {
                    return prefixed;
                }
            }

            return string.Empty;
        });
    }

    private static bool IsDisplayValue(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value!.Contains('$', StringComparison.Ordinal)
        && !value!.Contains('[', StringComparison.Ordinal);

    /// <summary>概念名必须是一段可显示文案，不能是 <c>[Legend.GetProtagonist.GetName]</c> 这类脚本表达式。</summary>
    private static bool IsConceptName(string? value) => IsDisplayValue(value);

    /// <summary>
    /// 成就描述里 <c>$变量$</c> 常省略实体前缀（<c>$innovations$</c> 指 <c>game_concept_innovations</c>）。
    /// 只按这些固定前缀查，查得到才替换，绝不猜相似键。
    /// </summary>
    private static readonly string[] VariableKeyPrefixes =
    [
        "game_concept_",
        "trait_",
        "activity_",
        "activity_group_",
        "legend_",
        "culture_",
        "government_",
        "modifier_",
    ];

    /// <summary>句子里残留的变量引用（<c>$COUNTRY$</c> 之类），查不到实体名时剥掉。</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\$(?<key>[^$\r\n]*)\$")]
    private static partial System.Text.RegularExpressions.Regex VariableRef();

    /// <summary>
    /// 按键取官方提示词。先查成就本地化；再查 <c>trigger_localization</c> 里的
    /// <c>key = { global = ACHIEVEMENT_DESC_… }</c> 映射，改查它指向的本地化键。
    /// </summary>
    private static string ResolveSummary(
        string key,
        IReadOnlyDictionary<string, string> localization,
        IReadOnlyDictionary<string, string> hintKeys)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = key;
        for (var depth = 0; depth < MaxAliasDepth && current.Length > 0; depth++)
        {
            if (!seen.Add(current))
            {
                return string.Empty;
            }

            if (!localization.TryGetValue(current, out var value) || string.IsNullOrWhiteSpace(value))
            {
                if (!hintKeys.TryGetValue(current, out var mapped) || string.IsNullOrWhiteSpace(mapped))
                {
                    return string.Empty;
                }

                current = mapped;
                continue;
            }

            var cleaned = CleanLocalizationValue(ResolveVariables(value, localization), localization);
            if (cleaned.Length > 2 && cleaned[0] == '$' && cleaned[^1] == '$')
            {
                current = cleaned[1..^1].Trim();
                continue;
            }

            return cleaned;
        }

        return string.Empty;
    }

    private static string CleanLocalizationValue(string value, IReadOnlyDictionary<string, string> localization)
    {
        // 概念链接是句子里的词（`[lifestyle|E]` → 概念名）；能查到 game_concept_<key> 就用
        // 正式文案，查不到再退回链接里写的键（绝不把整个方括号剥掉留下残句）。
        value = LocalizationConceptLink().Replace(value, match =>
        {
            var parts = match.Groups["body"].Value.Split('|');
            var concept = parts[0].Trim();
            // 裸键可能被脚本表达式占用（`legend_protagonist = [Legend.GetProtagonist.GetName]`），
            // 概念名在 game_concept_<key>，带前缀的那条优先。
            if (localization.TryGetValue("game_concept_" + concept, out var prefixedName)
                && IsConceptName(prefixedName))
            {
                return prefixedName;
            }

            if (localization.TryGetValue(concept, out var name) && IsConceptName(name))
            {
                return name;
            }

            return parts.Length > 2 && parts[2].Trim().Length > 0 ? parts[2].Trim() : concept;
        });

        var result = new System.Text.StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            // 同一行里也可能夹着 #bold/#EMP 这类标记：这里不能只看换行，
            // 否则 `获得一套...里的#EMP 所有#!技能` 会原样带着标记上屏。
            var next = value.AsSpan(index).IndexOfAny("\n#£§");
            if (next < 0)
            {
                AppendLocalizationLine(result, value.AsSpan(index));
                break;
            }

            AppendLocalizationLine(result, value.AsSpan(index, next));
            index += next;
            if (value[index] == '\n')
            {
                index++;
                continue;
            }

            // 标记整体跳过（#bold / #EMP / #! / §Y / £icon£）；文字与标记永远按出现位置切开，
            // 不存在「行首是色标就吞掉整行」这种分支。
            // 两侧文字不能粘在一起：`#bold必读#!指南` 去掉标记后是「必读 指南」，不是一个词。
            if (result.Length > 0 && result[^1] != ' ')
            {
                result.Append(' ');
            }

            var markerEnd = index;
            SkipMarker(value, ref markerEnd);
            index = markerEnd < value.Length ? markerEnd : value.Length;
            if (index < value.Length && value[index] != ' ')
            {
                result.Append(' ');
            }
        }

        return CollapseSpaces(result.ToString());
    }

    /// <summary>把连续空白压成一个空格并去掉首尾空白（标记替换会在两侧各补一个空格）。</summary>
    private static string CollapseSpaces(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var character in text)
        {
            var isSpace = char.IsWhiteSpace(character);
            if (isSpace && lastWasSpace)
            {
                continue;
            }

            builder.Append(isSpace ? ' ' : character);
            lastWasSpace = isSpace;
        }

        return builder.ToString().Trim();
    }

    private static void AppendLocalizationLine(System.Text.StringBuilder result, ReadOnlySpan<char> line)
    {
        var quoted = false;
        foreach (var character in line)
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            result.Append(character);
        }
    }

    /// <summary>CK3 的 <c>[concept|E|显示名]</c> 概念链接；只保留可展示的那一段。</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\[(?<body>[^\r\n\[\]]*\|[^\r\n\[\]]*)\]")]
    private static partial System.Text.RegularExpressions.Regex LocalizationConceptLink();

    /// <summary>跳过一个 Clausewitz 富文本标记（从 <c>#</c> / <c>£</c> / <c>§</c> 开始）。</summary>
    private static void SkipMarker(string value, ref int index)
    {
        if (index >= value.Length)
        {
            return;
        }

        switch (value[index])
        {
            case '#':
                index++;
                while (index < value.Length
                    && (char.IsAsciiLetterOrDigit(value[index]) || value[index] is '_' or '!' or '*'))
                {
                    index++;
                }

                return;

            case '£':
                index++;
                while (index < value.Length && value[index] != '£' && value[index] != '\n')
                {
                    index++;
                }

                if (index < value.Length && value[index] == '£')
                {
                    index++;
                }

                return;

            case '§':
                index += Math.Min(2, value.Length - index);
                return;
        }

        index++;
    }


    private static string Truncate(string script)
    {
        var normalized = script.Trim();
        if (normalized.Length <= MaxScriptLength)
        {
            return normalized;
        }

        return normalized[..MaxScriptLength] + "\n[truncated]";
    }

    private static bool IsKeyCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-' or ':';

    private static bool IsKeyToken(string value)
    {
        foreach (var character in value)
        {
            if (!IsKeyCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    private static void SkipIgnorable(string text, ref int index)
    {
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            if (text[index] == '#')
            {
                // 注释必须整行跳过，否则这里会停在 '#' 上原地打转：
                // SkipToLineEnd 遇到行首的 '#' 立刻 break，索引不前进，成就脚本里
                // 只要有一条注释，读取就会死循环（页面一直显示「加载中」）。
                SkipCommentLine(text, ref index);
                continue;
            }

            break;
        }
    }

    private static void SkipToNextLine(string text, ref int index)
    {
        if (index < text.Length && text[index] == '#')
        {
            SkipCommentLine(text, ref index);
            return;
        }

        SkipToLineEnd(text, ref index);
        if (index < text.Length && text[index] == '\n')
        {
            index++;
        }
    }

    /// <summary>
    /// 跳过一整条注释行（从 '#' 到行尾，含换行符）。索引至少要前进一格，
    /// 防止任何调用点因为「停在 '#' 上」而卡死。
    /// </summary>
    private static void SkipCommentLine(string text, ref int index)
    {
        var start = index;
        while (index < text.Length && text[index] != '\n')
        {
            index++;
        }

        if (index < text.Length)
        {
            index++;
        }

        if (index == start)
        {
            index++;
        }
    }

    private static void SkipToLineEnd(string text, ref int index)
    {
        var inQuote = false;
        while (index < text.Length)
        {
            if (text[index] == '"')
            {
                inQuote = !inQuote;
            }
            else if (!inQuote && text[index] == '#')
            {
                break;
            }
            else if (!inQuote && text[index] == '\n')
            {
                break;
            }

            index++;
        }
    }
}
