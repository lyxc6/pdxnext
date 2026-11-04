namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Steam;
using PdxLauncher.Core.Text;

/// <summary>
/// 「创意工坊 mod 详情」子页的视图模型：先把列表行已有的元数据摆上去（名字/作者/评分立刻可见），
/// 再异步补详情正文、截图、外链、依赖与评论。原来这些内容挤在一个 MaxHeight=560 的 ContentDialog 里，
/// 长描述根本看不全，现在改成整页滚动 + 富文本渲染。
/// <para>
/// 两种来源的字段差异在这里抹平：Steam 有作者主页与评论区，PDX 有更新日志、截图、外链和前置依赖。
/// 页面只认这里的属性，不判断来源。
/// </para>
/// <para>本类不引用任何 Microsoft.UI.Xaml 类型。</para>
/// </summary>
public sealed partial class WorkshopDetailViewModel : LocalizedObservableObject
{
    /// <summary>截图并发下载上限：详情页没有列表那种 6 张封面的压力，2 张够用又不抢带宽。</summary>
    private const int MaxConcurrentScreenshots = 2;

    /// <summary>评论每页条数（Steam 服务端上限 50，这里取小一点让翻页有感知）。</summary>
    public const int CommentsPageSize = 15;

    private readonly WorkshopDetailSource _source;
    private readonly WorkshopImageCache _images;
    private readonly WorkshopSubscriptionService _subscription;
    private readonly IWorkshopSubscribeBackend? _steamBackend;
    private readonly WorkshopSubscriptionReader? _steamReader;
    private readonly IAppLogger _logger;

    /// <summary>
    /// 截图下载进度：界面上的「正在下载截图…」只看它。
    /// <para>
    /// 不能看「整批下载任务是否结束」—— Steam 路径的 <c>Task.WhenAll</c> 里还挂着正文配图，
    /// 那批没下完会让提示在截图全都就位之后继续转。
    /// </para>
    /// </summary>
    private readonly ScreenshotLoadTracker _galleryTracker = new();

    /// <summary>Steam 评论翻页要用作者 SteamID（接口路径里的一部分）。</summary>
    private string? _steamCreatorId;

    /// <summary>
    /// 这一页的「返回」该回哪儿（导航发起方给的 Tag）；null 表示按默认的创意工坊列表处理。
    /// </summary>
    private string? _returnTag;

    /// <summary>正文配图：URL → 本地文件。与顶部图片墙分开，避免重复下载与互相覆盖。</summary>
    private readonly Dictionary<string, string> _inlineImages = new(StringComparer.Ordinal);

    /// <summary>构造。</summary>
    /// <param name="source">详情取数。</param>
    /// <param name="images">图片缓存（封面/截图落地）。</param>
    /// <param name="subscription">PDX 订阅服务。</param>
    /// <param name="logger">日志。</param>
    /// <param name="steamBackend">Steam 直连订阅后端；null 或不可用表示只能打开客户端。</param>
    /// <param name="steamReader">acf 只读查询（后端不可用时用它显示「已订阅/已安装」）。</param>
    public WorkshopDetailViewModel(
        WorkshopDetailSource source,
        WorkshopImageCache images,
        WorkshopSubscriptionService subscription,
        IAppLogger logger,
        IWorkshopSubscribeBackend? steamBackend = null,
        WorkshopSubscriptionReader? steamReader = null)
    {
        _source = source;
        _images = images;
        _subscription = subscription;
        _logger = logger;
        _steamBackend = steamBackend;
        _steamReader = steamReader;
        IsSteamDirectAvailable = steamBackend?.IsAvailable ?? false;
        SteamUnavailableReason = steamBackend?.UnavailableReason ?? string.Empty;
    }

    /// <summary>当前展示的 mod 行；null 表示还没加载。</summary>
    public WorkshopRowViewModel? Row { get; private set; }

    /// <summary>标题（mod 名称）。</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>来源徽标文案（Steam 创意工坊 / Paradox Mods）。</summary>
    [ObservableProperty]
    private string _sourceLabel = string.Empty;

    /// <summary>作者一行（含「作者」前缀）。</summary>
    [ObservableProperty]
    private string _authorText = string.Empty;

    /// <summary>作者主页地址；null 表示拿不到（PDX 只有显示名）。</summary>
    [ObservableProperty]
    private string? _authorProfileUrl;

    /// <summary>评分 / 订阅 / 收藏 / 浏览 / 体积 / 更新时间拼成的一行。</summary>
    [ObservableProperty]
    private string _metaText = string.Empty;

    /// <summary>发布时间（取不到时为空串）。</summary>
    [ObservableProperty]
    private string _createdText = string.Empty;

    /// <summary>简介纯文本（无障碍名与兜底显示用）。</summary>
    [ObservableProperty]
    private string _descriptionText = string.Empty;

    /// <summary>简介富文本。</summary>
    [ObservableProperty]
    private MarkdownDocument? _descriptionDocument;

    /// <summary>更新日志富文本（仅 PDX 有）。</summary>
    [ObservableProperty]
    private MarkdownDocument? _changelogDocument;

    /// <summary>简介译文富文本；没有启用翻译或翻译失败时为 null。</summary>
    [ObservableProperty]
    private MarkdownDocument? _translatedDescriptionDocument;

    /// <summary>简介译文纯文本（无障碍名称用）。</summary>
    [ObservableProperty]
    private string _translatedDescriptionText = string.Empty;

    /// <summary>更新日志译文富文本；没有启用翻译或翻译失败时为 null。</summary>
    [ObservableProperty]
    private MarkdownDocument? _translatedChangelogDocument;

    /// <summary>简介译文是否还在翻：界面在译文将来出现的位置显示「正在翻译简介…」。</summary>
    [ObservableProperty]
    private bool _isTranslatingDescription;

    /// <summary>更新日志译文是否还在翻。</summary>
    [ObservableProperty]
    private bool _isTranslatingChangelog;

    /// <summary>评论译文是否还在翻（评论是逐条翻的，整段期间都算）。</summary>
    [ObservableProperty]
    private bool _isTranslatingComments;

    /// <summary>标签拼成的单行文本。</summary>
    [ObservableProperty]
    private string _tagsText = string.Empty;

    /// <summary>依赖摘要（「共 3 个前置 mod，1 个未启用」）。</summary>
    [ObservableProperty]
    private string _dependencySummary = string.Empty;

    /// <summary>评论摘要（「共 136 条讨论，显示最新 10 条」）。</summary>
    [ObservableProperty]
    private string _commentsSummary = string.Empty;

    /// <summary>封面本地路径。</summary>
    [ObservableProperty]
    private string? _coverPath;

    /// <summary>错误提示文案。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>正文来自缓存（断网兜底）时的提示；空串表示没有这回事。</summary>
    [ObservableProperty]
    private string _cachedHint = string.Empty;

    /// <summary>翻译没出结果时的一句页面级提示；三个范围各提示一次，空串表示没这回事。</summary>
    [ObservableProperty]
    private string _translationNotice = string.Empty;

    /// <summary>已经提示过失败的范围（简介 / 更新日志 / 评论），避免重复刷屏。</summary>
    private readonly HashSet<string> _translateFailureScopes = new(StringComparer.Ordinal);

    /// <summary>是否正在读取详情。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>是否已成功读到详情。</summary>
    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>正文已就位、只剩截图还在下载。</summary>
    [ObservableProperty]
    private bool _isLoadingScreenshots;

    /// <summary>评论区还在抓（Steam 物品页比详情接口慢）。</summary>
    [ObservableProperty]
    private bool _isLoadingComments;

    /// <summary>是否读取失败（此时展示 <see cref="ErrorText"/> 与「重试」）。</summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>当前账号是否已订阅（仅 PDX 有意义）。</summary>
    [ObservableProperty]
    private bool _isInstalled;

    /// <summary>订阅状态是否已从平台查到（false 时按钮仍可用，只是不敢断言「未订阅」）。</summary>
    [ObservableProperty]
    private bool _isSubscriptionKnown;

    /// <summary>是否已登录 Paradox 账号（登录才能订阅）。</summary>
    [ObservableProperty]
    private bool _isSignedIn;

    /// <summary>订阅/退订请求正在飞。</summary>
    [ObservableProperty]
    private bool _isSubscribing;

    /// <summary>评论总数（服务端 total_count）。</summary>
    [ObservableProperty]
    private int _commentsTotal;

    /// <summary>当前评论页（1 基）。</summary>
    [ObservableProperty]
    private int _commentsPage = 1;

    /// <summary>当前显示的这批评论在整串评论里的起始偏移（0 基）。</summary>
    /// <remarks>
    /// 首屏不一定从 0 开始按整页对齐：物品页内嵌的首屏是 10 条，而翻页接口一页 15 条。
    /// 翻页按「已显示到哪儿」往后接，才不会重复或漏掉中间那几条。
    /// </remarks>
    private int _commentsShownStart;

    /// <summary>当前显示的这批评论条数。</summary>
    private int _commentsShownCount;

    /// <summary>评论区的一句状态说明（翻页失败、拿不到作者、没有可显示的评论），空串表示没有。</summary>
    [ObservableProperty]
    private string _commentsStatusText = string.Empty;

