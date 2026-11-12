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
    /// 解析顶层块（<c>dlc019 = { name = "..." steam_id = "..." }</c>）：返回每个块的块名与块内标量键值对。
    /// <para>
    /// <see cref="Parse"/> 会把 <c>{ }</c> 整块忽略，读不到 <c>game/dlc_metadata/*.txt</c>
    /// 这种「一条 DLC 一个顶层块」的文件；本方法专门补这一层。
    /// 块内只取标量（键小写，值去引号），更内层的块整块忽略；块名原样保留（大小写敏感）。
    /// </para>
    /// <para>容错与 <see cref="Parse"/> 一致：BOM、行尾注释、未闭合块、畸形输入都不抛异常 —— 坏块跳过。</para>
    /// </summary>
    /// <param name="text">Clausewitz 文本。</param>
    /// <returns>按出现顺序排列的顶层块；没有可解析的块时为空列表。</returns>
    public static IReadOnlyList<ClausewitzBlock> ParseBlocks(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new List<ClausewitzBlock>();

        var index = 0;
        var length = text.Length;
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
                SkipToEndOfLine(text, ref index);
                continue;
            }

            index++; // 跳过 '='
            SkipWhitespaceAndComments(text, ref index);

            if (index < length && text[index] == '{')
            {
                var body = ReadBlockBody(text, ref index);
                if (body is null)
                {
                    // 未闭合的块：读到文件尾也没有配对的 '}'，放弃剩余内容但不抛异常。
                    break;
                }

                if (key.Length > 0)
                {
                    result.Add(new ClausewitzBlock(key, Parse(body)));
                }

                continue;
            }

            // 顶层标量：与 Parse 一样忽略，只把值吃掉，避免值被当成下一个块名。
            if (index < length && text[index] == '"')
            {
                index++;
                ReadQuotedValue(text, ref index);
            }
            else
            {
                ReadBareValue(text, ref index);
            }
        }

        return result;
    }

    /// <summary>
    /// 吃掉一个 <c>{ ... }</c> 块并返回花括号之间的原文。
    /// 引号内的花括号与注释里的花括号不算结构，支持嵌套块。
    /// </summary>
    /// <returns>块内原文；到文件尾仍未闭合时返回 null（<paramref name="index"/> 停在文件尾）。</returns>
    private static string? ReadBlockBody(string text, ref int index)
    {
        var length = text.Length;
        if (index >= length || text[index] != '{')
        {
            return null;
        }

        index++; // 跳过 '{'
        var start = index;
        var depth = 1;
        while (index < length)
        {
            var ch = text[index];
            if (ch == '"')
            {
                index++;
                ReadQuotedValue(text, ref index);
                continue;
            }

            if (ch == '#')
            {
                SkipToEndOfLine(text, ref index);
                continue;
            }

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
                if (depth == 0)
                {
                    return text.Substring(start, index - start - 1);
                }

                continue;
            }

            index++;
        }

        return null;
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
