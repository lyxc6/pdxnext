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

/// <summary>
/// 「创意工坊 mod 详情」子页的视图模型：先把列表行已有的元数据摆上去（名字/作者/评分立刻可见），
/// 再异步补详情正文、截图、外链。原来这些内容挤在一个 MaxHeight=560 的 ContentDialog 里，
/// 长描述根本看不全，现在改成整页滚动。
/// <para>
/// 两种来源的字段差异在这里抹平：Steam 只有描述/体积/标签，PDX 还有更新日志、截图、外链与论坛帖。
/// 页面只认这里的属性，不判断来源。
/// </para>
/// <para>
/// 本类不引用任何 Microsoft.UI.Xaml 类型。
/// </para>
/// </summary>
public sealed partial class WorkshopDetailViewModel : LocalizedObservableObject
{
    /// <summary>截图并发下载上限：详情页没有列表那种 6 张封面的压力，2 张够用又不抢带宽。</summary>
    private const int MaxConcurrentScreenshots = 2;

    private readonly WorkshopDetailSource _source;
    private readonly WorkshopImageCache _images;
    private readonly IAppLogger _logger;

    /// <summary>构造。</summary>
    /// <param name="source">详情取数。</param>
    /// <param name="images">图片缓存（封面/截图落地）。</param>
    /// <param name="logger">日志。</param>
    public WorkshopDetailViewModel(
        WorkshopDetailSource source,
        WorkshopImageCache images,
        IAppLogger logger)
    {
        _source = source;
        _images = images;
        _logger = logger;
    }

    /// <summary>当前展示的 mod 行；null 表示还没加载。</summary>
    public WorkshopRowViewModel? Row { get; private set; }

    /// <summary>标题（mod 名称）。</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>来源徽标文案（Steam 创意工坊 / Paradox Mods）。</summary>
    [ObservableProperty]
    private string _sourceLabel = string.Empty;

    /// <summary>作者一行。</summary>
    [ObservableProperty]
    private string _authorText = string.Empty;

    /// <summary>评分 / 订阅 / 收藏 / 浏览 / 体积 / 更新时间拼成的一行。</summary>
    [ObservableProperty]
    private string _metaText = string.Empty;

    /// <summary>发布时间（取不到时为空串）。</summary>
    [ObservableProperty]
    private string _createdText = string.Empty;

    /// <summary>简介正文（已清成纯文本）。</summary>
    [ObservableProperty]
    private string _descriptionText = string.Empty;

    /// <summary>更新日志正文（仅 PDX 有）。</summary>
    [ObservableProperty]
    private string _changelogText = string.Empty;

    /// <summary>标签拼成的单行文本。</summary>
    [ObservableProperty]
    private string _tagsText = string.Empty;

    /// <summary>封面本地路径。</summary>
    [ObservableProperty]
    private string? _coverPath;

    /// <summary>错误提示文案。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>正文来自缓存（断网兜底）时的提示；空串表示没有这回事。</summary>
    [ObservableProperty]
    private string _cachedHint = string.Empty;

    /// <summary>是否正在读取详情。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>是否已成功读到详情。</summary>
    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>是否读取失败（此时展示 <see cref="ErrorText"/> 与「重试」）。</summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>当前账号是否已订阅（仅 PDX 有意义）。</summary>
    [ObservableProperty]
    private bool _isInstalled;

    /// <summary>截图列表。</summary>
    public ObservableCollection<WorkshopScreenshotViewModel> Screenshots { get; } = [];

    /// <summary>外链列表（含论坛帖）。</summary>
    public ObservableCollection<WorkshopLinkViewModel> Links { get; } = [];

    /// <summary>封面是否就绪。</summary>
    public bool HasCover => !string.IsNullOrWhiteSpace(CoverPath);

    /// <summary>简介是否有内容。</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(DescriptionText);

    /// <summary>简介是否为空（读到详情后才提示「作者没有填写简介」）。</summary>
    public bool ShowNoDescription => IsLoaded && !HasError && !HasDescription;

    /// <summary>更新日志是否有内容。</summary>
    public bool HasChangelog => !string.IsNullOrWhiteSpace(ChangelogText);

    /// <summary>是否有标签。</summary>
    public bool HasTags => !string.IsNullOrWhiteSpace(TagsText);

    /// <summary>是否有截图。</summary>
    public bool HasScreenshots => Screenshots.Count > 0;

    /// <summary>是否有外链。</summary>
    public bool HasLinks => Links.Count > 0;