    /// <summary>订阅操作的即时反馈（成功/失败文案），空串表示没有。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Steam 侧是否已订阅（来自 Steamworks 实时状态或本地 acf）。</summary>
    [ObservableProperty]
    private bool _isSteamSubscribed;

    /// <summary>Steam 订阅状态是否已确认（读不到 acf、后端不可用时保持 false）。</summary>
    [ObservableProperty]
    private bool _isSteamStateKnown;

    /// <summary>Steam 订阅请求正在飞（接口要等 Steam 确认，可能好几秒）。</summary>
    [ObservableProperty]
    private bool _isSteamSubscribing;

    /// <summary>Steam 订阅不可用的原因（dll 没找到 / 设置里没开开关），用于按钮下方提示。</summary>
    [ObservableProperty]
    private string _steamUnavailableReason = string.Empty;

    /// <summary>Steam 订阅按钮是否真的直连（false 表示只能打开客户端）。</summary>
    [ObservableProperty]
    private bool _isSteamDirectAvailable;

    /// <summary>Steam 那边已安装但内容比工坊新版本旧（Steam 会自己更新，这里只提示）。</summary>
    [ObservableProperty]
    private bool _isSteamNeedsUpdate;

    /// <summary>Steam 下载进度文案（「下载中 42%」），没在下就空串。</summary>
    [ObservableProperty]
    private string _steamDownloadProgressText = string.Empty;

    /// <summary>图片墙（PDX 截图 + 描述里内嵌的图）。</summary>
    public ObservableCollection<WorkshopScreenshotViewModel> Screenshots { get; } = [];

    /// <summary>外链列表（含论坛帖）。</summary>
    public ObservableCollection<WorkshopLinkViewModel> Links { get; } = [];

    /// <summary>前置依赖。</summary>
    public ObservableCollection<WorkshopDependencyViewModel> Dependencies { get; } = [];

    /// <summary>评论区。</summary>
    public ObservableCollection<WorkshopCommentViewModel> Comments { get; } = [];

    /// <summary>封面是否就绪。</summary>
    public bool HasCover => !string.IsNullOrWhiteSpace(CoverPath);

    /// <summary>简介是否有内容。</summary>
    public bool HasDescription => DescriptionDocument is { IsEmpty: false } || !string.IsNullOrWhiteSpace(DescriptionText);

    /// <summary>简介是否为空（读到详情后才提示「作者没有填写简介」）。</summary>
    public bool ShowNoDescription => IsLoaded && !HasError && !HasDescription;

    /// <summary>更新日志是否有内容。</summary>
    public bool HasChangelog => ChangelogDocument is { IsEmpty: false };

    /// <summary>是否有可显示的简介译文。</summary>
    public bool HasTranslatedDescription => TranslatedDescriptionDocument is { IsEmpty: false };

    /// <summary>是否有可显示的更新日志译文。</summary>
    public bool HasTranslatedChangelog => TranslatedChangelogDocument is { IsEmpty: false };

    /// <summary>是否在等简介译文：等的时候在译文将来出现的位置显示环形进度 + 「正在翻译简介…」。</summary>
    public bool ShowTranslatingDescription => IsTranslatingDescription && !HasTranslatedDescription;

    /// <summary>是否在等更新日志译文。</summary>
    public bool ShowTranslatingChangelog => IsTranslatingChangelog && !HasTranslatedChangelog;

    /// <summary>是否在等评论译文（评论逐条翻，整段期间都显示）。</summary>
    public bool ShowTranslatingComments => IsTranslatingComments;

    /// <summary>是否有标签。</summary>
    public bool HasTags => !string.IsNullOrWhiteSpace(TagsText);

    /// <summary>是否有图片（截图或描述配图）。</summary>
    public bool HasScreenshots => Screenshots.Count > 0;

    /// <summary>
    /// 是否显示顶部图片区。
    /// <para>
    /// PDX 是详情接口给的真实截图；Steam 只有一张大图预览（简介里的配图留在正文，不属于这里）。
    /// </para>
    /// </summary>
    public bool ShowGalleryCard => HasScreenshots;

    /// <summary>正文配图下载完成（交给页面刷新 MarkdownView 里的图片）。</summary>
    public event EventHandler? GalleryImageReady;

    /// <summary>图片 URL → 本地缓存路径（未下载完成返回 null），供正文内联图片使用。</summary>
    public string? ResolveImagePath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (_inlineImages.TryGetValue(url, out var inlinePath) && !string.IsNullOrWhiteSpace(inlinePath))
        {
            return inlinePath;
        }

        foreach (var shot in Screenshots)
        {
            if (shot.HasPath && string.Equals(shot.Url, url, StringComparison.Ordinal))
            {
                return shot.Path;
            }
        }

