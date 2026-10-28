namespace PdxLauncher.App.ViewModels;

using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Mods;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 创意工坊列表里的一行。两个平台共用同一套界面，
/// 所以这里把 <see cref="SteamWorkshopItem"/> 与 <see cref="PdxModsSummary"/> 归一成同一组只读文案。
/// 本类不含任何 Microsoft.UI.Xaml 类型；只有封面路径是可观察的 ——
/// 封面要下载到本地，补到图后就地刷新这一行。
/// </summary>
public sealed partial class WorkshopRowViewModel : LocalizedObservableObject
{
    /// <summary>封面本地缓存路径；还没下载好时为 null。</summary>
    [ObservableProperty]
    private string? _coverPath;

    private WorkshopRowViewModel(
        WorkshopSource source,
        string id,
        string name,
        string authorText,
        string? ratingText,
        string? subscriptionsText,
        string? updatedText,
        string? sizeText,
        string? coverUrl,
        double thumbnailSize,
        string webUrl,
        string? steamUrl,
        SteamWorkshopItem? steamItem,
        PdxModsSummary? pdxSummary)
    {
        Source = source;
        Id = id;
        Name = name;
        AuthorText = authorText;
        RatingText = ratingText ?? string.Empty;
        SubscriptionsText = subscriptionsText ?? string.Empty;
        UpdatedText = updatedText ?? string.Empty;
        SizeText = sizeText ?? string.Empty;
        CoverUrl = coverUrl;
        ThumbnailSize = thumbnailSize;
        WebUrl = webUrl;
        SteamUrl = steamUrl;
        SteamItem = steamItem;
        PdxSummary = pdxSummary;
    }

    /// <summary>这行来自哪个平台。</summary>
    public WorkshopSource Source { get; }

    /// <summary>平台内 id：Steam 是 publishedfileid，PDX 是数字 modId。</summary>
    public string Id { get; }

    /// <summary>标题。</summary>
    public string Name { get; }

    /// <summary>原始 Steam 条目（详情弹窗用）；PDX 行为 null。</summary>
    public SteamWorkshopItem? SteamItem { get; }

    /// <summary>原始 PDX Mods 条目（详情弹窗用）；Steam 行为 null。</summary>
    public PdxModsSummary? PdxSummary { get; }

    /// <summary>作者文案；Steam 只拿得到 SteamID64。</summary>
    public string AuthorText { get; }

    /// <summary>评分文案；无评分时为空串。</summary>
    public string RatingText { get; }

    /// <summary>订阅数文案；未知时为空串。</summary>
    public string SubscriptionsText { get; }

    /// <summary>最近更新文案；未知时为空串。</summary>
    public string UpdatedText { get; }

    /// <summary>体积文案；未知时为空串（PDX 列表没有体积）。</summary>
    public string SizeText { get; }

