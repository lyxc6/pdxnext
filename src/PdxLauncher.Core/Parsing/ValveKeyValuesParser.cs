namespace PdxLauncher.Core.Parsing;

/// <summary>
/// Valve KeyValues（VDF / ACF）容错解析器。支持：
/// <c>"key" { "k" "v" }</c> 嵌套块、<c>"key" "value"</c> 标量、<c>//</c> 行注释、转义引号与反斜杠。
/// 输入畸形时尽量恢复而不抛异常：多余的 <c>}</c> 忽略，未闭合的块在文件结尾自动收尾。
/// </summary>
public static class ValveKeyValuesParser
{
    /// <summary>
    /// 解析一段 VDF/ACF 文本，返回根节点（Key 为空字符串，Children 为顶层项）。
    /// 空输入返回空根节点。
    /// </summary>
    public static ValveKeyValuesNode Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var root = new ValveKeyValuesNode(string.Empty, null);
        var stack = new Stack<ValveKeyValuesNode>();
        stack.Push(root);

        var tokens = Tokenize(text);
        var index = 0;
        while (index < tokens.Count)
        {
            var token = tokens[index];
            if (token.IsBlockStart)
            {
                // 孤立的 '{'：忽略，避免把后续内容误吞进上一层
                index++;
                continue;
            }

            if (token.IsBlockEnd)
            {
                if (stack.Count > 1)
                {
                    stack.Pop();
                }

                index++;
                continue;
            }

            // 现在读到一个键；看下一个 token 决定是块还是标量
            var key = token.Value ?? string.Empty;
            index++;
            if (index >= tokens.Count)
            {
                // 只有键没有值：记成空标量，保持容错
                stack.Peek().AddChild(new ValveKeyValuesNode(key, string.Empty));
                break;
            }

            var next = tokens[index];
            if (next.IsBlockStart)
            {
                var block = new ValveKeyValuesNode(key, null);
                stack.Peek().AddChild(block);
                stack.Push(block);
                index++;
                continue;
            }

            if (next.IsBlockEnd)
            {
                // "key" } —— 键没有值就遇到收尾，记成空标量并让下一轮处理 '}'
                stack.Peek().AddChild(new ValveKeyValuesNode(key, string.Empty));
                continue;
            }

            stack.Peek().AddChild(new ValveKeyValuesNode(key, next.Value ?? string.Empty));
            index++;
        }

        return root;
    }

    /// <summary>解析文件内容；文件不存在或读失败返回空根节点。</summary>
    public static ValveKeyValuesNode ParseFile(Abstractions.IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(path);
        if (!fileSystem.FileExists(path))
        {
            return new ValveKeyValuesNode(string.Empty, null);
        }

        string text;
        try
        {
            text = fileSystem.ReadAllText(path);
        }
        catch (IOException)
        {
            return new ValveKeyValuesNode(string.Empty, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new ValveKeyValuesNode(string.Empty, null);
        }

        return Parse(text);
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var index = 0;
        var length = text.Length;

        // 跳过 BOM（如果调用方用其它方式读进来）
        if (length > 0 && text[0] == '\uFEFF')
        {
            index = 1;
        }

        while (index < length)
        {
            var ch = text[index];
            if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n')
            {
                index++;
                continue;
            }

            if (ch == '/' && index + 1 < length && text[index + 1] == '/')
            {
                index += 2;
                while (index < length && text[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (ch == '{')
            {
                tokens.Add(Token.Start());
                index++;
                continue;
            }

            if (ch == '}')
            {
                tokens.Add(Token.End());
                index++;
                continue;
            }

            if (ch == '"')
            {
                index++;
                var builder = new StringBuilder();
                while (index < length)
                {
                    var current = text[index];
                    if (current == '\\' && index + 1 < length)
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
                            case 'r':
                                builder.Append('\r');
                                break;
                            default:
                                builder.Append(escaped);
                                break;
                        }

                        index += 2;
                        continue;
                    }

                    if (current == '"')
                    {
                        index++;
                        break;
                    }

                    builder.Append(current);
                    index++;
                }

                tokens.Add(Token.Scalar(builder.ToString()));
                continue;
            }

            // 无引号 token（Steam 有时写裸值），读到空白或结构符号为止
            var start = index;
            while (index < length)
            {
                var current = text[index];
                if (current == ' ' || current == '\t' || current == '\r' || current == '\n'
                    || current == '{' || current == '}' || current == '"')
                {
                    break;
                }

                if (current == '/' && index + 1 < length && text[index + 1] == '/')
                {
                    break;
                }

                index++;
            }

            if (index > start)
            {
                tokens.Add(Token.Scalar(text.Substring(start, index - start)));
            }
            else
            {
                // 防御：无法识别的字符，跳过以免死循环
                index++;
            }
        }

        return tokens;
    }

    private readonly struct Token
    {
        private Token(string? value, bool blockStart, bool blockEnd)
        {
            Value = value;
            IsBlockStart = blockStart;
            IsBlockEnd = blockEnd;
        }

        public string? Value { get; }

        public bool IsBlockStart { get; }

        public bool IsBlockEnd { get; }

        public static Token Scalar(string value) => new(value, false, false);

        public static Token Start() => new(null, true, false);

        public static Token End() => new(null, false, true);
    }
}
