namespace PdxLauncher.App.ViewModels;

using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Mods;

/// <summary>
/// PDX Mods 在线列表里的一行：把 <see cref="PdxModsSummary"/> 翻译成界面直接可绑的文案。
/// 本类不含任何 Microsoft.UI.Xaml 类型；只有封面路径是可观察的 ——
/// 封面要从 <c>modscontent.paradox-interactive.com</c> 下载到本地，补到图后就地刷新这一行。
/// </summary>
public sealed partial class OnlineModRowViewModel : LocalizedObservableObject
{
    /// <summary>封面本地缓存路径；还没下载好时为 null。</summary>
    [ObservableProperty]
    private string? _coverPath;

    public OnlineModRowViewModel(PdxModsSummary summary, int thumbnailSize)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Summary = summary;
        ThumbnailSize = thumbnailSize;
    }

    /// <summary>搜索结果原始记录，打开详情时直接取。</summary>
    public PdxModsSummary Summary { get; }

    /// <summary>PDX Mods 的数字 id（字符串形态，用于详情接口与网页跳转）。</summary>
    public string ModId => Summary.ModId;

    /// <summary>mod 名称。</summary>
    public string Name => Summary.DisplayName;

    /// <summary>作者。</summary>
    public string AuthorText => string.IsNullOrWhiteSpace(Summary.Author)
        ? string.Empty
        : T(Keys.ModlibOnlineAuthor, Summary.Author);

    /// <summary>简介（官方只给一句话，长描述要另取详情）。</summary>
    public string ShortDescription => Summary.ShortDescription;

    /// <summary>简介是否为空（为空时不占行高）。</summary>
    public bool HasShortDescription => !string.IsNullOrWhiteSpace(ShortDescription);

    /// <summary>是否有可用封面（XAML 用它决定显示图片还是占位图标）。</summary>
    public bool HasCover => !string.IsNullOrWhiteSpace(CoverPath);

    /// <summary>封面边长（像素），跟设置里的「Mod 图片大小」三档一致。</summary>
    public double ThumbnailSize { get; }

    /// <summary>评分文案：4.8 分（123 人评分）；无评分时为空串。</summary>
    public string RatingText => Summary.RatingsTotal > 0
        ? T(
            Keys.ModlibOnlineRating,
            Summary.Rating.ToString("0.##", CultureInfo.InvariantCulture),
            Summary.RatingsTotal)
        : string.Empty;

    /// <summary>订阅数文案：12,345 订阅；未知（0）时为空串。</summary>
    public string SubscriptionsText => Summary.Subscriptions > 0
        ? T(Keys.ModlibOnlineSubscriptions, Summary.Subscriptions.ToString("N0", CultureInfo.CurrentCulture))
        : string.Empty;

    /// <summary>最近更新文案：更新于 2025/08/04；未知时为空串。</summary>
    public string UpdatedText => Summary.UpdatedOn is { } updated
        ? T(Keys.ModlibOnlineUpdated, updated.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture))
        : string.Empty;

    /// <summary>支持版本文案：支持版本 1.1.*；未声明时为空串。</summary>
    public string SupportedVersionText => string.IsNullOrWhiteSpace(Summary.GameVersion)
        ? string.Empty
        : T(Keys.ModlibSupportedVersion, Summary.GameVersion);

    /// <summary>副标题：作者 · 评分 · 订阅数 · 更新时间，空段自动省略，不留孤立分隔符。</summary>
    public string SubtitleText => string.Join(
        " · ",
        new[] { AuthorText, RatingText, SubscriptionsText, UpdatedText }
            .Where(static part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>行的自动化 Id（每个 mod 唯一，供 UI 测试定位）。</summary>
    public string RowAutomationId => "onlineModRow_" + ModId;

    /// <summary>PDX Mods 网页详情页地址。</summary>
    public string WebUrl => "https://mods.paradoxplaza.com/mods/" + ModId;

    /// <summary>行内「用浏览器打开网页」按钮的自动化 Id（每行唯一）。</summary>
    public string OpenBrowserAutomationId => "onlineModOpenBrowser_" + ModId;

    partial void OnCoverPathChanged(string? value) => OnPropertyChanged(nameof(HasCover));
}
