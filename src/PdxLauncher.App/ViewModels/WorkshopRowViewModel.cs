namespace PdxLauncher.App.ViewModels;

using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Mods;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Steam;

/// <summary>
/// 创意工坊列表里的一行。两个平台共用同一套界面，
/// 所以这里把 <see cref="SteamWorkshopItem"/> 与 <see cref="PdxModsSummary"/> 归一成同一组只读文案。
/// 本类不含任何 Microsoft.UI.Xaml 类型；只有封面路径是可观察的 ——
/// 封面要下载到本地，补到图后就地刷新这一行。
/// </summary>
public sealed partial class WorkshopRowViewModel : LocalizedObservableObject
{
    /// <summary>Paradox Mods 网页端的条目地址前缀（与 <c>OnlineHosts.ModsWebHost</c> 同一主机）。</summary>
    private const string PdxWebPrefix = "https://mods.paradoxplaza.com/mods/";

    /// <summary>封面本地缓存路径；还没下载好时为 null。</summary>
    [ObservableProperty]
    private string? _coverPath;

    /// <summary>译名；没有启用翻译、翻译失败或原文已是目标语言时为 null。</summary>
    [ObservableProperty]
    private string? _translatedName;

    /// <summary>这一行的单项订阅请求是否在飞（合集内容列表用）。</summary>
    [ObservableProperty]
    private bool _isSteamSubscribing;

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
        PdxModsSummary? pdxSummary,
        bool isCollection = false,
        string? shortDescription = null,
        bool steamActionAvailable = false)
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
        IsCollection = isCollection;
        ShortDescription = shortDescription ?? string.Empty;
        IsSteamActionAvailable = steamActionAvailable;
    }

    /// <summary>这行是不是 Steam 合集（列表页按区段判断，详情按 <c>file_type=2</c>）。</summary>
    public bool IsCollection { get; }

    /// <summary>是否显示「合集」徽标。</summary>
    public bool ShowCollectionBadge => IsCollection;

    /// <summary>「合集」徽标文案。</summary>
    public string CollectionBadgeText => T(Keys.WorkshopCollectionBadge);

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

    /// <summary>列表短描述；合集内容列表用来显示子项简介，普通行也可用。空串表示没有。</summary>
    public string ShortDescription { get; }

    /// <summary>是否有短描述。</summary>
    public bool HasShortDescription => !string.IsNullOrWhiteSpace(ShortDescription);

    /// <summary>合集子项是否允许本页直接改 Steam 订阅（后端可用且设置里开了写开关）。</summary>
    public bool IsSteamActionAvailable { get; }

    /// <summary>这一项在 Steam 侧是否已订阅（含已安装，安装通常意味着订阅过）。</summary>
    public bool IsSubscribed => SteamState?.IsSubscribed == true || SteamState?.IsInstalled == true;

    /// <summary>单项订阅按钮是否可点。</summary>
    public bool CanToggleSteamSubscription => IsSteamActionAvailable && !IsSteamSubscribing;

    /// <summary>单项订阅按钮文案：订阅 / 取消订阅 / 正在订阅…。</summary>
    public string SteamSubscribeActionText => IsSteamSubscribing
        ? T(Keys.WorkshopDetailSubscribing)
        : IsSubscribed
            ? T(Keys.WorkshopDetailUnsubscribe)
            : T(Keys.WorkshopDetailSubscribe);

    /// <summary>单项订阅按钮的自动化 Id。</summary>
    public string SteamSubscribeAutomationId => "workshopCollectionSubscribe_" + Id;

    /// <summary>副标题：作者 · 评分 · 订阅数 · 更新时间，空段自动省略，不留孤立分隔符。</summary>
    public string SubtitleText => string.Join(
        " · ",
        new[] { AuthorText, RatingText, SubscriptionsText, UpdatedText }
            .Where(static part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>封面原始地址（下载完之前 XAML 用占位图标）。</summary>
    public string? CoverUrl { get; }

    /// <summary>是否有可用封面。</summary>
    public bool HasCover => !string.IsNullOrWhiteSpace(CoverPath);

    /// <summary>是否有可显示的译名。</summary>
    public bool HasTranslatedName => !string.IsNullOrWhiteSpace(TranslatedName);

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

    /// <summary>
    /// 这一条在本机的 Steam 状态（订阅 / 已安装 / 下载中 / 需要更新 / 本地禁用）。
    /// 列表铺好后才由 <see cref="WorkshopViewModel"/> 用整库缓存补进来；PDX 行与读不到时保持 null。
    /// </summary>
    [ObservableProperty]
    private WorkshopInstallState? _steamState;

    /// <summary>是否要显示 Steam 状态徽标（与 Mod 库行同一套判定）。</summary>
    public bool ShowSteamStateBadge => SteamStateBadgeFormatter.IsVisible(SteamState);

    /// <summary>Steam 状态徽标文案；下载中带上百分比与字节数。</summary>
    public string SteamStateBadgeText => SteamStateBadgeFormatter.Build(SteamState);

    /// <summary>Steam 状态徽标的自动化 Id。</summary>
    public string SteamStateAutomationId => "workshopRowSteamState_" + Id;

    /// <summary>行的自动化 Id（每行唯一，供 UI 测试定位）。</summary>
    public string RowAutomationId => "workshopRow_" + (Source == WorkshopSource.Steam ? "steam_" : "pdx_") + Id;

    /// <summary>行内「用浏览器打开」按钮的自动化 Id。</summary>
    public string OpenBrowserAutomationId => "workshopOpenBrowser_" + Id;

    /// <summary>行内「在 Steam 中打开」按钮的自动化 Id。</summary>
    public string OpenSteamAutomationId => "workshopOpenSteam_" + Id;

    /// <summary>从 Steam 列表条目建一行。</summary>
    /// <param name="item">Steam 条目。</param>
    /// <param name="thumbnailSize">封面边长（像素）。</param>
    /// <param name="isCollection">是否是合集列表里的行（SSR 不一定给 <c>file_type</c>，按浏览区段兜底）。</param>
    public static WorkshopRowViewModel FromSteam(
        SteamWorkshopItem item,
        int thumbnailSize,
        bool isCollection = false)
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
            null,
            isCollection || item.IsCollection,
            item.ShortDescription);
    }

    /// <summary>
    /// 从 <c>GetPublishedFileDetails</c> 的详情建一行（合集子项列表用）。
    /// 详情没有评分/当前订阅数，副标题就只显示作者与体积；这些行只用于展示与跳转。
    /// </summary>
    /// <param name="detail">子项详情。</param>
    /// <param name="thumbnailSize">封面边长（像素）。</param>
    /// <param name="steamActionAvailable">是否允许本页直接改这一项的 Steam 订阅。</param>
    public static WorkshopRowViewModel FromSteamDetail(
        SteamWorkshopItemDetail detail,
        int thumbnailSize,
        bool steamActionAvailable = false)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var author = string.IsNullOrWhiteSpace(detail.CreatorSteamId)
            ? string.Empty
            : T(Keys.WorkshopAuthor, detail.CreatorSteamId);
        var updated = detail.UpdatedUtc > DateTimeOffset.UnixEpoch
            ? T(Keys.WorkshopUpdated, detail.UpdatedUtc.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture))
            : null;
        var size = detail.FileSizeBytes > 0
            ? T(Keys.WorkshopSize, ModSizeText.Format(detail.FileSizeBytes))
            : null;

        return new WorkshopRowViewModel(
            WorkshopSource.Steam,
            detail.PublishedFileId,
            detail.Title,
            author,
            null,
            null,
            updated,
            size,
            detail.PreviewUrl,
            thumbnailSize,
            SteamWorkshop.GetBrowserAddress(detail.PublishedFileId) ?? string.Empty,
            SteamWorkshop.GetSteamAddress(detail.PublishedFileId),
            null,
            null,
            detail.IsCollection,
            null,
            steamActionAvailable);
    }

    /// <summary>
    /// 从合集物品页解析出的子项建一行。作者只有显示名（页面不给 SteamID），也没有评分/体积，
    /// 所以副标题只显示作者；这些行用于合集内容列表与跳转子项详情。
    /// </summary>
    /// <param name="item">物品页上的子项。</param>
    /// <param name="thumbnailSize">封面边长（像素）。</param>
    /// <param name="steamActionAvailable">是否允许本页直接改这一项的 Steam 订阅。</param>
    public static WorkshopRowViewModel FromSteamCollectionItem(
        SteamWorkshopCollectionItemPage item,
        int thumbnailSize,
        bool steamActionAvailable = false)
    {
        ArgumentNullException.ThrowIfNull(item);

        var author = string.IsNullOrWhiteSpace(item.AuthorName)
            ? string.Empty
            : T(Keys.WorkshopAuthor, item.AuthorName!);

        return new WorkshopRowViewModel(
            WorkshopSource.Steam,
            item.PublishedFileId,
            item.Title,
            author,
            null,
            null,
            null,
            null,
            item.PreviewUrl,
            thumbnailSize,
            SteamWorkshop.GetBrowserAddress(item.PublishedFileId) ?? string.Empty,
            SteamWorkshop.GetSteamAddress(item.PublishedFileId),
            null,
            null,
            false,
            item.ShortDescription,
            steamActionAvailable);
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
            PdxWebPrefix + summary.ModId,
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
    /// <param name="isCollection">是否是合集列表里的行（页缓存按区段隔离，可直接沿用）。</param>
    public static WorkshopRowViewModel FromCache(
        WorkshopInfoCacheEntry entry,
        int thumbnailSize,
        bool isCollection = false)
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
                : PdxWebPrefix + entry.Id,
            isSteam ? SteamWorkshop.GetSteamAddress(entry.Id) : null,
            null,
            null,
            isSteam && isCollection);
    }

    /// <summary>
    /// 从 Mod 库的一行建详情行：那边只有 (平台, 编号) 与本地记录，没有列表接口那种对象。
    /// <para>
    /// 缺的元数据由 <paramref name="cached"/>（工坊信息缓存里可能有，也可能没有）补，补不到的段落
    /// 就留空 —— 副标题是「非空段拼接」，空段自动省略，不会留孤立分隔符。详情正文本来就是一次真实请求，
    /// 评分/标签/更新时间到了详情页再填；Steam 侧连作者显示名都能从物品页拿到。
    /// </para>
    /// </summary>
    /// <param name="reference">已经解析过的工坊引用（平台 + 编号，见 <see cref="ModWorkshopRef.Resolve"/>）。</param>
    /// <param name="name">mod 名称（取本地记录里的显示名）。</param>
    /// <param name="sizeBytes">本地安装体积；工坊那边显示的是文件大小，量纲一致所以直接拿来用。</param>
    /// <param name="localThumbnailPath">本地已经解码/下载到的缩略图，直接当封面，省一次网络。</param>
    /// <param name="cached">工坊信息缓存里的同名条目；没缓存过时为 null。</param>
    /// <param name="thumbnailSize">封面边长（像素）。</param>
    public static WorkshopRowViewModel FromLibrary(
        ModWorkshopRef reference,
        string name,
        long? sizeBytes,
        string? localThumbnailPath,
        WorkshopInfoCacheEntry? cached,
        int thumbnailSize)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!reference.CanOpen)
        {
            throw new ArgumentException("这一行没有可用的工坊编号，建不出详情行", nameof(reference));
        }

        var isSteam = reference.Source == WorkshopSource.Steam;
        var authorText = string.IsNullOrWhiteSpace(cached?.Author)
            ? string.Empty
            : T(Keys.WorkshopAuthor, cached!.Author);
        var rating = cached is { RatingsTotal: > 0 } && cached.Rating is > 0
            ? T(
                Keys.WorkshopRating,
                cached.Rating.Value.ToString("0.##", CultureInfo.InvariantCulture),
                cached.RatingsTotal!.Value)
            : null;
        var subscriptions = cached?.Subscriptions is > 0
            ? T(Keys.WorkshopSubscriptions, cached!.Subscriptions!.Value.ToString("N0", CultureInfo.CurrentCulture))
            : null;
        var updated = cached is { UpdatedUtc: { } updatedUtc } && updatedUtc > DateTimeOffset.UnixEpoch
            ? T(Keys.WorkshopUpdated, updatedUtc.ToLocalTime().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture))
            : null;
        var size = sizeBytes > 0
            ? T(Keys.WorkshopSize, ModSizeText.Format(sizeBytes))
            : null;

        // 封面地址只取工坊缓存里的（和创意工坊列表同一张图）。本地缩略图是文件路径，不能当 URL 用：
        // 详情正文的配图兜底会把它当地址去下载，所以它只走 CoverPath 这条「已经是本地文件」的路。
        var coverUrl = string.IsNullOrWhiteSpace(cached?.CoverUrl) ? null : cached!.CoverUrl;

        var row = new WorkshopRowViewModel(
            reference.Source,
            reference.Id,
            name,
            authorText,
            rating,
            subscriptions,
            updated,
            size,
            coverUrl,
            thumbnailSize,
            isSteam
                ? SteamWorkshop.GetBrowserAddress(reference.Id) ?? string.Empty
                : PdxWebPrefix + reference.Id,
            isSteam ? SteamWorkshop.GetSteamAddress(reference.Id) : null,
            null,
            null);

        // 本地缩略图已经在 Mod 库上显示出来了，直接当封面，省一次下载（详情页只在没有封面时才补图）。
        if (!string.IsNullOrWhiteSpace(localThumbnailPath))
        {
            row.CoverPath = localThumbnailPath;
        }

        return row;
    }

    partial void OnCoverPathChanged(string? value) => OnPropertyChanged(nameof(HasCover));

    partial void OnTranslatedNameChanged(string? value) => OnPropertyChanged(nameof(HasTranslatedName));

    partial void OnSteamStateChanged(WorkshopInstallState? value)
    {
        OnPropertyChanged(nameof(ShowSteamStateBadge));
        OnPropertyChanged(nameof(SteamStateBadgeText));
        OnPropertyChanged(nameof(IsSubscribed));
        OnPropertyChanged(nameof(SteamSubscribeActionText));
    }

    partial void OnIsSteamSubscribingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggleSteamSubscription));
        OnPropertyChanged(nameof(SteamSubscribeActionText));
    }
}
