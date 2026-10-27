namespace PdxLauncher.Core.Contracts;

/// <summary>
/// 主页底部的官方公告卡（来源：官方启动器的 Braze content cards 接口）。
/// <para>接口按「slot」返回卡片：<c>main</c> 是一张大图卡，<c>secondary-1</c> / <c>secondary-2</c> 是右侧两张小卡。
/// 实测 <c>content.title.en</c> / <c>content.text.en</c> 都是空串，卡片实际上只有图与链接。</para>
/// <para>本类型是纯数据投影：图片已由服务落盘，<see cref="NewsFeedItem.ImagePath"/> 是本地绝对路径。</para>
/// </summary>
public sealed record NewsFeedItem(
    string Slot,
    string? Title,
    string? Text,
    string? ImagePath,
    string? ImageUrl,
    string? Link)
{
    /// <summary>是否有可显示的本地图片。</summary>
    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath);

    /// <summary>是否有标题文本（实测接口里通常为空）。</summary>
    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    /// <summary>是否有正文文本（实测接口里通常为空）。</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    /// <summary>链接是否可交给浏览器打开（必须是绝对的 http/https）。</summary>
    public bool IsOpenable =>
        !string.IsNullOrWhiteSpace(Link)
        && Uri.TryCreate(Link, UriKind.Absolute, out var uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));
}

/// <summary>一批官方公告卡（一门游戏一次请求的结果，通常 0~3 张）。</summary>
public sealed record NewsFeed(IReadOnlyList<NewsFeedItem> Items)
{
    /// <summary>没有任何卡片时的实例（不联网、接口 404、解析失败都用它）。</summary>
    public static NewsFeed Empty { get; } = new(Array.Empty<NewsFeedItem>());

    /// <summary>是否没有可显示的卡片。</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>按 slot 名取卡片（找不到返回 null）。</summary>
    public NewsFeedItem? Find(string slot)
    {
        if (string.IsNullOrWhiteSpace(slot))
        {
            return null;
        }

        foreach (var item in Items)
        {
            if (string.Equals(item.Slot, slot, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }
}

/// <summary>公告 slot 名与官方渲染顺序（官方启动器常量 SECONDRY1/SECONDRY2/MAIN）。</summary>
public static class NewsFeedSlots
{
    /// <summary>左侧大图卡。</summary>
    public const string Main = "main";

    /// <summary>右侧第一张小卡。</summary>
    public const string Secondary1 = "secondary-1";

    /// <summary>右侧第二张小卡。</summary>
    public const string Secondary2 = "secondary-2";

    /// <summary>版式顺序：main、secondary-1、secondary-2。</summary>
    public static IReadOnlyList<string> Order { get; } = new[] { Main, Secondary1, Secondary2 };
}