        return null;
    }

    /// <summary>是否有外链。</summary>
    public bool HasLinks => Links.Count > 0;

    /// <summary>是否有前置依赖。</summary>
    public bool HasDependencies => Dependencies.Count > 0;

    /// <summary>是否有依赖摘要。</summary>
    public bool HasDependencySummary => !string.IsNullOrWhiteSpace(DependencySummary);

    /// <summary>是否有评论。</summary>
    public bool HasComments => Comments.Count > 0;

    /// <summary>读到详情后仍然没有评论（显示「暂无讨论」）。</summary>
    public bool ShowNoComments => IsLoaded && !HasError && !IsLoadingComments && !HasComments && ShowCommentsSection;

    /// <summary>是否显示「讨论」卡（Steam 才有评论区）。</summary>
    public bool ShowCommentsSection => Row?.Source == WorkshopSource.Steam;

    /// <summary>是否有评论摘要。</summary>
    public bool HasCommentsSummary => !string.IsNullOrWhiteSpace(CommentsSummary);

    /// <summary>评论区的一句状态说明（翻页失败、拿不到作者、没有可显示的评论）。</summary>
    public bool HasCommentsStatusText => !string.IsNullOrWhiteSpace(CommentsStatusText);

    /// <summary>是否有发布时间。</summary>
    public bool HasCreated => !string.IsNullOrWhiteSpace(CreatedText);

    /// <summary>是否有缓存兜底提示。</summary>
    public bool HasCachedHint => !string.IsNullOrWhiteSpace(CachedHint);

    /// <summary>是否有「翻译没出结果」的页面级提示。</summary>
    public bool HasTranslationNotice => !string.IsNullOrWhiteSpace(TranslationNotice);

    /// <summary>是否显示「已订阅」徽标。</summary>
    public bool ShowInstalled => IsInstalled;

    /// <summary>作者主页是否可用（决定作者那一行能不能点）。</summary>
    public bool HasAuthorProfile => !string.IsNullOrWhiteSpace(AuthorProfileUrl);

    /// <summary>浏览器地址。</summary>
    public string WebUrl => Row?.WebUrl ?? string.Empty;

    /// <summary>Steam 客户端协议地址（仅 Steam 来源）。</summary>
    public string? SteamUrl => Row?.SteamUrl;

    /// <summary>是否可「在 Steam 中打开」。</summary>
    public bool CanOpenSteam => Row?.CanOpenSteam ?? false;

    /// <summary>是否有可打开的浏览器地址（决定页头按钮是否可用）。</summary>
    public bool HasWebUrl => !string.IsNullOrWhiteSpace(WebUrl);

    /// <summary>
    /// 页头「返回」按钮的文案：从创意工坊列表进来是「返回创意工坊」，
    /// 从 Mod 库进来是「返回 Mod 库」—— 按钮永远只是「回上一页」，
    /// 但用户需要知道这一下会回到哪儿。
    /// </summary>
    public string BackText => string.Equals(_returnTag, NavigationService.ModLibraryTag, StringComparison.OrdinalIgnoreCase)
        ? T(Keys.WorkshopDetailBackLibrary)
        : T(Keys.WorkshopDetailBack);

    /// <summary>
    /// 评论总数超过一页、而且拿到了作者 SteamID 才显示分页控件。
    /// <para>拿不到作者 id 时翻页接口根本没法请求，宁可不显示，也不要摆一个点了没反应的按钮。</para>
    /// </summary>
    public bool HasCommentsPaging =>
        ShowCommentsSection && CommentsTotal > CommentsPageSize && !string.IsNullOrWhiteSpace(_steamCreatorId);

    /// <summary>评论总页数（至少 1）。</summary>
    public int CommentsPageCount =>
        CommentsTotal <= 0 ? 1 : (CommentsTotal + CommentsPageSize - 1) / CommentsPageSize;

    /// <summary>「第 3 / 10 页 · 共 136 条」文案；没有评论时为空串。</summary>
    public string CommentsPageText => CommentsTotal <= 0
        ? string.Empty
        : T(Keys.WorkshopCommentsPage, CommentsPage, CommentsPageCount, CommentsTotal);

    /// <summary>是否有评论页码文案。</summary>
    public bool HasCommentsPageText => !string.IsNullOrWhiteSpace(CommentsPageText);

    /// <summary>能不能翻上一页。</summary>
    public bool CanCommentsPrev => HasCommentsPaging && _commentsShownStart > 0 && !IsLoadingComments;

    /// <summary>
    /// 能不能翻下一页（加载中禁点，避免连点重复请求）。
    /// <para>判据是「已显示到哪儿」而不是页号：首屏内嵌 10 条、接口一页 15 条，两者不对齐。</para>
    /// </summary>
    public bool CanCommentsNext =>
        HasCommentsPaging && _commentsShownStart + _commentsShownCount < CommentsTotal && !IsLoadingComments;

    /// <summary>这张卡是不是 PDX 的订阅卡（Steam 走「在 Steam 中订阅」）。</summary>
    public bool CanSubscribe => Row?.Source == WorkshopSource.Pdx;

    /// <summary>订阅按钮是否可点（已登录且当前没有请求在飞）。</summary>
    public bool CanToggleSubscription => CanSubscribe && IsSignedIn && !IsSubscribing;

    /// <summary>订阅按钮文案：订阅 / 订阅中… / 取消订阅。</summary>
    public string SubscribeActionText => IsSubscribing
        ? T(Keys.WorkshopDetailSubscribing)
        : IsInstalled
            ? T(Keys.WorkshopDetailUnsubscribe)
            : T(Keys.WorkshopDetailSubscribe);

    /// <summary>订阅框下方的说明（未登录提示 / 播放集提示）。</summary>
    public string SubscribeHint => !CanSubscribe
        ? string.Empty
        : IsSignedIn
            ? T(Keys.WorkshopDetailSubscribePlaysetHint)
            : T(Keys.WorkshopDetailSubscribeSignIn);

    /// <summary>是否有订阅说明文案。</summary>
    public bool HasSubscribeHint => !string.IsNullOrWhiteSpace(SubscribeHint);

    /// <summary>是否有订阅操作反馈文案。</summary>
    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    /// <summary>是否显示订阅卡（两种来源都显示：PDX 走平台订阅，Steam 走客户端）。</summary>
    public bool ShowSubscribeCard => Row is not null;

    /// <summary>是否显示 PDX 的订阅按钮（Steam 显示「在 Steam 中订阅」）。</summary>
    public bool ShowPdxSubscribeButton => CanSubscribe;

    /// <summary>是否显示 Steam 的订阅按钮（跳客户端）。</summary>
    public bool ShowSteamSubscribeButton => Row?.Source == WorkshopSource.Steam;

    /// <summary>订阅状态是否已确认（未确认时不显示「未订阅」这种断言文案）。</summary>
    public bool ShowSubscriptionState => CanSubscribe && IsSubscriptionKnown;

    /// <summary>Steam 订阅按钮文案：订阅 / 订阅中… / 取消订阅。</summary>
    public string SteamSubscribeActionText => IsSteamSubscribing
        ? T(Keys.WorkshopDetailSubscribing)
        : IsSteamSubscribed
            ? T(Keys.WorkshopDetailUnsubscribe)
            : T(Keys.WorkshopDetailSubscribe);

    /// <summary>Steam 订阅按钮是否可点：直连可用、写动作开关开着、当前没有请求在飞。</summary>
    public bool CanToggleSteamSubscription =>
        ShowSteamSubscribeButton && IsSteamDirectAvailable && CanWriteSteam && !IsSteamSubscribing;

    /// <summary>
    /// 是否允许写 Steam（订阅/退订）。只读状态查询默认开着，
    /// 真正改订阅要另一个开关，两个是分开的。
    /// </summary>
    public bool CanWriteSteam => AppServices.Settings.SteamWorkshopSubscribeEnabled;

    /// <summary>Steam 订阅框的说明（直连开没开 / 只读还是可写）。</summary>
    public string SteamSubscribeHint => !ShowSteamSubscribeButton
        ? string.Empty
        : !IsSteamDirectAvailable
            ? T(Keys.WorkshopDetailSteamSubscribeHintOff)
            : CanWriteSteam
                ? T(Keys.WorkshopDetailSteamSubscribeHintOn)
                : T(Keys.WorkshopDetailSteamReadOnlyHint);

    /// <summary>是否有 Steam 订阅说明。</summary>
    public bool HasSteamSubscribeHint => !string.IsNullOrWhiteSpace(SteamSubscribeHint);

    /// <summary>Steam 直连不可用的具体原因（找不到 dll 等），有才显示。</summary>
    public bool HasSteamUnavailableReason =>
        ShowSteamSubscribeButton && !string.IsNullOrWhiteSpace(SteamUnavailableReason);

    /// <summary>只在「已确认且确实订阅着」时显示「已订阅」徽标，别把「未订阅」也标成已订阅。</summary>
    public bool ShowSteamSubscribeState => ShowSteamSubscribeButton && IsSteamStateKnown && IsSteamSubscribed;

    /// <summary>Steam 侧内容落后于工坊版本（Steam 会自己更新，这里只是提示）。</summary>
    public bool ShowSteamUpdateHint => ShowSteamSubscribeButton && IsSteamNeedsUpdate;

    /// <summary>Steam 正在下载这条（显示进度）。</summary>
    public bool HasSteamDownloadProgress => ShowSteamSubscribeButton && !string.IsNullOrWhiteSpace(SteamDownloadProgressText);

    /// <summary>加载指定行；重复调用会先清空上一次的内容。</summary>
    /// <param name="row">创意工坊列表行。</param>
    /// <param name="cancellationToken">取消令牌（页面离开时取消）。</param>
    /// <param name="returnTag">
    /// 「返回」按钮要回到的页面 Tag（null 表示创意工坊列表）。详情页能被多个页面复用，
    /// 由发起方说回来处，正文与文案都按它调整。
    /// </param>
    public async Task LoadAsync(
        WorkshopRowViewModel row,
        CancellationToken cancellationToken,
        string? returnTag = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        Row = row;
        _returnTag = returnTag;
        ResetForLoad(row);
        NotifyStateChanged();

        try
        {
            await LoadCoverAsync(row, cancellationToken).ConfigureAwait(true);

            if (row.Source == WorkshopSource.Steam)
            {
                await ApplySteamAsync(row, cancellationToken).ConfigureAwait(true);
            }
            else
            {
                await ApplyPdxAsync(row, cancellationToken).ConfigureAwait(true);
            }

            IsLoaded = !HasError;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("创意工坊详情加载异常: " + row.Id, ex);
            Fail(DescribeLoadFailure(ex));
        }
        finally
        {
            IsLoading = false;
            IsLoadingScreenshots = false;
            IsLoadingComments = false;
            NotifyStateChanged();
        }
    }

    /// <summary>重新加载当前行（「重试」按钮）；返回目标沿用上一次加载拿到的值。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task ReloadAsync(CancellationToken cancellationToken) =>
        Row is null ? Task.CompletedTask : LoadAsync(Row, cancellationToken, _returnTag);

    /// <summary>
    /// 解析评论翻页要用的作者 SteamID64：详情 → 列表行 → 物品页作者主页地址，逐级兜底。
    /// <para>
    /// 详情走缓存时 <c>CreatorSteamId</c> 可能为空（老缓存文件里没有这个键，见 §0.47），
    /// 而翻页接口必须要有它；所以再退两级：创意工坊列表行本身带着作者 SteamID，
    /// 物品页的作者块也能解析出数字 id（自定义主页地址解析不出来，那就认命，隐藏分页条）。
    /// </para>
    /// </summary>
    /// <param name="row">当前行。</param>
    /// <param name="detail">Steam 详情。</param>
    /// <param name="page">物品页（可能没拿到）。</param>
    private void ResolveCommentOwner(
        WorkshopRowViewModel row,
        SteamWorkshopItemDetail detail,
        SteamWorkshopItemPage? page)
    {
        _steamCreatorId = null;

        if (!string.IsNullOrWhiteSpace(detail.CreatorSteamId))
        {
            _steamCreatorId = detail.CreatorSteamId;
            return;
        }

        if (WorkshopText.TryParseSteamId(row.SteamItem?.AuthorSteamId, out var fromRow))
        {
            _steamCreatorId = fromRow;
            return;
        }

        if (WorkshopText.TryParseSteamId(page?.AuthorProfileUrl, out var fromPage))
        {
            _steamCreatorId = fromPage;
            return;
        }

        _logger.Info("Steam 详情拿不到作者 SteamID，评论只显示物品页内嵌的十条: " + row.Id);
    }

    /// <summary>
    /// 载入某一页评论（Steam 走翻页接口）。
    /// <para>作者 SteamID 还没拿到、或来源不是 Steam 时直接返回（首屏评论已经渲染过了）。</para>
    /// </summary>
    /// <param name="page">页号（1 基）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task LoadCommentsPageAsync(int page, CancellationToken cancellationToken)
    {
        if (Row is not { Source: WorkshopSource.Steam } row || string.IsNullOrWhiteSpace(_steamCreatorId))
        {
            return;
        }

        var target = Math.Max(1, page);

        // 起点按「已显示到哪儿」推算，而不是页号 × 页大小：物品页内嵌首屏只有 10 条，
        // 整页对齐会跳过第 11–15 条（翻页接口和物品页的分页尺寸本来就不一样）。
        var start = target > CommentsPage
            ? _commentsShownStart + _commentsShownCount
            : target < CommentsPage
                ? Math.Max(0, _commentsShownStart - CommentsPageSize)
                : _commentsShownStart;
        if (CommentsTotal > 0 && start >= CommentsTotal)
        {
            return;
        }

        IsLoadingComments = true;
        NotifyStateChanged();

        try
        {
            var result = await _source
                .GetSteamCommentsAsync(_steamCreatorId!, row.Id, start, CommentsPageSize, cancellationToken)
                .ConfigureAwait(true);

            if (result is { IsSuccess: true, Value: { } pageData })
            {
                ApplyCommentPage(pageData, target, start, cancellationToken);
                CommentsStatusText = string.Empty;
            }
            else
            {
                _logger.Info("Steam 评论翻页失败，保留当前页: " + row.Id + " page=" + target);
                CommentsStatusText = T(Keys.WorkshopCommentsFailed, Comments.Count);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("Steam 评论翻页异常: " + ex.Message);
            CommentsStatusText = T(Keys.WorkshopCommentsFailed, Comments.Count);
        }
        finally
        {
            IsLoadingComments = false;
            NotifyStateChanged();
        }
    }

    /// <summary>查询 PDX 订阅状态（未登录/接口失败时静默保持「未知」）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task LoadSubscriptionStateAsync(CancellationToken cancellationToken)
    {
        if (Row is not { Source: WorkshopSource.Pdx } row)
        {
            return;
        }

        var gameId = AppServices.CurrentGame?.GameId;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        try
        {
            var state = await _subscription
                .GetStateAsync(gameId, row.Id, cancellationToken)
                .ConfigureAwait(true);

            IsSignedIn = state.IsSignedIn;
            IsSubscriptionKnown = state.IsKnown;
            if (state.IsKnown)
            {
                IsInstalled = state.IsSubscribed;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("查询订阅状态异常: " + ex.Message);
        }
        finally
        {
            NotifyStateChanged();
        }
    }

    /// <summary>订阅 / 退订（界面已做二次确认）。成功后立刻刷新按钮与徽标。</summary>
    /// <param name="subscribe">true 订阅，false 退订。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppResult<bool>> ToggleSubscriptionAsync(
        bool subscribe,
        CancellationToken cancellationToken)
    {
        if (Row is not { Source: WorkshopSource.Pdx } row)
        {
            return AppResult<bool>.Fail(ErrorKind.Unsupported, "workshop-subscription:not-pdx");
        }

        var gameId = AppServices.CurrentGame?.GameId;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return AppResult<bool>.Fail(ErrorKind.Invalid, "workshop-subscription:no-game");
        }

        IsSubscribing = true;
        NotifyStateChanged();

        try
        {
            var result = await _subscription
                .SetSubscribedAsync(gameId, row.Id, null, subscribe, cancellationToken)
                .ConfigureAwait(true);

            if (result.IsSuccess)
            {
                IsInstalled = subscribe;
                IsSubscriptionKnown = true;
            }

            return result;
        }
        finally
        {
            IsSubscribing = false;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// 查询 Steam 工坊订阅状态：后端可用时读 Steamworks 实时状态，否则只读本地 acf。
    /// 两边都读不到就保持「未确认」，不猜。
    /// </summary>
    /// <param name="row">列表行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RefreshSteamSubscriptionAsync(WorkshopRowViewModel row, CancellationToken cancellationToken)
    {
        if (row.Source != WorkshopSource.Steam || !ulong.TryParse(row.Id, out var fileId))
        {
            return;
        }

        try
        {
            // 一层缓存：Mod 库列表刚整库查过一次（一次子进程拿全量），60 秒内直接复用，
            // 别为了一个条目再拉起一次宿主。
            if (TryReadCachedSteamState(fileId, out var cached))
            {
                ApplySteamInstallState(cached, "cache");
                return;
            }

            if (_steamBackend is { IsAvailable: true } backend)
            {
                // 宿主后端每次都要拉起一次子进程（约 1 秒），绝不能占着 UI 线程等。
                var steamState = await Task
                    .Run(() => backend.ReadState(fileId), cancellationToken)
                    .ConfigureAwait(true);

                if (steamState.IsKnown)
                {
                    ApplySteamInstallState(steamState, "Steamworks");
                }
                else if (_steamReader is not null && ResolveSteamAppId() is int fallbackAppId)
                {
                    // 宿主没给出结论（Steam 没运行、宿主超时等）时继续回退 acf，
                    // 否则已经订阅过的条目会显示成「未确认」。
                    var acf = await Task
                        .Run(() => _steamReader.Read(fallbackAppId, row.Id), cancellationToken)
                        .ConfigureAwait(true);
                    ApplySteamInstallState(acf, "acf");
                }
            }
            else if (_steamReader is not null && ResolveSteamAppId() is int appId)
            {
                // acf 解析是纯磁盘操作，丢到线程池里别占 UI 线程。
                var state = await Task
                    .Run(() => _steamReader.Read(appId, row.Id), cancellationToken)
                    .ConfigureAwait(true);
                ApplySteamInstallState(state, "acf");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("查询 Steam 订阅状态异常: " + ex.Message);
        }
        finally
        {
            NotifyStateChanged();
        }
    }

    /// <summary>订阅 / 退订 Steam 工坊条目（界面已做二次确认）。</summary>
    /// <param name="subscribe">true 订阅，false 退订。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<WorkshopSubscribeResult> ToggleSteamSubscriptionAsync(
        bool subscribe,
        CancellationToken cancellationToken)
    {
        if (_steamBackend is not { IsAvailable: true } backend)
        {
            return WorkshopSubscribeResult.Fail(T(Keys.WorkshopDetailSteamSubscribeHintOff));
        }

        if (!CanWriteSteam)
        {
            // 只读档：能看状态，但不许改订阅，免得用户以为开关没生效。
            return WorkshopSubscribeResult.Fail(T(Keys.WorkshopDetailSteamReadOnlyHint));
        }

        if (Row is not { } row || !ulong.TryParse(row.Id, out var fileId))
        {
            return WorkshopSubscribeResult.Fail(T(Keys.WorkshopDetailErrorUnknown));
        }

        IsSteamSubscribing = true;
        NotifyStateChanged();

        try
        {
            var result = await backend
                .SetSubscribedAsync(fileId, subscribe, cancellationToken)
                .ConfigureAwait(true);

            if (result.Ok)
            {
                IsSteamSubscribed = subscribe;
                IsSteamStateKnown = true;
            }

            return result;
        }
        finally
        {
            IsSteamSubscribing = false;
            NotifyStateChanged();
        }
    }

    /// <summary>把 acf / Steamworks 读到的状态落到界面上。</summary>
    /// <param name="state">读到的安装状态。</param>
    /// <param name="source">状态来源（Steamworks / cache / acf），只用于日志。</param>
    private void ApplySteamInstallState(WorkshopInstallState state, string source)
    {
        _logger.Info(
            "Steam 订阅状态（" + source + "）：订阅=" + Describe(state.IsSubscribed)
            + " 安装=" + Describe(state.IsInstalled)
            + " 下载中=" + Describe(state.IsDownloading)
            + " 需要更新=" + Describe(state.NeedsUpdate)
            + " 本地禁用=" + Describe(state.IsDisabledLocally)
            + " acf=" + (state.AcfPath ?? "无"));

        IsSteamNeedsUpdate = state.NeedsUpdate == true;
        SteamDownloadProgressText = BuildDownloadProgressText(state);

        if (state.IsSubscribed is bool subscribed)
        {
            IsSteamSubscribed = subscribed;
            IsSteamStateKnown = true;
        }
        else if (state.IsInstalled is bool installed)
        {
            // 只有内容目录、没有 acf：能确定装过，但不敢断言订阅状态。
            IsSteamSubscribed = installed;
            IsSteamStateKnown = false;
        }
    }

    /// <summary>下载进度文案：只有真的在下、且拿得到百分比时才出。</summary>
    private string BuildDownloadProgressText(WorkshopInstallState state)
    {
        if (state.IsDownloading != true && state.IsDownloadPending != true)
        {
            return string.Empty;
        }

        return state.DownloadPercent is double percent
            ? T(Keys.WorkshopDetailSteamDownloadProgress, (int)Math.Round(percent))
            : string.Empty;
    }

    /// <summary>查 Mod 库那层 60 秒整库缓存里有没有这一条。</summary>
    private static bool TryReadCachedSteamState(ulong publishedFileId, out WorkshopInstallState state)
    {
        state = WorkshopInstallState.Unknown;

        if (ResolveSteamAppId() is not int appId)
        {
            return false;
        }

        if (!AppServices.WorkshopStates.TryGet(appId, out var states)
            || !states.TryGetValue(publishedFileId, out var cached))
        {
            return false;
        }

        state = cached;
        return state.IsKnown;
    }

    /// <summary>把三态布尔写成日志里能一眼看懂的中文。</summary>
    private static string Describe(bool? value) => value switch
    {
        true => "是",
        false => "否",
        _ => "未知",
    };

    /// <summary>当前游戏的 Steam appid（acf 与 Steamworks 都要它）。</summary>
    private static int? ResolveSteamAppId() =>
        WorkshopSourceChooser.ResolveAppId(
            AppServices.CurrentGame?.SteamAppId,
            AppServices.CurrentGame?.GameId);

    /// <summary>下行前把上一次的内容清掉，避免看到上一个 mod 的正文。</summary>
    private void ResetForLoad(WorkshopRowViewModel row)
    {
        Title = row.Name;
        SourceLabel = row.Source == WorkshopSource.Steam
            ? T(Keys.WorkshopSourceSteam)
            : T(Keys.WorkshopSourcePdx);
        AuthorText = row.AuthorText;
        AuthorProfileUrl = null;
        MetaText = row.SubtitleText;
        CreatedText = string.Empty;
        DescriptionText = string.Empty;
        DescriptionDocument = null;
        TranslatedDescriptionText = string.Empty;
        TranslatedDescriptionDocument = null;
        ChangelogDocument = null;
        TranslatedChangelogDocument = null;
        IsTranslatingDescription = false;
        IsTranslatingChangelog = false;
        IsTranslatingComments = false;
        TagsText = string.Empty;
        DependencySummary = string.Empty;
        CommentsSummary = string.Empty;
        ErrorText = string.Empty;
        CachedHint = string.Empty;
        TranslationNotice = string.Empty;
        _translateFailureScopes.Clear();
        IsInstalled = false;
        HasError = false;
        IsLoaded = false;
        IsLoading = true;
        _galleryTracker.Reset();
        NotifyGalleryActivity();
        IsLoadingComments = false;
        IsSubscriptionKnown = false;
        IsSignedIn = false;
        IsSubscribing = false;
        CommentsTotal = 0;
        CommentsPage = 1;
        CommentsStatusText = string.Empty;
        _steamCreatorId = null;
        _inlineImages.Clear();
        IsSteamSubscribed = false;
        IsSteamStateKnown = false;
        IsSteamSubscribing = false;
        IsSteamNeedsUpdate = false;
        SteamDownloadProgressText = string.Empty;

        // 列表里已经下好的封面先直接用，别在详情页再下一次。
        CoverPath = row.CoverPath;

        Screenshots.Clear();
        Links.Clear();
        Dependencies.Clear();
        Comments.Clear();
    }

    /// <summary>封面：列表没给就按缓存复用/下载补一张。</summary>
    private async Task LoadCoverAsync(WorkshopRowViewModel row, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(CoverPath))
        {
            return;
        }

        CoverPath = await _images
            .EnsureCoverAsync(row.Source, row.Id, row.CoverUrl, cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Steam：详情接口给描述/体积/标签，物品页给作者主页、描述配图与评论区。
    /// 两路并发，物品页失败只影响配图与评论（静默降级）。
    /// </summary>
    private async Task ApplySteamAsync(WorkshopRowViewModel row, CancellationToken cancellationToken)
    {
        var detailTask = _source.GetSteamDetailAsync(row.Id, row.Name, cancellationToken);
        var pageTask = _source.GetSteamItemPageAsync(row.Id, cancellationToken);

        var detailResult = await detailTask.ConfigureAwait(true);
        if (detailResult is not { IsSuccess: true, Value: { } detail })
        {
            UseCachedDescription(row, detailResult.ErrorMessage);
            return;
        }

        var item = row.SteamItem;
        MetaText = BuildSteamMeta(item, detail);
        AuthorProfileUrl = detail.CreatorProfileUrl;
        TagsText = BuildSteamTags(detail, item);
        CommentsStatusText = string.Empty;

        // 缓存命中的 CreatedUtc 是「抓取时间」，不是发布时间，所以只认列表条目里的真实值。
        CreatedText = item is { } steamItem && steamItem.CreatedUtc > DateTimeOffset.UnixEpoch
            ? T(Keys.WorkshopDetailCreated, WorkshopText.FormatDate(steamItem.CreatedUtc))
            : string.Empty;

        var descriptionHtml = (string?)null;
        var imageUrls = new List<string>();

        var pageResult = await pageTask.ConfigureAwait(true);
        SteamWorkshopItemPage? page = null;
        if (pageResult is { IsSuccess: true, Value: { } itemPage })
        {
            page = itemPage;
            descriptionHtml = page.DescriptionHtml;
            imageUrls.AddRange(page.ImageUrls);
            if (!string.IsNullOrWhiteSpace(page.AuthorProfileUrl))
            {
                AuthorProfileUrl = page.AuthorProfileUrl;
            }

            // 列表条目里只有作者的 SteamID，物品页才有显示名。
            if (!string.IsNullOrWhiteSpace(page.AuthorName))
            {
                AuthorText = page.AuthorName;
            }
        }
        else
        {
            _logger.Info("Steam 物品页不可用，评论区与描述配图降级: " + row.Id);
            CommentsSummary = string.Empty;
        }

        // 翻页接口要的是作者 SteamID64，取不到就只能显示物品页内嵌的那十条。
        ResolveCommentOwner(row, detail, page);

        // 首屏评论：物品页本身已经内嵌了前十条，直接用它，不再额外打一次评论接口
        // （这一条以前是「详情页三条请求里唯一冗余的那条」）。只有内嵌评论为空、或物品页
        // 整体不可用时，才回退到翻页接口。
        if (page is { Comments.Count: > 0 } embedded && _steamCreatorId is not null)
        {
            ApplyComments(embedded, cancellationToken);
            CommentsStatusText = string.Empty;
        }
        else if (_steamCreatorId is not null)
        {
            await LoadCommentsPageAsync(1, cancellationToken).ConfigureAwait(true);

            if (Comments.Count == 0)
            {
                if (page is not null)
                {
                    ApplyComments(page, cancellationToken);
                }

                // 物品页自己就说「一条评论都没有」时，说「没有评论」比说「加载失败」准确；
                // 关闭了评论的 mod（接口回 success:false）走的就是这条。
                var closed = page is { Comments.Count: 0 } && page.TotalComments <= 0;
                CommentsStatusText = closed
                    ? T(Keys.WorkshopCommentsClosed)
                    : T(Keys.WorkshopCommentsFailed, Comments.Count);
            }
        }
        else if (page is not null)
        {
            ApplyComments(page, cancellationToken);

            // 有内嵌评论说明这个 mod 只是没拿到作者 id；一条都没有才是真的没有评论。
            CommentsStatusText = Comments.Count > 0
                ? T(Keys.WorkshopCommentsUnavailable)
                : T(Keys.WorkshopCommentsClosed);
        }
        else
        {
            CommentsStatusText = T(Keys.WorkshopCommentsUnavailable);
        }

        // 描述优先用物品页的 HTML（有排版），退到接口给的 BBCode 原文 —— 兜底也要保留
        // 标题 / 列表 / 分隔线，所以走 SteamBbCodeParser 而不是先压成纯文本再按 Markdown 解析。
        var document = !string.IsNullOrWhiteSpace(descriptionHtml)
            ? HtmlDescriptionParser.Parse(descriptionHtml)
            : SteamBbCodeParser.Parse(detail.Description);
        ApplyDescription(document, cancellationToken);

        // 顶部图片区用物品页官方画廊（#highlight_player_area）里的截图；抓不到时退回预览图。
        // 简介里的 <img> 属于正文，单独下载给正文用，不再铺到顶部。
        var previewUrl = !string.IsNullOrWhiteSpace(detail.PreviewUrl)
            ? detail.PreviewUrl
            : !string.IsNullOrWhiteSpace(row.CoverUrl) ? row.CoverUrl : null;

        var galleryUrls = new List<string>();
        if (page is { ScreenshotUrls.Count: > 0 })
        {
            galleryUrls.AddRange(page.ScreenshotUrls);
        }
        else if (previewUrl is not null)
        {
            // 只有视频、或页面结构变化抓不到画廊时的兜底：至少显示封面。
            galleryUrls.Add(previewUrl);
        }

        BuildGallery(row, galleryUrls);

        // Steam 工坊订阅状态：后端可用读实时状态，否则读 acf（都读不到就保持未知）。
        await RefreshSteamSubscriptionAsync(row, cancellationToken).ConfigureAwait(true);

        var inlineUrls = imageUrls.Count > 0 ? imageUrls : document.ImageUrls.ToList();

        NotifyStateChanged();
        IsLoading = false;
        // 按「集合里实际有几种截图」计数（BuildGallery 会去重），不按抓到的 URL 条数。
        _galleryTracker.Begin(Screenshots.Count);
        NotifyGalleryActivity();
        NotifyStateChanged();

        try
        {
            await Task.WhenAll(
                DownloadGalleryAsync(row, cancellationToken),
                DownloadInlineImagesAsync(row, inlineUrls, cancellationToken)).ConfigureAwait(true);
        }
        finally
        {
            // 取消或异常时剩下的条目不会再结算，这里兜底收尾，避免提示卡在「下载中」。
            _galleryTracker.FinishAll();
            NotifyGalleryActivity();
            NotifyStateChanged();
        }
    }

    /// <summary>PDX：正文与更新日志本来就是 Markdown；截图、外链、依赖都要补。</summary>
    private async Task ApplyPdxAsync(WorkshopRowViewModel row, CancellationToken cancellationToken)
    {
        var result = await _source.GetPdxDetailAsync(row.Id, cancellationToken).ConfigureAwait(true);
        if (result is not { IsSuccess: true, Value: { } detail })
        {
            UseCachedDescription(row, result.ErrorMessage);
            return;
        }

        ApplyDescription(MarkdownParser.Parse(detail.LongDescription), cancellationToken);
        var changelog = MarkdownParser.Parse(detail.Changelog);
        ChangelogDocument = changelog;
        if (!changelog.IsEmpty)
        {
            StartChangelogTranslation(changelog, cancellationToken);
        }
        MetaText = BuildPdxMeta(detail);
        CreatedText = detail.Summary.CreatedOn is { } created && created > DateTimeOffset.UnixEpoch
            ? T(Keys.WorkshopDetailCreated, WorkshopText.FormatDate(created))
            : string.Empty;
        TagsText = WorkshopText.Join(detail.Summary.Tags, " · ");
        IsInstalled = detail.IsInstalled;

        BuildLinks(detail);
        BuildGallery(row, detail.ScreenshotUrls);

        await BuildDependenciesAsync(detail, cancellationToken).ConfigureAwait(true);
        await LoadSubscriptionStateAsync(cancellationToken).ConfigureAwait(true);

        NotifyStateChanged();

        // 正文与元信息已经能看了，就不该再挂着「正在读取详情…」——
        // 剩下只有截图在下载，改用截图卡里的独立进度提示。
        IsLoading = false;
        _galleryTracker.Begin(Screenshots.Count);
        NotifyGalleryActivity();
        NotifyStateChanged();

        try
        {
            await DownloadGalleryAsync(row, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            // 取消或异常时剩下的条目不会再结算，这里兜底收尾，避免提示卡在「下载中」。
            _galleryTracker.FinishAll();
            NotifyGalleryActivity();
            NotifyStateChanged();
        }
    }

    /// <summary>把 AST 同时存成富文本与纯文本（纯文本供无障碍名与「无简介」判断）。</summary>
    /// <param name="document">解析后的正文。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌（供后台译文使用）。</param>
    private void ApplyDescription(MarkdownDocument document, CancellationToken cancellationToken)
    {
        DescriptionDocument = document.IsEmpty ? null : document;
        DescriptionText = document.ToPlainText();
        TranslatedDescriptionDocument = null;
        TranslatedDescriptionText = string.Empty;

        if (document.IsEmpty)
        {
            return;
        }

        StartDescriptionTranslation(document, cancellationToken);
    }

    /// <summary>后台译简介：译文与原文并存，译好再挂上去。</summary>
    /// <param name="document">原文正文。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private void StartDescriptionTranslation(MarkdownDocument document, CancellationToken cancellationToken)
    {
        if (!AppServices.Settings.TranslateDescriptionEnabled || !AppServices.Translation.IsEnabled)
        {
            return;
        }

        _ = TranslateDescriptionAsync(document, cancellationToken);
    }

    /// <summary>翻译简介正文并写回译文属性。</summary>
    private async Task TranslateDescriptionAsync(MarkdownDocument document, CancellationToken cancellationToken)
    {
        IsTranslatingDescription = true;
        try
        {
            var translated = await AppServices.Translation
                .TranslateDocumentAsync(document, cancellationToken)
                .ConfigureAwait(true);
            if (translated.IsEmpty)
            {
                return;
            }

            var plain = translated.ToPlainText();
            if (string.Equals(plain, document.ToPlainText(), StringComparison.Ordinal))
            {
                // 没翻出不同内容，就不重复显示一遍原文。
                ReportTranslationFailure("description");
                return;
            }

            TranslatedDescriptionDocument = translated;
            TranslatedDescriptionText = plain;
        }
        catch (OperationCanceledException)
        {
            // 页面已关闭或设置已变更：这批译文作废，不记错误。
        }
        catch (Exception ex)
        {
            _logger.Warn("简介翻译失败：" + ex.Message);
        }
        finally
        {
            IsTranslatingDescription = false;
        }
    }

    /// <summary>后台译更新日志（只有 PDX 侧才有更新日志）。</summary>
    /// <param name="document">原文更新日志。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private void StartChangelogTranslation(MarkdownDocument document, CancellationToken cancellationToken)
    {
        if (!AppServices.Settings.TranslateChangelogEnabled || !AppServices.Translation.IsEnabled)
        {
            return;
        }

        _ = TranslateChangelogAsync(document, cancellationToken);
    }

    /// <summary>翻译更新日志并写回译文属性。</summary>
    private async Task TranslateChangelogAsync(MarkdownDocument document, CancellationToken cancellationToken)
    {
        IsTranslatingChangelog = true;
        try
        {
            var translated = await AppServices.Translation
                .TranslateDocumentAsync(document, cancellationToken)
                .ConfigureAwait(true);
            if (translated.IsEmpty)
            {
                return;
            }

            // 更新日志没翻出不同内容时保留原文，并提示一次「翻译没出结果」。
            if (string.Equals(
                    translated.ToPlainText(),
                    document.ToPlainText(),
                    StringComparison.Ordinal))
            {
                ReportTranslationFailure("changelog");
                return;
            }

            TranslatedChangelogDocument = translated;
        }
        catch (OperationCanceledException)
        {
            // 页面已关闭或设置已变更：这批译文作废，不记错误。
        }
        catch (Exception ex)
        {
            _logger.Warn("更新日志翻译失败：" + ex.Message);
        }
        finally
        {
            IsTranslatingChangelog = false;
        }
    }

    /// <summary>后台译评论正文：逐条译，译好一条挂一条。</summary>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private void StartCommentsTranslation(CancellationToken cancellationToken)
    {
        if (!AppServices.Settings.TranslateCommentsEnabled || !AppServices.Translation.IsEnabled)
        {
            return;
        }

        _ = TranslateCommentsAsync(cancellationToken);
    }

    /// <summary>逐条翻译评论正文。</summary>
    private async Task TranslateCommentsAsync(CancellationToken cancellationToken)
    {
        var attempted = 0;
        var translatedAny = false;
        IsTranslatingComments = true;
        try
        {
            foreach (var comment in Comments)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!comment.HasBody)
                {
                    continue;
                }

                attempted++;
                var translated = await AppServices.Translation
                    .TranslateDocumentAsync(comment.Body, cancellationToken)
                    .ConfigureAwait(true);
                if (translated.IsEmpty)
                {
                    continue;
                }

                var plain = translated.ToPlainText();
                if (string.Equals(plain, comment.BodyPlain, StringComparison.Ordinal))
                {
                    // 没翻出不同内容，就不重复显示一遍原文。
                    continue;
                }

                comment.TranslatedBody = translated;
                comment.TranslatedBodyPlain = plain;
                translatedAny = true;
            }
        }
        catch (OperationCanceledException)
        {
            // 页面已关闭或设置已变更：这批译文作废，不记错误。
        }
        catch (Exception ex)
        {
            _logger.Warn("评论翻译失败：" + ex.Message);
        }
        finally
        {
            IsTranslatingComments = false;
        }

        if (attempted > 0 && !translatedAny)
        {
            ReportTranslationFailure("comments");
        }
    }

    /// <summary>
    /// 某个范围整片没翻出结果时，在详情页顶部提示一句，并说明可以去设置页测引擎。
    /// 每个范围只提示一次，避免逐条失败刷屏。
    /// </summary>
    /// <param name="scope">范围标识：description / changelog / comments。</param>
    private void ReportTranslationFailure(string scope)
    {
        if (!_translateFailureScopes.Add(scope))
        {
            return;
        }

        var reason = AppServices.Translation.LastFailure ?? T(Keys.CommonUnknown);
        _logger.Warn("创意工坊详情翻译没有出结果（" + scope + "）：" + reason);
        TranslationNotice = T(Keys.WorkshopTranslateUnavailable, reason);
    }

    /// <summary>图片墙：先按顺序建好行，下载留给 <see cref="DownloadGalleryAsync"/>。</summary>
    private void BuildGallery(WorkshopRowViewModel row, IReadOnlyList<string> urls)
    {
        Screenshots.Clear();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
            {
                continue;
            }

            Screenshots.Add(new WorkshopScreenshotViewModel(Screenshots.Count, url));
        }
    }

    /// <summary>依赖清单与本机 mod 库比对；读不到库时全部标「未知」。</summary>
    private async Task BuildDependenciesAsync(PdxModsDetail detail, CancellationToken cancellationToken)
    {
        if (detail.Requirements.Count == 0)
        {
            return;
        }

        var local = await _source
            .LoadLocalModsAsync(AppServices.CurrentGame, cancellationToken)
            .ConfigureAwait(true);

        var matched = WorkshopDependencyMatcher.Match(detail.Requirements, local.Installed, local.Enabled);
        for (var index = 0; index < matched.Count; index++)
        {
            Dependencies.Add(new WorkshopDependencyViewModel(index, matched[index]));
        }

        var missing = matched.Count(static item => item.State == WorkshopDependencyState.Missing);
        var disabled = matched.Count(static item => item.State == WorkshopDependencyState.InstalledDisabled);

        DependencySummary = missing > 0
            ? T(Keys.WorkshopDependencySummaryMissing, matched.Count, missing)
            : disabled > 0
                ? T(Keys.WorkshopDependencySummaryDisabled, matched.Count, disabled)
                : T(Keys.WorkshopDependencySummaryOk, matched.Count);
    }

    /// <summary>评论区：首屏评论 + 总数提示。</summary>
    /// <param name="page">物品页内嵌评论。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌（供后台译文使用）。</param>
    private void ApplyComments(SteamWorkshopItemPage page, CancellationToken cancellationToken = default)
    {
        Comments.Clear();

        for (var index = 0; index < page.Comments.Count; index++)
        {
            Comments.Add(BuildComment(index, page.Comments[index]));
        }

        CommentsTotal = page.TotalComments > 0 ? page.TotalComments : Comments.Count;
        CommentsPage = 1;
        _commentsShownStart = 0;
        _commentsShownCount = Comments.Count;

        CommentsSummary = Comments.Count > 0
            ? T(Keys.WorkshopCommentsCount, CommentsTotal)
            : string.Empty;

        StartCommentsTranslation(cancellationToken);
    }

    /// <summary>把翻页接口返回的一页评论换成界面上的列表。</summary>
    /// <param name="page">接口返回的一页评论。</param>
    /// <param name="requestedPage">界面上的页号（用于页码文案）。</param>
    /// <param name="requestedStart">本次请求的起始偏移（接口没回 start 时用它记账）。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌（供后台译文使用）。</param>
    private void ApplyCommentPage(
        SteamWorkshopCommentPage page,
        int requestedPage,
        int requestedStart,
        CancellationToken cancellationToken = default)
    {
        Comments.Clear();

        for (var index = 0; index < page.Comments.Count; index++)
        {
            Comments.Add(BuildComment(index, page.Comments[index]));
        }

        if (page.TotalCount > 0)
        {
            CommentsTotal = page.TotalCount;
        }

        CommentsPage = Math.Max(1, requestedPage);
        _commentsShownStart = Math.Max(0, requestedStart);
        _commentsShownCount = page.Comments.Count;
        CommentsSummary = CommentsTotal > 0
            ? T(Keys.WorkshopCommentsCount, CommentsTotal)
            : string.Empty;

        StartCommentsTranslation(cancellationToken);
    }

    /// <summary>一条评论 → 行视图模型（正文优先按 HTML 排版解析）。</summary>
    private static WorkshopCommentViewModel BuildComment(int index, SteamWorkshopComment comment)
    {
        var body = !string.IsNullOrWhiteSpace(comment.TextHtml)
            ? HtmlDescriptionParser.Parse(comment.TextHtml)
            : MarkdownParser.Parse(comment.TextPlain);

        return new WorkshopCommentViewModel(
            index,
            comment.AuthorName,
            comment.AuthorProfileUrl,
            comment.PostedUtc,
            body,
            comment.TextPlain);
    }

    /// <summary>详情取不到时退回缓存正文，至少还能看上次抓到的东西。</summary>
    private void UseCachedDescription(WorkshopRowViewModel row, string? errorMessage)
    {
        var cached = _source.TryGetCachedDescription(row.Source, row.Id);
        if (string.IsNullOrWhiteSpace(cached))
        {
            Fail(errorMessage);
            return;
        }

        ApplyDescription(MarkdownParser.Parse(cached), CancellationToken.None);
        CachedHint = T(Keys.WorkshopDetailCached);
        IsLoaded = true;
    }

    /// <summary>
    /// 加载失败的原因文案。被 Steam 限流时给出「请约 N 分钟后重试」，
    /// 而不是把「HTTP 429（被限流）」这种内部措辞直接甩给用户。
    /// </summary>
    private static string DescribeLoadFailure(Exception exception)
    {
        if (exception is not SteamThrottleException throttle)
        {
            return exception.Message;
        }

        var remaining = throttle.RetryAfter ?? TimeSpan.Zero;
        if (remaining <= TimeSpan.Zero
            && AppServices.HttpThrottle.TryGetCooldown("steamcommunity.com", out var untilUtc))
        {
            remaining = untilUtc - DateTimeOffset.UtcNow;
        }

        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return T(Keys.WorkshopThrottleRetryIn, minutes);
    }

    /// <summary>失败落点：错误提示 + 保证「重试」能重来。</summary>
    private void Fail(string? errorMessage)
    {
        HasError = true;
        IsLoaded = false;
        ErrorText = T(Keys.WorkshopDetailFailed, string.IsNullOrWhiteSpace(errorMessage) ? T(Keys.WorkshopDetailErrorUnknown) : errorMessage);
    }

    /// <summary>Steam 的元信息一行：评分 · 订阅 · 收藏 · 浏览 · 体积 · 更新于。</summary>
    private static string BuildSteamMeta(SteamWorkshopItem? item, SteamWorkshopItemDetail detail)
    {
        var views = item is { Views: > 0 } ? item.Views : detail.Views;
        var subs = item is { Subscriptions: > 0 } ? item.Subscriptions : detail.LifetimeSubscriptions;

        var parts = new List<string?>
        {
            item is null || item.TotalVotes <= 0 || item.StarRating <= 0
                ? null
                : T(Keys.WorkshopRating, item.StarRating.ToString("0.##", CultureInfo.InvariantCulture), item.TotalVotes),
            subs <= 0
                ? null
                : T(Keys.WorkshopSubscriptions, subs.ToString("N0", CultureInfo.CurrentCulture)),
            item is null || item.Favorites <= 0
                ? null
                : T(Keys.WorkshopDetailFavorites, item.Favorites.ToString("N0", CultureInfo.CurrentCulture)),
            views <= 0
                ? null
                : T(Keys.WorkshopDetailViews, views.ToString("N0", CultureInfo.CurrentCulture)),
            detail.FileSizeBytes <= 0
                ? null
                : T(Keys.WorkshopSize, SizeFormatter.Format(detail.FileSizeBytes)),
            detail.UpdatedUtc > DateTimeOffset.UnixEpoch
                ? T(Keys.WorkshopUpdated, WorkshopText.FormatDate(detail.UpdatedUtc))
                : null,
        };

        return WorkshopText.Join(parts, " · ");
    }

    /// <summary>Steam 标签：详情接口给了就用，没有就退回列表条目。</summary>
    private static string BuildSteamTags(SteamWorkshopItemDetail detail, SteamWorkshopItem? item)
    {
        if (detail.TagList.Count > 0)
        {
            return WorkshopText.Join(detail.TagList, " · ");
        }

        return item is null
            ? string.Empty
            : WorkshopText.Join(item.Tags.Select(static tag => tag.DisplayName), " · ");
    }

    /// <summary>PDX 的元信息一行：评分 · 订阅 · 体积 · 更新于。</summary>
    private static string BuildPdxMeta(PdxModsDetail detail)
    {
        var summary = detail.Summary;
        var parts = new List<string?>
        {
            summary.RatingsTotal > 0
                ? T(Keys.WorkshopRating, summary.Rating.ToString("0.##", CultureInfo.InvariantCulture), summary.RatingsTotal)
                : null,
            summary.Subscriptions > 0
                ? T(Keys.WorkshopSubscriptions, summary.Subscriptions.ToString("N0", CultureInfo.CurrentCulture))
                : null,
            detail.SizeBytes is null or <= 0
                ? null
                : T(Keys.WorkshopSize, SizeFormatter.Format(detail.SizeBytes.Value)),
            summary.UpdatedOn is { } updated && updated > DateTimeOffset.UnixEpoch
                ? T(Keys.WorkshopUpdated, WorkshopText.FormatDate(updated))
                : null,
        };

        return WorkshopText.Join(parts, " · ");
    }

    /// <summary>外链 + 论坛帖：标题缺失时依次退到类型标签、主机名。</summary>
    private void BuildLinks(PdxModsDetail detail)
    {
        for (var index = 0; index < detail.ExternalLinks.Count; index++)
        {
            var link = detail.ExternalLinks[index];
            var type = index < detail.ExternalLinkTypes.Count ? detail.ExternalLinkTypes[index] : link.Type;
            Links.Add(new WorkshopLinkViewModel(ResolveLinkTitle(link, type), link.Url, type, Links.Count));
        }

        if (!string.IsNullOrWhiteSpace(detail.ForumUrl))
        {
            Links.Add(new WorkshopLinkViewModel(
                T(Keys.WorkshopDetailForum),
                detail.ForumUrl!,
                "forum",
                Links.Count));
        }
    }

    /// <summary>链接标题：显示名 → 类型标签 → 主机名，三个都空就显示原地址。</summary>
    private static string ResolveLinkTitle(PdxModsLink link, string type)
    {
        if (!string.IsNullOrWhiteSpace(link.DisplayName))
        {
            return link.DisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            return type.Trim();
        }

        return Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : link.Url;
    }

    /// <summary>图片逐张下载（最多 2 张并发），每张完成即刷新对应项。</summary>
    private async Task DownloadGalleryAsync(WorkshopRowViewModel row, CancellationToken cancellationToken)
    {
        if (Screenshots.Count == 0)
        {
            return;
        }

        using var gate = new SemaphoreSlim(MaxConcurrentScreenshots);
        var items = Screenshots.ToList();

        await Task.WhenAll(items.Select(async shot =>
        {
            var acquired = false;
            var failed = false;
            try
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(true);
                acquired = true;

                shot.Path = await _images
                    .EnsureScreenshotAsync(row.Source, row.Id, shot.Url, cancellationToken)
                    .ConfigureAwait(true);

                if (shot.HasPath)
                {
                    // 正文里的内联图片拿的是同一份缓存文件，通知页面原地换图。
                    GalleryImageReady?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单张图片失败不该让整页报错：这一张显示「下载失败」，其余继续下。
                failed = true;
                _logger.Warn("创意工坊图片下载失败: " + row.Id + " #" + shot.Index + " " + ex.Message);
            }
            finally
            {
                if (acquired)
                {
                    gate.Release();
                }

                // 每张图自己结算一次：大图的「下载中」叠层与顶部批次提示都靠它推进，
                // 不再等整批（更不等正文配图）结束。
                shot.MarkSettled(failed);
                if (_galleryTracker.Settle())
                {
                    NotifyGalleryActivity();
                }
            }
        })).ConfigureAwait(true);
    }

    /// <summary>正文配图逐张下载（最多 2 张并发），每张完成即通知页面原地换图。</summary>
    private async Task DownloadInlineImagesAsync(
        WorkshopRowViewModel row,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
        {
            return;
        }

        // 同一张图在正文里可能出现多次，按 URL 去重后再下。
        var distinct = urls.Distinct(StringComparer.Ordinal).ToList();

        using var gate = new SemaphoreSlim(MaxConcurrentScreenshots);

        await Task.WhenAll(distinct.Select(async url =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                var path = await _images
                    .EnsureScreenshotAsync(row.Source, row.Id, url, cancellationToken)
                    .ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(path))
                {
                    _inlineImages[url] = path;
                    GalleryImageReady?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单张配图失败不该让整页报错，留占位即可。
                _logger.Warn("创意工坊正文配图下载失败: " + row.Id + " " + url + " " + ex.Message);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(true);
    }

    /// <summary>所有派生可见性一次刷完（x:Bind OneWay 靠这些通知）。</summary>
    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasCover));
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(ShowNoDescription));
        OnPropertyChanged(nameof(HasChangelog));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(HasScreenshots));
        OnPropertyChanged(nameof(ShowGalleryCard));
        OnPropertyChanged(nameof(HasLinks));
        OnPropertyChanged(nameof(HasDependencies));
        OnPropertyChanged(nameof(HasDependencySummary));
        OnPropertyChanged(nameof(HasComments));
        OnPropertyChanged(nameof(ShowNoComments));
        OnPropertyChanged(nameof(ShowCommentsSection));
        OnPropertyChanged(nameof(HasCommentsSummary));
        OnPropertyChanged(nameof(HasCommentsStatusText));
        OnPropertyChanged(nameof(HasCreated));
        OnPropertyChanged(nameof(HasCachedHint));
        OnPropertyChanged(nameof(HasTranslationNotice));
        OnPropertyChanged(nameof(ShowInstalled));
        OnPropertyChanged(nameof(HasAuthorProfile));
        OnPropertyChanged(nameof(WebUrl));
        OnPropertyChanged(nameof(HasWebUrl));
        OnPropertyChanged(nameof(SteamUrl));
        OnPropertyChanged(nameof(CanOpenSteam));
        OnPropertyChanged(nameof(BackText));
        OnPropertyChanged(nameof(HasCommentsPaging));
        OnPropertyChanged(nameof(CommentsPageCount));
        OnPropertyChanged(nameof(CommentsPageText));
        OnPropertyChanged(nameof(HasCommentsPageText));
        OnPropertyChanged(nameof(CanCommentsPrev));
        OnPropertyChanged(nameof(CanCommentsNext));
        OnPropertyChanged(nameof(CanSubscribe));
        OnPropertyChanged(nameof(CanToggleSubscription));
        OnPropertyChanged(nameof(SubscribeActionText));
        OnPropertyChanged(nameof(SubscribeHint));
        OnPropertyChanged(nameof(HasSubscribeHint));
        OnPropertyChanged(nameof(ShowSubscribeCard));
        OnPropertyChanged(nameof(ShowPdxSubscribeButton));
        OnPropertyChanged(nameof(ShowSteamSubscribeButton));
        OnPropertyChanged(nameof(ShowSubscriptionState));
        OnPropertyChanged(nameof(HasStatusText));
        OnPropertyChanged(nameof(SteamSubscribeActionText));
        OnPropertyChanged(nameof(CanToggleSteamSubscription));
        OnPropertyChanged(nameof(SteamSubscribeHint));
        OnPropertyChanged(nameof(HasSteamSubscribeHint));
        OnPropertyChanged(nameof(HasSteamUnavailableReason));
        OnPropertyChanged(nameof(ShowSteamSubscribeState));
    }

    /// <summary>把「还有几张截图没落地」同步到批次提示；每张结算都会走一次。</summary>
    private void NotifyGalleryActivity() => IsLoadingScreenshots = _galleryTracker.IsBusy;

    partial void OnCoverPathChanged(string? value) => OnPropertyChanged(nameof(HasCover));

    partial void OnDescriptionTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(ShowNoDescription));
    }

    partial void OnDescriptionDocumentChanged(MarkdownDocument? value)
    {
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(ShowNoDescription));
    }

    partial void OnChangelogDocumentChanged(MarkdownDocument? value) => OnPropertyChanged(nameof(HasChangelog));

    partial void OnTranslatedDescriptionDocumentChanged(MarkdownDocument? value)
    {
        // 译文一到就立刻把描边框露出来（此前没人发这个通知，得切页才会刷新）。
        OnPropertyChanged(nameof(HasTranslatedDescription));
        OnPropertyChanged(nameof(ShowTranslatingDescription));
    }

    partial void OnTranslatedChangelogDocumentChanged(MarkdownDocument? value)
    {
        OnPropertyChanged(nameof(HasTranslatedChangelog));
        OnPropertyChanged(nameof(ShowTranslatingChangelog));
    }

    partial void OnIsTranslatingDescriptionChanged(bool value) => OnPropertyChanged(nameof(ShowTranslatingDescription));

    partial void OnIsTranslatingChangelogChanged(bool value) => OnPropertyChanged(nameof(ShowTranslatingChangelog));

    partial void OnIsTranslatingCommentsChanged(bool value) => OnPropertyChanged(nameof(ShowTranslatingComments));

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(ShowInstalled));

    partial void OnCachedHintChanged(string value) => OnPropertyChanged(nameof(HasCachedHint));

    partial void OnTranslationNoticeChanged(string value) => OnPropertyChanged(nameof(HasTranslationNotice));

    partial void OnAuthorProfileUrlChanged(string? value) => OnPropertyChanged(nameof(HasAuthorProfile));

    partial void OnIsLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNoDescription));
        OnPropertyChanged(nameof(ShowNoComments));
    }

    partial void OnIsLoadingCommentsChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNoComments));

        // 加载中禁点分页按钮，避免连点重复请求。
        OnPropertyChanged(nameof(CanCommentsPrev));
        OnPropertyChanged(nameof(CanCommentsNext));
    }

    partial void OnIsSubscribingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggleSubscription));
        OnPropertyChanged(nameof(SubscribeActionText));
    }

    partial void OnIsSignedInChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggleSubscription));
        OnPropertyChanged(nameof(SubscribeHint));
        OnPropertyChanged(nameof(HasSubscribeHint));
    }

    partial void OnIsSubscriptionKnownChanged(bool value) => OnPropertyChanged(nameof(ShowSubscriptionState));

    partial void OnCommentsTotalChanged(int value)
    {
        OnPropertyChanged(nameof(HasCommentsPaging));
        OnPropertyChanged(nameof(CommentsPageCount));
        OnPropertyChanged(nameof(CommentsPageText));
        OnPropertyChanged(nameof(HasCommentsPageText));
        OnPropertyChanged(nameof(CanCommentsPrev));
        OnPropertyChanged(nameof(CanCommentsNext));
    }

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatusText));

    partial void OnCommentsPageChanged(int value)
    {
        OnPropertyChanged(nameof(CommentsPageText));
        OnPropertyChanged(nameof(CanCommentsPrev));
        OnPropertyChanged(nameof(CanCommentsNext));
    }

    partial void OnDependencySummaryChanged(string value) => OnPropertyChanged(nameof(HasDependencySummary));

    partial void OnCommentsSummaryChanged(string value) => OnPropertyChanged(nameof(HasCommentsSummary));

    partial void OnCommentsStatusTextChanged(string value) => OnPropertyChanged(nameof(HasCommentsStatusText));

    partial void OnIsSteamSubscribingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggleSteamSubscription));
        OnPropertyChanged(nameof(SteamSubscribeActionText));
    }

    partial void OnIsSteamSubscribedChanged(bool value)
    {
        OnPropertyChanged(nameof(SteamSubscribeActionText));
        OnPropertyChanged(nameof(ShowSteamSubscribeState));
    }

    partial void OnIsSteamStateKnownChanged(bool value) => OnPropertyChanged(nameof(ShowSteamSubscribeState));

    partial void OnIsSteamDirectAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggleSteamSubscription));
        OnPropertyChanged(nameof(SteamSubscribeHint));
        OnPropertyChanged(nameof(HasSteamSubscribeHint));
        OnPropertyChanged(nameof(HasSteamUnavailableReason));
    }

    partial void OnSteamUnavailableReasonChanged(string value) => OnPropertyChanged(nameof(HasSteamUnavailableReason));

    partial void OnIsSteamNeedsUpdateChanged(bool value) => OnPropertyChanged(nameof(ShowSteamUpdateHint));

    partial void OnSteamDownloadProgressTextChanged(string value) =>
        OnPropertyChanged(nameof(HasSteamDownloadProgress));
}
