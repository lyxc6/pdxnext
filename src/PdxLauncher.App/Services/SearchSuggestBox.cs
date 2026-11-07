namespace PdxLauncher.App.Services;

using Microsoft.UI.Xaml.Controls;

/// <summary>
/// 把 <see cref="SearchSuggestions"/> 算出来的名字贴到搜索框的下拉里。
/// </summary>
/// <remarks>
/// <para>
/// 只做「算 + 贴」这一件事；什么时候该调（<c>args.Reason == AutoSuggestionBoxTextChangeReason.UserInput</c>）
/// 留给页面判断。程序化改 Text 时也会触发 TextChanged——绑定回写、恢复上次搜索、以及两个搜索框
/// 共用同一个 <c>SearchText</c> 时互相回写——那种时候弹下拉就是和用户抢焦点。
/// </para>
/// <para>
/// 没建议时要显式收起下拉：<see cref="AutoSuggestBox"/> 不会因为 ItemsSource 变空自己收起来，
/// 会留下一个只有边框的空白弹层挂在输入框下面。
/// </para>
/// </remarks>
internal static class SearchSuggestBox
{
    /// <summary>按当前输入重算建议并贴到 <paramref name="box"/> 上；没有建议则收起下拉。</summary>
    /// <param name="box">要更新的搜索框。</param>
    /// <param name="names">候选名字（允许 null 项与重复项）；页面传"已经筛过的那批"即可。</param>
    /// <param name="query">搜索框当前的文本。</param>
    public static void Update(AutoSuggestBox box, IEnumerable<string?>? names, string? query)
    {
        ArgumentNullException.ThrowIfNull(box);

        var items = SearchSuggestions.Build(names, query);
        if (items.Count == 0)
        {
            box.IsSuggestionListOpen = false;
            return;
        }

        // 建议列表没变就不要重设 ItemsSource：每按一个键都换一遍会让下拉整块重建、
        // 已选中的候选项丢失，视觉上就是下拉闪一下。
        if (!IsSameSequence(box.ItemsSource, items))
        {
            box.ItemsSource = items;
        }

        box.IsSuggestionListOpen = true;
    }

    /// <summary>比较当前 ItemsSource 与刚算出来的建议是否完全一致。</summary>
    private static bool IsSameSequence(object? current, IReadOnlyList<string> items)
    {
        if (current is not IEnumerable<string> existing)
        {
            return false;
        }

        var index = 0;
        foreach (var item in existing)
        {
            if (index >= items.Count || !string.Equals(item, items[index], StringComparison.Ordinal))
            {
                return false;
            }

            index++;
        }

        return index == items.Count;
    }
}
