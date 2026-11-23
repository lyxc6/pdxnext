namespace PdxLauncher.Achievements;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 从游戏本地成就脚本里读取条件。只读安装目录下固定几个文本位置，不写盘、不起进程。
/// 匹配是严格相等的：Steam API 名 == 脚本块名，或 == 块内 localization 字段值，
/// 或 == 本地化提示键；绝不做相似度/模糊匹配。
/// </summary>
public sealed class AchievementConditionReader(IFileSystem fileSystem)
{
    /// <summary>单段脚本最多展示多少字符；超出后截断并标注，避免详情撑爆 UI。</summary>
    public const int MaxScriptLength = 2000;

    private const int MaxAliasDepth = 8;

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
        var scriptNames = ResolveScriptNames(apiName);
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

            foreach (var scriptName in scriptNames)
            {
                if (TryFindBlock(text, scriptName, out var block) && block.Length > 0)
                {
                    return BuildInfo(block, path, strings);
                }
            }

            var allBlocks = FindAllBlockNames(text);
            foreach (var blockName in allBlocks)
            {
                if (TryFindBlock(text, blockName, out var candidateBlock) && candidateBlock.Length > 0)
                {
                    var localizationKey = ReadFieldValue(candidateBlock, "localization");
                    if (scriptNames.Contains(localizationKey))
                    {
                        return BuildInfo(candidateBlock, path, strings);
                    }
                }
            }

            if (TryFindBlockByLocalizationField(text, scriptNames, out var localizedBlock))
            {
                return BuildInfo(localizedBlock, path, strings);
            }

            // 有些包的块名是展示键：脚本里的自定义提示键直接等于 Steam API 名。
            if (strings.ContainsKey(apiName)
                && TryFindBlock(text, apiName, out var hintBlock)
                && hintBlock.Length > 0)
            {
                return BuildInfo(hintBlock, path, strings);
            }
        }

        return AchievementConditionInfo.Empty;
    }

    private static HashSet<string> ResolveScriptNames(string apiName) =>
        new(StringComparer.OrdinalIgnoreCase) { apiName.Trim() };

    private static IReadOnlyList<string> FindAllBlockNames(string text)
    {
        var names = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            if (!TryFindAssignment(text, index, out var key, out var valueStart))
            {
                break;
            }

            index = valueStart;
            if (index < text.Length && text[index] == '{'
                && TryReadBlock(text, index, out _, out var nextIndex))
            {
                names.Add(key);
                index = nextIndex;
            }
        }

        return names;
    }

    private static bool TryFindBlockByLocalizationField(
        string text,
        HashSet<string> scriptNames,
        out string block)
    {
        block = string.Empty;
        foreach (var scriptName in scriptNames)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { scriptName };
            foreach (var candidate in candidates)
            {
                if (TryFindBlock(text, candidate, out var candidateBlock) && candidateBlock.Length > 0)
                {
                    var localization = ReadFieldValue(candidateBlock, "localization");
                    if (scriptNames.Contains(localization))
                    {
                        block = candidateBlock;
                        return true;
                    }
                }
            }
        }

        return false;
    }

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

    /// <summary>
    /// 找顶层 <c>name = { ... }</c> 块，按精确名匹配；引号与 # 注释不参与解析，
    /// 未闭合块直接丢弃，不会污染后面的块。
    /// </summary>
    private static bool TryFindBlock(string text, string name, out string block)
    {
        block = string.Empty;
        var index = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };

        while (index < text.Length)
        {
            if (!TryFindAssignment(text, index, out var key, out var valueStart))
            {
                break;
            }

            // 只接受行首的顶层赋值：happened / possible 内部的 key 不能冒充成就块。
            var lineStart = IsLineStart(text, index);
            if (!names.Contains(key)
                || valueStart >= text.Length
                || text[valueStart] != '{')
            {
                index = valueStart;
                if (lineStart && SkipBlockOrValue(text, index, out var skippedIndex))
                {
                    index = skippedIndex;
                }

                continue;
            }

            if (!TryReadBlock(text, valueStart, out var content, out var nextIndex))
            {
                return false;
            }

            block = content;
            index = nextIndex;
            return true;
        }

        return false;
    }

    private static bool IsLineStart(string text, int index)
    {
        for (var cursor = index - 1; cursor >= 0; cursor--)
        {
            if (text[cursor] == '\n')
            {
                return true;
            }

            if (!char.IsWhiteSpace(text[cursor]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SkipBlockOrValue(string text, int index, out int nextIndex)
    {
        nextIndex = index;
        if (index < text.Length && text[index] == '{')
        {
            return TryReadBlock(text, index, out _, out nextIndex);
        }

        SkipToNextLine(text, ref nextIndex);
        return true;
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
        string sourceFile,
        IReadOnlyDictionary<string, string> localization)
    {
        var happened = FindFieldText(block, "happened", out var happenedComplete);
        var possible = FindFieldText(block, "possible", out _);
        var hintKey = FindTooltipKey(happened);
        var summary = ResolveSummary(hintKey, localization);
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
    private static string FindTooltipKey(string happened)
    {
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
            if (value.Length > 0 && IsKeyToken(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string ResolveSummary(string key, IReadOnlyDictionary<string, string> localization)
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
                return string.Empty;
            }

            var cleaned = CleanLocalizationValue(value);
            if (cleaned.Length > 2 && cleaned[0] == '$' && cleaned[^1] == '$')
            {
                current = cleaned[1..^1].Trim();
                continue;
            }

            return cleaned;
        }

        return string.Empty;
    }

    private static string CleanLocalizationValue(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        var index = 0;
        var boldMatch = System.Text.RegularExpressions.Regex.Match(
            value,
            @"#bold\s*(.*?)#!",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        if (boldMatch.Success)
        {
            value = value.Remove(boldMatch.Index, boldMatch.Length).Insert(boldMatch.Index, boldMatch.Groups[1].Value + " ");
        }

        while (index < value.Length)
        {
            if (value[index] == '§')
            {
                var end = value.IndexOf('§', index + 1);
                if (end < 0)
                {
                    index = value.Length;
                }
                else if (end == index + 1 || value[index + 1] == '!')
                {
                    index = end + 1;
                }
                else
                {
                    var contentStart = index + (char.IsAsciiLetter(value[index + 1]) ? 2 : 1);
                    AppendLocalizationLine(result, value.AsSpan(contentStart, end - contentStart));
                    index = end + 1;
                    if (index < value.Length && value[index] == '!')
                    {
                        index++;
                    }
                }

                continue;
            }

            if (value[index] == '#')
            {
                var end = value.IndexOf('#', index + 1);
                if (end < 0)
                {
                    index = value.Length;
                }
                else
                {
                    AppendLocalizationLine(result, value.AsSpan(index + 1, end - index - 1));
                    index = end + 1;
                }

                continue;
            }

            var newline = value.IndexOf('\n', index);
            if (newline < 0)
            {
                AppendLocalizationLine(result, value.AsSpan(index));
                break;
            }

            AppendLocalizationLine(result, value.AsSpan(index, newline - index));
            index = newline + 1;
        }

        return result.ToString().Trim();
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
                SkipToNextLine(text, ref index);
                continue;
            }

            break;
        }
    }

    private static void SkipToNextLine(string text, ref int index)
    {
        SkipToLineEnd(text, ref index);
        if (index < text.Length && text[index] == '\n')
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
