namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 搜索框下拉建议的纯逻辑自检。
/// <para>
/// 这一层的错都不会崩：建议里出现重复行、前缀匹配被包含匹配挤到后面、工坊历史记住空词或
/// 记成 20 条，只会让搜索框「用起来不太对」，编译器和人工点测都容易漏。所以边界由这里钉住：
/// 空输入不铺建议、忽略大小写去重、前缀优先、上限 8 条、历史去重回队首。
/// </para>
/// </summary>
public static class SearchSuggestionsChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckDefaultMax();
        CheckEmptyQuery();
        CheckDedupeAndExactMatch();
        CheckPrefixBeforeContains();
        CheckCapAndLimits();
        CheckBlankNames();
        CheckPushOrderAndDedupe();
        CheckPushLimits();
        CheckPushDoesNotMutateInput();

        return ChecksContext.Failures - before;
    }

    private static void CheckDefaultMax()
    {
        ChecksContext.Equal("搜索建议：默认上限是 8 条", 8, SearchSuggestions.DefaultMax);
    }

    private static void CheckEmptyQuery()
    {
        string[] names = ["Alpha", "Beta"];

        ChecksContext.Equal(
            "搜索建议：空输入不铺建议",
            0,
            SearchSuggestions.Build(names, string.Empty).Count);
        ChecksContext.Equal(
            "搜索建议：纯空白输入不铺建议",
            0,
            SearchSuggestions.Build(names, "   ").Count);
        ChecksContext.Equal(
            "搜索建议：null 输入不铺建议",
            0,
            SearchSuggestions.Build(names, null).Count);
        ChecksContext.Equal(
            "搜索建议：null 候选返回空",
            0,
            SearchSuggestions.Build(null, "a").Count);
    }

    private static void CheckDedupeAndExactMatch()
    {
        string[] names = ["Rice", "rice", "RICE", "Rice Bowl", "Rimworld"];

        var built = SearchSuggestions.Build(names, "ri");
        ChecksContext.Equal("搜索建议：忽略大小写去重后 3 条", 3, built.Count);
        ChecksContext.Equal("搜索建议：去重保留首次出现的写法", "Rice", built[0]);
        ChecksContext.Equal("搜索建议：包含匹配在第二条", "Rice Bowl", built[1]);
        ChecksContext.Equal("搜索建议：包含匹配在第三条", "Rimworld", built[2]);

        ChecksContext.Equal(
            "搜索建议：跳过与输入完全相同的名字",
            "Rimworld Expanded",
            SearchSuggestions.Build(["Rimworld", "RIMWORLD", "Rimworld Expanded"], "rimworld")[0]);
        ChecksContext.Equal(
            "搜索建议：与输入相同（大小写不同）的名字也跳过",
            1,
            SearchSuggestions.Build(["Rice", "Rice Bowl"], "rice").Count);
    }

    private static void CheckPrefixBeforeContains()
    {
        string[] names = ["Valor", "Alpha", "Beta Alpha"];

        var built = SearchSuggestions.Build(names, "al");
        ChecksContext.Equal("搜索建议：前缀匹配排在包含匹配之前", "Alpha", built[0]);
        ChecksContext.Equal("搜索建议：包含匹配在后", "Valor", built[1]);
        ChecksContext.Equal("搜索建议：前缀 + 包含共 3 条", 3, built.Count);

        var stable = SearchSuggestions.Build(["Gamma", "Garden", "Game"], "ga");
        ChecksContext.Equal("搜索建议：组内保持输入顺序（前缀组）", "Gamma", stable[0]);
        ChecksContext.Equal("搜索建议：组内保持输入顺序（第二条）", "Garden", stable[1]);
        ChecksContext.Equal("搜索建议：组内保持输入顺序（第三条）", "Game", stable[2]);

        ChecksContext.Equal("搜索建议：大小写不敏感匹配", 1, SearchSuggestions.Build(["RICE"], "ri").Count);
    }

    private static void CheckCapAndLimits()
    {
        var many = Enumerable.Range(0, 30).Select(i => "Mod" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)).ToArray();

        ChecksContext.Equal("搜索建议：30 个候选截断到 8 条", 8, SearchSuggestions.Build(many, "mod").Count);
        ChecksContext.Equal("搜索建议：max = 3 时截断到 3 条", 3, SearchSuggestions.Build(many, "mod", 3).Count);
        ChecksContext.Equal("搜索建议：max = 0 时不要建议", 0, SearchSuggestions.Build(many, "mod", 0).Count);
        ChecksContext.Equal("搜索建议：max < 0 时不要建议", 0, SearchSuggestions.Build(many, "mod", -1).Count);
    }

    private static void CheckBlankNames()
    {
        string?[] names = [null, string.Empty, "   ", "Alpha"];

        var built = SearchSuggestions.Build(names, "a");
        ChecksContext.Equal("搜索建议：忽略 null/空白名字", 1, built.Count);
        ChecksContext.Equal("搜索建议：剩下的是有效名字", "Alpha", built[0]);
    }

    private static void CheckPushOrderAndDedupe()
    {
        List<string> history = ["old", "RICE", "older"];

        var pushed = SearchSuggestions.Push(history, "rice");
        ChecksContext.Equal("搜索历史：新词进队首", "rice", pushed[0]);
        ChecksContext.Equal("搜索历史：忽略大小写去重（旧写法被移走）", 3, pushed.Count);
        ChecksContext.Equal("搜索历史：原顺序保留", "old", pushed[1]);
        ChecksContext.Equal("搜索历史：原顺序保留第二条", "older", pushed[2]);

        var blank = SearchSuggestions.Push(history, "   ");
        ChecksContext.Equal("搜索历史：空白词不入历史", 3, blank.Count);
        ChecksContext.Equal("搜索历史：空白词不占队首", "old", blank[0]);

        var entryTrimmed = SearchSuggestions.Push(null, "  ck3  ");
        ChecksContext.Equal("搜索历史：提交词前后空白被去掉", "ck3", entryTrimmed[0]);
        ChecksContext.Equal("搜索历史：null 历史也能记", 1, entryTrimmed.Count);

        var noHistory = SearchSuggestions.Push(null, string.Empty);
        ChecksContext.Equal("搜索历史：空词 + null 历史 → 空列表", 0, noHistory.Count);
    }

    private static void CheckPushLimits()
    {
        List<string> history = Enumerable.Range(0, 12)
            .Select(i => "q" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        var capped = SearchSuggestions.Push(history, "new");
        ChecksContext.Equal("搜索历史：最多 8 条", 8, capped.Count);
        ChecksContext.Equal("搜索历史：截断保留最新的 8 条", "q6", capped[7]);

        ChecksContext.Equal("搜索历史：max = 0 时不留历史", 0, SearchSuggestions.Push(history, "new", 0).Count);
        ChecksContext.Equal("搜索历史：max = 2 时只留 2 条", 2, SearchSuggestions.Push(history, "new", 2).Count);
    }

    private static void CheckPushDoesNotMutateInput()
    {
        List<string> history = ["a", "b"];

        var pushed = SearchSuggestions.Push(history, "c");
        ChecksContext.Equal("搜索历史：不修改入参长度", 2, history.Count);
        ChecksContext.Equal("搜索历史：不修改入参内容", "a", history[0]);
        ChecksContext.Equal("搜索历史：返回的是新列表", 3, pushed.Count);
        ChecksContext.Check("搜索历史：返回列表不是同一个实例", !ReferenceEquals(history, pushed));
    }
}