    /// <summary>是否有发布时间。</summary>
    public bool HasCreated => !string.IsNullOrWhiteSpace(CreatedText);

    /// <summary>是否有缓存兜底提示。</summary>
    public bool HasCachedHint => !string.IsNullOrWhiteSpace(CachedHint);

    /// <summary>是否显示「已订阅」徽标。</summary>
    public bool ShowInstalled => IsInstalled;

    /// <summary>浏览器地址。</summary>
    public string WebUrl => Row?.WebUrl ?? string.Empty;

    /// <summary>Steam 客户端协议地址（仅 Steam 来源）。</summary>
    public string? SteamUrl => Row?.SteamUrl;

    /// <summary>是否可「在 Steam 中打开」。</summary>
    public bool CanOpenSteam => Row?.CanOpenSteam ?? false;

    /// <summary>是否有可打开的浏览器地址（决定页头按钮是否可用）。</summary>
    public bool HasWebUrl => !string.IsNullOrWhiteSpace(WebUrl);

    /// <summary>加载指定行；重复调用会先清空上一次的内容。</summary>
    /// <param name="row">创意工坊列表行。</param>
    /// <param name="cancellationToken">取消令牌（页面离开时取消）。</param>
    public async Task LoadAsync(WorkshopRowViewModel row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        Row = row;
        ResetForLoad(row);
        NotifyStateChanged();

        try
        {
            await LoadCoverAsync(row, cancellationToken).ConfigureAwait(true);

            if (row.Source == WorkshopSource.Steam)
            {
                var result = await _source
                    .GetSteamDetailAsync(row.Id, row.Name, cancellationToken)
                    .ConfigureAwait(true);
                ApplySteam(row, result);
            }
            else
            {
                var result = await _source
                    .GetPdxDetailAsync(row.Id, cancellationToken)
                    .ConfigureAwait(true);
                await ApplyPdxAsync(row, result, cancellationToken).ConfigureAwait(true);
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
            Fail(ex.Message);
        }
        finally
        {
            IsLoading = false;
            NotifyStateChanged();
        }
    }

    /// <summary>重新加载当前行（「重试」按钮）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task ReloadAsync(CancellationToken cancellationToken) =>
        Row is null ? Task.CompletedTask : LoadAsync(Row, cancellationToken);

    /// <summary>下行前把上一次的内容清掉，避免看到上一个 mod 的正文。</summary>
    private void ResetForLoad(WorkshopRowViewModel row)
    {
        Title = row.Name;
        SourceLabel = row.Source == WorkshopSource.Steam
            ? T(Keys.WorkshopSourceSteam)
            : T(Keys.WorkshopSourcePdx);
        AuthorText = row.AuthorText;
        MetaText = row.SubtitleText;
        CreatedText = string.Empty;
        DescriptionText = string.Empty;
        ChangelogText = string.Empty;
        TagsText = string.Empty;
        ErrorText = string.Empty;
        CachedHint = string.Empty;
        IsInstalled = false;
        HasError = false;
        IsLoaded = false;
        IsLoading = true;

        // 列表里已经下好的封面先直接用，别在详情页再下一次。
        CoverPath = row.CoverPath;

        Screenshots.Clear();
        Links.Clear();
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

    /// <summary>套用 Steam 详情（只有描述/体积/标签，没有截图与外链）。</summary>
    private void ApplySteam(WorkshopRowViewModel row, AppResult<SteamWorkshopItemDetail> result)
    {
        if (result is not { IsSuccess: true, Value: { } detail })
        {
            UseCachedDescription(row, result.ErrorMessage);
            return;
        }

        var item = row.SteamItem;
        DescriptionText = WorkshopText.ToPlainText(detail.Description);
        MetaText = BuildSteamMeta(item, detail);

        // 缓存命中的 CreatedUtc 是「抓取时间」，不是发布时间，所以只认列表条目里的真实值。
        CreatedText = item is { } steamItem && steamItem.CreatedUtc > DateTimeOffset.UnixEpoch
            ? T(Keys.WorkshopDetailCreated, WorkshopText.FormatDate(steamItem.CreatedUtc))
            : string.Empty;
        TagsText = item is null
            ? string.Empty
            : WorkshopText.Join(item.Tags.Select(static tag => tag.DisplayName), " · ");
    }

    /// <summary>套用 PDX 详情：正文 + 更新日志 + 截图 + 外链 + 论坛帖。</summary>
    private async Task ApplyPdxAsync(
        WorkshopRowViewModel row,
        AppResult<PdxModsDetail> result,
        CancellationToken cancellationToken)
    {
        if (result is not { IsSuccess: true, Value: { } detail })
        {
            UseCachedDescription(row, result.ErrorMessage);
            return;
        }

        DescriptionText = WorkshopText.ToPlainText(detail.LongDescription);
        ChangelogText = WorkshopText.ToPlainText(detail.Changelog);
        MetaText = BuildPdxMeta(detail);
        CreatedText = detail.Summary.CreatedOn is { } created && created > DateTimeOffset.UnixEpoch
            ? T(Keys.WorkshopDetailCreated, WorkshopText.FormatDate(created))
            : string.Empty;
        TagsText = WorkshopText.Join(detail.Summary.Tags, " · ");
        IsInstalled = detail.IsInstalled;

        BuildLinks(detail);

        Screenshots.Clear();
        for (var index = 0; index < detail.ScreenshotUrls.Count; index++)
        {
            Screenshots.Add(new WorkshopScreenshotViewModel(index, detail.ScreenshotUrls[index]));
        }

        NotifyStateChanged();
        await DownloadScreenshotsAsync(row, detail, cancellationToken).ConfigureAwait(true);
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

        DescriptionText = WorkshopText.ToPlainText(cached);
        CachedHint = T(Keys.WorkshopDetailCached);
        IsLoaded = true;
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
        var parts = new List<string?>
        {
            item is null || item.TotalVotes <= 0 || item.StarRating <= 0
                ? null
                : T(Keys.WorkshopRating, item.StarRating.ToString("0.##", CultureInfo.InvariantCulture), item.TotalVotes),
            item is null || item.Subscriptions <= 0
                ? null
                : T(Keys.WorkshopSubscriptions, item.Subscriptions.ToString("N0", CultureInfo.CurrentCulture)),
            item is null || item.Favorites <= 0
                ? null
                : T(Keys.WorkshopDetailFavorites, item.Favorites.ToString("N0", CultureInfo.CurrentCulture)),
            item is null || item.Views <= 0
                ? null
                : T(Keys.WorkshopDetailViews, item.Views.ToString("N0", CultureInfo.CurrentCulture)),
            detail.FileSizeBytes <= 0
                ? null
                : T(Keys.WorkshopSize, SizeFormatter.Format(detail.FileSizeBytes)),
            detail.UpdatedUtc > DateTimeOffset.UnixEpoch
                ? T(Keys.WorkshopUpdated, WorkshopText.FormatDate(detail.UpdatedUtc))
                : null,
        };

        return WorkshopText.Join(parts, " · ");
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

    /// <summary>截图逐张下载（最多 2 张并发），每张完成即刷新对应项。</summary>
    private async Task DownloadScreenshotsAsync(
        WorkshopRowViewModel row,
        PdxModsDetail detail,
        CancellationToken cancellationToken)
    {
        if (Screenshots.Count == 0)
        {
            return;
        }

        using var gate = new SemaphoreSlim(MaxConcurrentScreenshots);
        var items = Screenshots.ToList();

        await Task.WhenAll(items.Select(async shot =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                var index = shot.Index;
                var url = index < detail.ScreenshotUrls.Count ? detail.ScreenshotUrls[index] : shot.Url;
                shot.Path = await _images
                    .EnsureScreenshotAsync(row.Source, row.Id, url, index, cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单张截图失败不该让整页报错，留占位即可。
                _logger.Warn("创意工坊截图下载失败: " + row.Id + " #" + shot.Index + " " + ex.Message);
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
        OnPropertyChanged(nameof(HasLinks));
        OnPropertyChanged(nameof(HasCreated));
        OnPropertyChanged(nameof(HasCachedHint));
        OnPropertyChanged(nameof(ShowInstalled));
        OnPropertyChanged(nameof(WebUrl));
        OnPropertyChanged(nameof(HasWebUrl));
        OnPropertyChanged(nameof(SteamUrl));
        OnPropertyChanged(nameof(CanOpenSteam));
    }

    partial void OnCoverPathChanged(string? value) => OnPropertyChanged(nameof(HasCover));

    partial void OnDescriptionTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(ShowNoDescription));
    }

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(ShowInstalled));

    partial void OnCachedHintChanged(string value) => OnPropertyChanged(nameof(HasCachedHint));

    partial void OnIsLoadedChanged(bool value) => OnPropertyChanged(nameof(ShowNoDescription));
}
