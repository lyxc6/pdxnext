namespace PdxLauncher.Core.Parsing;

/// <summary>
/// Clausewitz（<c>k = v</c>）文本解析器，用于 <c>.dlc</c> / <c>.mod</c> / <c>descriptor.mod</c>。
/// 只返回顶层标量键值对，键统一小写，值去掉包裹引号（保留原值）。
/// 容错：CRLF、BOM、行尾 <c>#</c> 注释、空行、未知键、重复键（后者覆盖前者）、
/// <c>tags={ "A" "B" }</c> 与嵌套 <c>{ }</c> 块（整块忽略）、无引号数字值。
/// 任何畸形输入都不抛异常。
/// </summary>
public static class ClausewitzParser
{
    /// <summary>
    /// 解析一段 Clausewitz 文本，返回顶层标量键值对（键为小写）。
    /// 键值对没有值时以空字符串记入。
    /// </summary>
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var index = 0;
        var length = text.Length;

        // 跳过 UTF-8 BOM
        if (length > 0 && text[0] == '\uFEFF')
        {
            index = 1;
        }

        while (index < length)
        {
            SkipWhitespaceAndComments(text, ref index);

            if (index >= length)
            {
                break;
            }

            var ch = text[index];
            if (ch == '{' || ch == '}' || ch == '=')
            {
                // 顶层出现结构符号：跳过，保持容错
                index++;
                continue;
            }

            if (!TryReadBareToken(text, ref index, out var key))
            {
                index++;
                continue;
            }

            SkipWhitespaceAndComments(text, ref index);
            if (index >= length || text[index] != '=')
            {
                // 没有 '='：这一行不是键值对，丢掉
                SkipToEndOfLine(text, ref index);
                continue;
            }

            index++; // 跳过 '='
            SkipWhitespaceAndComments(text, ref index);

            string value;
            if (index < length && text[index] == '"')
            {
                index++;
                value = ReadQuotedValue(text, ref index);
            }
            else if (index < length && text[index] == '{')
            {
                // tags={ ... } 之类的块：整块跳过，不进字典
                SkipBlock(text, ref index);
                SkipToEndOfLine(text, ref index);
                continue;
            }
            else
            {
                value = ReadBareValue(text, ref index);
            }

            if (key.Length == 0)
            {
                continue;
            }

            result[key.ToLowerInvariant()] = value;
        }

        return result;
    }

    /// <summary>
    /// 解析文件；文件不存在或读取失败返回空字典（并记一条 Warn）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseFile(
        Abstractions.IFileSystem fileSystem,
        string path,
        Abstractions.IAppLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(path);
        if (!fileSystem.FileExists(path))
        {
            logger?.Warn("Clausewitz 文件不存在，跳过: " + path);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        string text;
        try
        {
            text = fileSystem.ReadAllText(path);
        }
        catch (IOException ex)
        {
            logger?.Warn("读取 Clausewitz 文件失败，跳过: " + path + " (" + ex.Message + ")");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.Warn("无权读取 Clausewitz 文件，跳过: " + path + " (" + ex.Message + ")");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return Parse(text);
    }

    private static void SkipWhitespaceAndComments(string text, ref int index)
    {
        var length = text.Length;
        while (index < length)
        {
            var ch = text[index];
            if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n')
            {
                index++;
                continue;
            }

            if (ch == '#')
            {
                SkipToEndOfLine(text, ref index);
                continue;
            }

            break;
        }
    }

    private static void SkipToEndOfLine(string text, ref int index)
    {
        var length = text.Length;
        while (index < length && text[index] != '\n')
        {
            index++;
        }

        if (index < length)
        {
            index++; // 吃掉换行符
        }
    }

    private static bool TryReadBareToken(string text, ref int index, out string token)
    {
        var length = text.Length;
        var start = index;
        while (index < length)
        {
            var ch = text[index];
            if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n' || ch == '=' || ch == '{' || ch == '}'
                || ch == '#' || ch == '"')
            {
                break;
            }

            index++;
        }

        if (index == start)
        {
            token = string.Empty;
            return false;
        }

        token = text.Substring(start, index - start);
        return true;
    }

    private static string ReadBareValue(string text, ref int index)
    {
        var length = text.Length;
        var start = index;
        while (index < length)
        {
            var ch = text[index];
            if (ch == '\r' || ch == '\n' || ch == '#' || ch == '{' || ch == '}')
            {
                break;
            }

            index++;
        }

        return text.Substring(start, index - start).Trim();
    }

    private static string ReadQuotedValue(string text, ref int index)
    {
        var length = text.Length;
        var builder = new StringBuilder();
        while (index < length)
        {
            var ch = text[index];
            if (ch == '\\' && index + 1 < length)
            {
                var escaped = text[index + 1];
                switch (escaped)
                {
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    default:
                        builder.Append(escaped);
                        break;
                }

                index += 2;
                continue;
            }

            if (ch == '"')
            {
                index++;
                return builder.ToString();
            }

            builder.Append(ch);
            index++;
        }

        // 未闭合的引号：把剩下的都当值
        return builder.ToString();
    }

    private static void SkipBlock(string text, ref int index)
    {
        var length = text.Length;
        if (index >= length || text[index] != '{')
        {
            return;
        }

        var depth = 0;
        while (index < length)
        {
            var ch = text[index];
            if (ch == '{')
            {
                depth++;
                index++;
                continue;
            }

            if (ch == '}')
            {
                depth--;
                index++;
                if (depth <= 0)
                {
                    return;
                }

                continue;
            }

            if (ch == '#')
            {
                SkipToEndOfLine(text, ref index);
                continue;
            }

            index++;
        }
    }
}
