namespace PdxLauncher.Core.Online.Translation;

/// <summary>
/// 「模型把提示词当成译文吐回来」的闸门。
/// <para>
/// 本地 Index-Translate 与自填 OpenAI 兼容接口的提示词都是我们自己拼的，所以这里只认
/// 我们自己写进去的标记：命中就按行截断，截完为空或与原文逐字相同就判为「没译出来」，
/// 让调用方保留原文并给出重试入口，而不是把 prompt 渲染成译文。
/// </para>
/// <para>
/// 不做通用语言判断，也不碰正常译文里的普通词（mod / workshop 之类）：只有「行首出现已知标记」
/// 或「出现 ChatML 记号 / 未闭合的 think 块」才算回吐，正常译文原样返回。
/// </para>
/// </summary>
public static class TranslationEchoGuard
{
    /// <summary>行首出现即视为提示词回吐的标记（模型会把「专名」改写成「专有名词」，所以只认句子骨架）。</summary>
    private static readonly string[] Markers =
    [
        "【源文】",
        "【约束要求】",
        "【硬性要求】",
        "只输出译文",
        "请将以下文本翻译为",
        "请将下面的文本翻译为",
        "把下面这段文本翻译成",
    ];

    /// <summary>ChatML 模板里才有的记号；正常译文里不会出现。带角色的整串放前面，避免把「assistant」当成译文。</summary>
    private static readonly string[] ChatTokens =
    [
        "<|im_start|>assistant",
        "<|im_start|>system",
        "<|im_start|>user",
        "<|im_start|>",
        "<|im_end|>",
        "<|endoftext",
    ];

    /// <summary>Index-Translate 模板里的 think 开标签（用转义写，避免与正文里的字符混淆）。</summary>
    private const string ThinkTag = "\u003Cthink\u003E";

    /// <summary>Index-Translate 模板里的 think 闭标签。</summary>
    private const string ThinkCloseTag = "\u003C/think\u003E";

    /// <summary>
    /// 切掉提示词尾巴：剥 think 块与 ChatML 记号 → 从最靠前的标记行截断 → 去首尾空白。
    /// </summary>
    /// <param name="text">模型输出或缓存里的译文；null/空白返回 null。</param>
    /// <returns>清洗后的文本；截完为空返回 null。</returns>
    public static string? Trim(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = StripMarkup(text);
        var cut = FindMarkerLine(cleaned);
        if (cut >= 0)
        {
            cleaned = cleaned[..cut];
        }

        cleaned = cleaned.Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>
    /// 这段文本是否带着提示词的痕迹（标记行、ChatML 记号或 think 块）。
    /// 缓存自愈用它判断「旧条目是不是被模型回吐污染过」。
    /// </summary>
    /// <param name="text">待检查文本。</param>
    public static bool ContainsPromptMarkup(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = StripMarkup(text);
        return cleaned.Length != text.Length || FindMarkerLine(cleaned) >= 0;
    }

    /// <summary>剥掉 think 块与 ChatML 记号；记号换成分行，后面残留的文字仍按行处理。</summary>
    private static string StripMarkup(string text)
    {
        var withoutThink = StripThinkBlocks(text);
        var builder = new StringBuilder(withoutThink.Length);
        var index = 0;
        while (index < withoutThink.Length)
        {
            var token = MatchToken(withoutThink, index);
            if (token is not null)
            {
                builder.Append('\n');
                index += token.Length;
                continue;
            }

            builder.Append(withoutThink[index]);
            index++;
        }

        return builder.ToString();
    }

    /// <summary>把成对的 think 块整段删掉；只剩开标签时截到结尾，只剩闭标签时留给记号处理。</summary>
    private static string StripThinkBlocks(string text)
    {
        var open = text.IndexOf(ThinkTag, StringComparison.Ordinal);
        if (open < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (open >= 0)
        {
            builder.Append(text, index, open - index);
            var close = text.IndexOf(ThinkCloseTag, open + ThinkTag.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                return builder.ToString();
            }

            index = close + ThinkCloseTag.Length;
            open = text.IndexOf(ThinkTag, index, StringComparison.Ordinal);
        }

        builder.Append(text, index, text.Length - index);
        return builder.ToString();
    }

    /// <summary>当前位置是否是一个 ChatML 记号；命中返回记号本身。</summary>
    private static string? MatchToken(string text, int index)
    {
        foreach (var token in ChatTokens)
        {
            if (index + token.Length <= text.Length
                && string.CompareOrdinal(text, index, token, 0, token.Length) == 0)
            {
                return token;
            }
        }

        return null;
    }

    /// <summary>最靠前的「标记行」起点；行首允许「1. 」「- 」这类列表前缀。没有返回 -1。</summary>
    private static int FindMarkerLine(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var newline = text.IndexOf('\n', start);
            var end = newline < 0 ? text.Length : newline;
            var line = text.AsSpan(start, end - start).Trim();
            if (StartsWithMarker(line))
            {
                return start;
            }

            if (newline < 0)
            {
                break;
            }

            start = newline + 1;
        }

        return -1;
    }

    /// <summary>去掉可选列表前缀后，行首是否就是我们的标记。</summary>
    private static bool StartsWithMarker(ReadOnlySpan<char> line)
    {
        var rest = StripListPrefix(line);
        foreach (var marker in Markers)
        {
            if (rest.StartsWith(marker.AsSpan(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>去掉「1. 」「1、」「1) 」这类前缀；找不到前缀时原样返回。</summary>
    private static ReadOnlySpan<char> StripListPrefix(ReadOnlySpan<char> line)
    {
        var digits = 0;
        while (digits < line.Length && char.IsDigit(line[digits]))
        {
            digits++;
        }

        if (digits == 0)
        {
            return line;
        }

        var index = digits;
        while (index < line.Length && (line[index] == '.' || line[index] == '、' || line[index] == ')' || line[index] == ' '))
        {
            index++;
        }

        return index > digits ? line[index..] : line;
    }
}