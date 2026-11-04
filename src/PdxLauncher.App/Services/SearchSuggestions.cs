namespace PdxLauncher.App.Services;

/// <summary>
/// 搜索框下拉建议的纯逻辑：算「当前输入该给哪几个名字」，以及「刚提交的词怎么记进历史」。
/// <para>
/// 这里不碰任何 XAML 类型，所以自检工程可以原样链接（先例见 <see cref="WrapLayout"/>）。
/// 口径错了不会崩，只会表现为「下拉里同一行出现两次」「前缀匹配被包含匹配挤到后面」
/// 或「工坊历史里出现用户刚打完的同一个词」，编译器看不见，所以由
/// SearchSuggestionsChecks 把边界钉住。
/// </para>
/// </summary>
public static class SearchSuggestions
{
    /// <summary>下拉建议最多几条；工坊的最近搜索也按这个数截断。</summary>
    public const int DefaultMax = 8;

    /// <summary>
    /// 按当前输入挑出建议名字。
    /// <list type="bullet">
    /// <item>空/纯空白输入返回空：不要在「什么都没打」时铺满一列建议。</item>
    /// <item>忽略大小写去重（保留首次出现），并跳过与输入完全相同的名字（选了也没变化）。</item>
    /// <item>前缀匹配排在包含匹配之前，组内保持调用方给的顺序（名字列表本身已按页面排序）。</item>
    /// </list>
    /// </summary>
    /// <param name="names">候选名字；允许 null 项、空白项与重复项。</param>
    /// <param name="query">当前输入；前后空白会被忽略。</param>
    /// <param name="max">最多返回几条；≤ 0 视为不要建议。</param>
    public static IReadOnlyList<string> Build(IEnumerable<string?>? names, string? query, int max = DefaultMax)
    {
        if (names is null || max <= 0)
        {
            return Array.Empty<string>();
        }

        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefixMatches = new List<string>(max);
        var containsMatches = new List<string>(max);

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var candidate = name.Trim();
            if (!candidate.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!seen.Add(candidate))
            {
                continue;
            }

            var target = candidate.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
                ? prefixMatches
                : containsMatches;
            if (target.Count < max)
            {
                target.Add(candidate);
            }
        }

        if (prefixMatches.Count == 0)
        {
            return containsMatches;
        }

        if (containsMatches.Count == 0)
        {
            return prefixMatches;
        }

        var result = new List<string>(Math.Min(max, prefixMatches.Count + containsMatches.Count));
        result.AddRange(prefixMatches);
        foreach (var candidate in containsMatches)
        {
            if (result.Count >= max)
            {
                break;
            }

            result.Add(candidate);
        }

        return result;
    }

    /// <summary>
    /// 把一个刚提交的搜索词记进历史：新词在队首，忽略大小写去重，最多 <paramref name="max"/> 条。
    /// 空/纯空白词不入历史（点开下拉只按回车不该留下一条空记录）。
    /// </summary>
    /// <param name="history">旧历史；null 视为空，不会被修改。</param>
    /// <param name="query">刚提交的词；保留用户这次输入的大小写。</param>
    /// <param name="max">历史最多几条；≤ 0 视为不留历史。</param>
    /// <returns>新列表；调用方直接替换设置里的旧列表。</returns>
    public static List<string> Push(IReadOnlyList<string>? history, string? query, int max = DefaultMax)
    {
        var result = new List<string>(max <= 0 ? 0 : max);
        if (max <= 0)
        {
            return result;
        }

        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length > 0)
        {
            result.Add(trimmed);
        }

        if (history is null)
        {
            return result;
        }

        foreach (var item in history)
        {
            if (result.Count >= max)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var entry = item.Trim();
            if (!result.Contains(entry, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(entry);
            }
        }

        return result;
    }
}