    /// <summary>副标题：作者 · 评分 · 订阅数 · 更新时间，空段自动省略，不留孤立分隔符。</summary>
    public string SubtitleText => string.Join(
        " · ",
        new[] { AuthorText, RatingText, SubscriptionsText, UpdatedText }
            .Where(static part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>封面原始地址（下载完之前 XAML 用占位图标）。</summary>
    public string? CoverUrl { get; }

    /// <summary>是否有可用封面。</summary>
    public bool HasCover => !string.IsNullOrWhiteSpace(CoverPath);

    /// <summary>封面边长（像素），跟设置里的「Mod 图片大小」三档一致。</summary>
    public double ThumbnailSize { get; }

    /// <summary>网页详情地址（两个平台都有）。</summary>
    public string WebUrl { get; }

    /// <summary>Steam 客户端地址；PDX 行为 null。</summary>
    public string? SteamUrl { get; }

    /// <summary>能不能用 Steam 客户端打开（只有 Steam 行可以）。</summary>
    public bool CanOpenSteam => !string.IsNullOrWhiteSpace(SteamUrl);

    /// <summary>用于封面缓存的文件名（含来源前缀，两套 id 空间不会撞）。</summary>
    public string CoverCacheKey => (Source == WorkshopSource.Steam ? "steam/" : "pdx/") + Id;

    /// <summary>行的自动化 Id（每行唯一，供 UI 测试定位）。</summary>
    public string RowAutomationId => "workshopRow_" + (Source == WorkshopSource.Steam ? "steam_" : "pdx_") + Id;

    /// <summary>行内「用浏览器打开」按钮的自动化 Id。</summary>
    public string OpenBrowserAutomationId => "workshopOpenBrowser_" + Id;

    /// <summary>行内「在 Steam 中打开」按钮的自动化 Id。</summary>
    public string OpenSteamAutomationId => "workshopOpenSteam_" + Id;

    /// <summary>从 Steam 列表条目建一行。</summary>
    /// <param name="item">Steam 条目。</param>
    /// <param name="thumbnailSize">封面边长（像素）。</param>
    public static WorkshopRowViewModel FromSteam(SteamWorkshopItem item, int thumbnailSize)
    {
        ArgumentNullException.ThrowIfNull(item);

        var author = string.IsNullOrWhiteSpace(item.AuthorSteamId)
            ? string.Empty
            : T(Keys.WorkshopAuthor, item.AuthorSteamId);
        // star_rating 为 0/负数表示「还没攒够评分」，这时有票数也不该显示「-1 分」。
        var rating = item.TotalVotes > 0 && item.StarRating > 0
            ? T(
                Keys.WorkshopRating,
                item.StarRating.ToString("0.##", CultureInfo.InvariantCulture),
                item.TotalVotes)
            : null;
        var subscriptions = item.Subscriptions > 0
            ? T(Keys.WorkshopSubscriptions, item.Subscriptions.ToString("N0", CultureInfo.CurrentCulture))
            : null;
        var updated = item.UpdatedUtc > DateTimeOffset.UnixEpoch
            ? T(Keys.WorkshopUpdated, item.UpdatedUtc.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture))
            : null;
        var size = item.FileSizeBytes is > 0
            ? T(Keys.WorkshopSize, ModSizeText.Format(item.FileSizeBytes))
            : null;

        return new WorkshopRowViewModel(
            WorkshopSource.Steam,
            item.PublishedFileId,
            item.Title,
            author,
            rating,
            subscriptions,
            updated,
            size,
            item.PreviewUrl,
            thumbnailSize,
            SteamWorkshop.GetBrowserAddress(item.PublishedFileId) ?? string.Empty,
            SteamWorkshop.GetSteamAddress(item.PublishedFileId),
            item,
            null);
    }

    /// <summary>从 PDX Mods 列表条目建一行。</summary>
    /// <param name="summary">PDX Mods 条目。</param>
    /// <param name="thumbnailSize">封面边长（像素）。</param>
    public static WorkshopRowViewModel FromPdx(PdxModsSummary summary, int thumbnailSize)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var author = string.IsNullOrWhiteSpace(summary.Author)
            ? string.Empty
            : T(Keys.WorkshopAuthor, summary.Author);
        var rating = summary.RatingsTotal > 0
            ? T(
                Keys.WorkshopRating,
                summary.Rating.ToString("0.##", CultureInfo.InvariantCulture),
                summary.RatingsTotal)
            : null;
        var subscriptions = summary.Subscriptions > 0
            ? T(Keys.WorkshopSubscriptions, summary.Subscriptions.ToString("N0", CultureInfo.CurrentCulture))
            : null;
        var updated = summary.UpdatedOn is { } updatedOn
            ? T(Keys.WorkshopUpdated, updatedOn.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture))
            : null;

        return new WorkshopRowViewModel(
            WorkshopSource.Pdx,
            summary.ModId,
            summary.DisplayName,
            author,
            rating,
            subscriptions,
            updated,
            null,
            summary.CoverUrl,
            thumbnailSize,
            "https://mods.paradoxplaza.com/mods/" + summary.ModId,
            null,
            null,
            summary);
    }

    /// <summary>
    /// 从缓存条目重建一行（断网/查询失败时的回退）。
    /// 缓存里只有列表级元数据，所以没有 <see cref="SteamItem"/> / <see cref="PdxSummary"/>，
    /// 打开详情时走一次真实请求；封面文件名命中后由调用方直接绑本地路径。
    /// </summary>
    /// <param name="entry">缓存条目。</param>
    /// <param name="thumbnailSize">封面边长（像素）。</param>
    public static WorkshopRowViewModel FromCache(WorkshopInfoCacheEntry entry, int thumbnailSize)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var isSteam = string.Equals(entry.Source, "steam", StringComparison.OrdinalIgnoreCase);
        var source = isSteam ? WorkshopSource.Steam : WorkshopSource.Pdx;
        var author = string.IsNullOrWhiteSpace(entry.Author)
            ? string.Empty
            : T(Keys.WorkshopAuthor, entry.Author);
        var rating = entry.RatingsTotal is > 0 && entry.Rating is > 0
            ? T(
                Keys.WorkshopRating,
                entry.Rating.Value.ToString("0.##", CultureInfo.InvariantCulture),
                entry.RatingsTotal.Value)
            : null;
        var subscriptions = entry.Subscriptions is > 0
            ? T(Keys.WorkshopSubscriptions, entry.Subscriptions.Value.ToString("N0", CultureInfo.CurrentCulture))
            : null;
        var updated = entry.UpdatedUtc is { } updatedUtc && updatedUtc > DateTimeOffset.UnixEpoch
            ? T(Keys.WorkshopUpdated, updatedUtc.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture))
            : null;
        var size = entry.SizeBytes is > 0
            ? T(Keys.WorkshopSize, ModSizeText.Format(entry.SizeBytes))
            : null;

        return new WorkshopRowViewModel(
            source,
            entry.Id,
            entry.Name ?? string.Empty,
            author,
            rating,
            subscriptions,
            updated,
            size,
            entry.CoverUrl,
            thumbnailSize,
            isSteam
                ? SteamWorkshop.GetBrowserAddress(entry.Id) ?? string.Empty
                : "https://mods.paradoxplaza.com/mods/" + entry.Id,
            isSteam ? SteamWorkshop.GetSteamAddress(entry.Id) : null,
            null,
            null);
    }

    partial void OnCoverPathChanged(string? value) => OnPropertyChanged(nameof(HasCover));
}
