namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 主页公告卡的数据投影（<c>DataTemplate</c> 的 <c>x:DataType</c>）。
/// 只持有字符串与布尔，不含任何 UI 类型 —— 与 <see cref="GameCardItem"/> 同一写法。
/// </summary>
/// <param name="Slot">官方槽位名（main / secondary-1 / secondary-2），同时用于生成 AutomationId。</param>
/// <param name="Title">标题；官方实测多数为空串（卡片以图为主）。</param>
/// <param name="Text">正文；同样多数为空串。</param>
/// <param name="ImagePath">已下载到本地的图片绝对路径；为 null 时卡片只显示文字。</param>
/// <param name="Link">点开卡片时跳转的链接；可能是 <c>pdxlaun://</c> 深链。</param>
public sealed record NewsCardItem(
    string Slot,
    string? Title,
    string? Text,
    string? ImagePath,
    string? Link)
{
    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath);

    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    /// <summary>该卡没有任何可显示内容（图、标题、正文全空）。</summary>
    public bool IsEmpty => !HasImage && !HasTitle && !HasText;

    /// <summary>
    /// 空卡占位，用来代替 null。页面上的 x:Bind 直接写 <c>ViewModel.NewsMain.ImagePath</c>
    /// 这类路径，而 x:Bind 不生成空值检查（生成的代码里没有 <c>?.</c>），中间对象为 null
    /// 会在绑定刷新时抛 NullReferenceException —— 所以三张卡始终持有实例，用 IsEmpty 判断有没有内容。
    /// </summary>
    public static NewsCardItem Empty(string slot) => new(slot, null, null, null, null);

    /// <summary>可点开的卡片：链接必须是绝对 http(s) URI。</summary>
    public bool IsOpenable => TryGetLink(out _);

    /// <summary>图片的无障碍标识（与卡片按钮的 AutomationId 区分）。</summary>
    public string AutomationIdCover => "imgNews_" + Slot;

    /// <summary>把 core 的公告条目投影成卡片数据；空条目返回 null（模板据此隐藏该卡）。</summary>
    public static NewsCardItem? FromFeedItem(NewsFeedItem? item) => item is null
        ? null
        : new NewsCardItem(item.Slot, item.Title, item.Text, item.ImagePath, item.Link);

    /// <summary>
    /// 把链接解析成可交给 shell 打开的绝对 URI。非绝对、非 http/https（如官方用于
    /// 账号引导的 <c>pdxlaun://accountonboarding</c>）一律返回 false —— 本启动器不实现这些深链，
    /// 点开只会失败，所以这类卡片在界面上灰化。
    /// </summary>
    public bool TryGetLink(out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(Link))
        {
            return false;
        }

        if (!Uri.TryCreate(Link, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>
    /// 卡片按钮用数据项 ToString 当无障碍名称；record 的默认实现会把链接与路径全拼进去，
    /// 这里只给标题（没有标题时给占位）。
    /// </summary>
    public override string ToString() =>
        string.IsNullOrWhiteSpace(Title) ? Localizer.Instance.HomeNewsCardName : Title;
}
