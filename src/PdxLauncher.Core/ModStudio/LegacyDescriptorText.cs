namespace PdxLauncher.Core.ModStudio;

/// <summary>
/// CK3 风格 <c>.mod</c> 文本的行级改写：只替换 <c>name</c>/<c>version</c>/<c>supported_version</c>/<c>tags</c>
/// 的值，注释、未知键、<c>path</c>、缩进与换行风格全部保留。
/// <para>
/// 这里刻意不复用 <c>ClausewitzParser</c>：它解析后只剩字典，写回去会丢掉注释和键顺序；
/// 描述符是用户会手动打开看的文件，来回一改就变样会很难排查。
/// </para>
/// </summary>
internal static class LegacyDescriptorText
{
    /// <summary>返回改写后的文本；缺失的键会补在 <c>name</c> 之后。</summary>
    public static string Apply(
        string text,
        string name,
        string version,
        string supported,
        IReadOnlyList<string> tags)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = Quote(name),
            ["version"] = Quote(version),
            ["supported_version"] = Quote(supported),
            ["tags"] = "{" + string.Join(" ", tags.Select(Quote)) + "}",
        };

        var result = ReplaceValues(text, values);
        result = InsertMissing(result, values, newline);
        return NormalizeEnd(result, newline);
    }

    private static string ReplaceValues(string text, IReadOnlyDictionary<string, string> values)
    {
        var assignments = Parse(text);
        foreach (var assignment in assignments.OrderByDescending(static item => item.KeyStart))
        {
            if (!values.TryGetValue(assignment.Key, out var replacement))
            {
                continue;
            }

            text = string.Concat(
                text.AsSpan(0, assignment.ValueStart),
                replacement,
                text.AsSpan(assignment.ValueEnd));
        }

        return text;
    }

    private static string InsertMissing(
        string text,
        IReadOnlyDictionary<string, string> values,
        string newline)
    {
        var assignments = Parse(text);
        var present = new HashSet<string>(assignments.Select(static item => item.Key), StringComparer.OrdinalIgnoreCase);

        var result = text;
        if (!present.Contains("name"))
        {
            result = "name=" + values["name"] + newline + result;
            assignments = Parse(result);
            present.Add("name");
        }

        var missing = new[] { "version", "supported_version", "tags" }
            .Where(key => !present.Contains(key))
            .ToArray();
        if (missing.Length == 0)
        {
            return result;
        }

        var anchor = assignments.FirstOrDefault(
            static item => item.Key.Equals("name", StringComparison.OrdinalIgnoreCase));
        if (anchor is null)
        {
            // 连 name 都解析不出来（例如整个文件是注释）：补在末尾，至少不覆盖已有内容。
            var lines = string.Concat(missing.Select(key => key + "=" + values[key] + newline));
            return result + (result.EndsWith(newline, StringComparison.Ordinal) ? string.Empty : newline) + lines;
        }

        var indent = LineIndent(result, anchor.KeyStart);
        var block = string.Concat(missing.Select(key => indent + key + "=" + values[key] + newline));
        var lineEnd = result.IndexOf('\n', anchor.ValueEnd);
        if (lineEnd < 0)
        {
            return result + newline + block.TrimEnd('\r', '\n');
        }

        return result[..(lineEnd + 1)] + block + result[(lineEnd + 1)..];
    }

    private static List<Assignment> Parse(string text)
    {
        var assignments = new List<Assignment>();
        var index = 0;
        while (index < text.Length)
        {
            var character = text[index];
            if (character == '#')
            {
                index = SkipLine(text, index);
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }

            if (character == '{')
            {
                // 不属于任何键的孤立块：整块跳过，避免把块里的键当成顶层键。
                index = FindBlockEnd(text, index);
                continue;
            }

            if (!IsKeyStart(character))
            {
                index++;
                continue;
            }

            var keyStart = index;
            var cursor = index + 1;
            while (cursor < text.Length && IsKeyChar(text[cursor]))
            {
                cursor++;
            }

            var key = text[keyStart..cursor];
            var equals = cursor;
            while (equals < text.Length && (text[equals] == ' ' || text[equals] == '\t'))
            {
                equals++;
            }

            if (equals >= text.Length || text[equals] != '=')
            {
                index = cursor;
                continue;
            }

            cursor = equals + 1;
            while (cursor < text.Length && (text[cursor] == ' ' || text[cursor] == '\t'))
            {
                cursor++;
            }

            var valueStart = cursor;
            if (cursor < text.Length && text[cursor] == '{')
            {
                var blockEnd = FindBlockEnd(text, cursor);
                assignments.Add(new Assignment(key, keyStart, valueStart, Math.Max(blockEnd, valueStart)));
                index = Math.Max(blockEnd, valueStart);
                continue;
            }

            var valueEnd = valueStart;
            var inQuote = false;
            while (cursor < text.Length)
            {
                var current = text[cursor];
                if (inQuote)
                {
                    if (current == '\\' && cursor + 1 < text.Length)
                    {
                        cursor += 2;
                        continue;
                    }

                    if (current == '"')
                    {
                        inQuote = false;
                    }

                    cursor++;
                    continue;
                }

                if (current == '"')
                {
                    inQuote = true;
                    cursor++;
                    continue;
                }

                if (current == '#' || current == '\n')
                {
                    break;
                }

                if (!char.IsWhiteSpace(current))
                {
                    valueEnd = cursor + 1;
                }

                cursor++;
            }

            assignments.Add(new Assignment(key, keyStart, valueStart, Math.Max(valueEnd, valueStart)));
            index = cursor;
        }

        return assignments;
    }

    private static int FindBlockEnd(string text, int openBrace)
    {
        var depth = 0;
        var inQuote = false;
        for (var index = openBrace; index < text.Length; index++)
        {
            var character = text[index];
            if (inQuote)
            {
                if (character == '\\' && index + 1 < text.Length)
                {
                    index++;
                    continue;
                }

                if (character == '"')
                {
                    inQuote = false;
                }

                continue;
            }

            if (character == '"')
            {
                inQuote = true;
                continue;
            }

            if (character == '#')
            {
                index = SkipLine(text, index) - 1;
                continue;
            }

            if (character == '{')
            {
                depth++;
                continue;
            }

            if (character == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index + 1;
                }
            }
        }

        return text.Length;
    }

    private static int SkipLine(string text, int index)
    {
        while (index < text.Length && text[index] != '\n')
        {
            index++;
        }

        return index;
    }

    private static string LineIndent(string text, int index)
    {
        var start = index;
        while (start > 0 && text[start - 1] != '\n')
        {
            start--;
        }

        var end = start;
        while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
        {
            end++;
        }

        return text[start..end];
    }

    private static string NormalizeEnd(string text, string newline)
    {
        var trimmed = text.TrimEnd('\r', '\n', ' ', '\t');
        return trimmed + newline;
    }

    private static bool IsKeyStart(char character) =>
        char.IsAsciiLetter(character) || character == '_';

    private static bool IsKeyChar(char character) =>
        char.IsAsciiLetterOrDigit(character) || character == '_';

    private static string Quote(string value) =>
        "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal) + "\"";

    private sealed record Assignment(string Key, int KeyStart, int ValueStart, int ValueEnd);
}
