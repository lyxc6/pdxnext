namespace PdxLauncher.Core.Online.Translation;

/// <summary>
/// 按句读把长文本切成若干块，给有单次长度上限的翻译引擎用。
/// <para>
/// 切点在「分隔符之后」，所以按顺序拼接这些块能还原原文，译文拼接后也保留段落结构。
/// </para>
/// </summary>
internal static class TranslationTextSplitter
{
    /// <summary>把文本切成每块不超过 <paramref name="maxChars"/> 的若干段。</summary>
    /// <param name="text">原文。</param>
    /// <param name="maxChars">单块最大字符数。</param>
    public static IReadOnlyList<string> SplitText(string text, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChars);

        if (text.Length <= maxChars)
        {
            return [text];
        }

        var chunks = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var remaining = text.Length - start;
            if (remaining <= maxChars)
            {
                chunks.Add(text[start..]);
                break;
            }

            var window = text.AsSpan(start, maxChars);
            var cut = -1;
            for (var i = window.Length - 1; i > 0; i--)
            {
                if (IsCutCharacter(window[i]))
                {
                    cut = i + 1;
                    break;
                }
            }

            if (cut <= 0)
            {
                // 一整段没有任何标点：硬切，总比整段译不出来强。
                cut = window.Length;
            }

            chunks.Add(text[start..(start + cut)]);
            start += cut;
        }

        return chunks;
    }

    /// <summary>可以作为切点的字符：换行与中英句读符号。</summary>
    private static bool IsCutCharacter(char value) =>
        value is '\n' or '。' or '！' or '？' or '；' or '，' or '、'
            or '.' or '!' or '?' or ';' or ',' or ' ';
}
