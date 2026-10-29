namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods;
using PdxLauncher.Core.Online;

/// <summary>
/// 工坊详情的取数：Steam 走「缓存 → 远端 → 写回缓存」，PDX 走远端并在成功时把正文写回缓存。
/// <para>
/// 这样断网时两种来源都还有正文可看：Steam 靠 <see cref="WorkshopInfoCache.IsDetailFresh"/> 直接复用，
/// PDX 没有独立接口缓存，失败时由调用方用 <see cref="TryGetCachedDescription"/> 兜底。
/// </para>
/// <para>
/// 页面只拿 <see cref="AppResult{T}"/>，失败怎么提示由界面决定，这里不碰任何 UI 类型。
/// </para>
/// </summary>
public sealed class WorkshopDetailSource
{
    private readonly SteamWorkshopClient _steam;
    private readonly PdxModsClient _pdx;
    private readonly AccountService _account;
    private readonly WorkshopInfoCache _info;
    private readonly SteamItemPageCache _itemPages;
    private readonly IPlaySetRepository _playSets;
    private readonly IAppLogger _logger;

    /// <summary>构造。</summary>
    /// <param name="steam">Steam 创意工坊客户端。</param>
    /// <param name="pdx">PDX Mods 客户端。</param>
    /// <param name="account">账号服务（PDX 详情要带登录态）。</param>
    /// <param name="info">工坊信息缓存。</param>
    /// <param name="itemPages">条目页（HTML 解析结果）持久缓存。</param>
    /// <param name="playSets">播放集仓储（依赖检查要读本机 mod 库）。</param>
    /// <param name="logger">日志。</param>
    public WorkshopDetailSource(
        SteamWorkshopClient steam,
        PdxModsClient pdx,
        AccountService account,
        WorkshopInfoCache info,
        SteamItemPageCache itemPages,
        IPlaySetRepository playSets,
        IAppLogger logger)
    {
        _steam = steam;
        _pdx = pdx;
        _account = account;
        _info = info;
        _itemPages = itemPages;
        _playSets = playSets;
        _logger = logger;
    }

    /// <summary>取 Steam 工坊条目详情。</summary>
    /// <param name="id">工坊条目 id。</param>
    /// <param name="fallbackName">缓存里没有名字时的兜底标题。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppResult<SteamWorkshopItemDetail>> GetSteamDetailAsync(
        string id,
        string fallbackName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        // 命中缓存（TTL 内）就不再请求 remote storage：详情正文一次几十 KB，重复打开同一 mod 很常见。
        var cacheKey = WorkshopInfoCache.Key(WorkshopSource.Steam, id);
        if (_info.TryGet(cacheKey, out var cached) &&
            _info.IsDetailFresh(cached) &&
            !string.IsNullOrWhiteSpace(cached.DetailText))
        {
            // 还原逻辑统一放在 SteamWorkshopDetailCache 里：这里以前漏了作者/标签/浏览数，
            // 命中缓存后评论翻页与作者链接就一起失效了。
            return AppResult<SteamWorkshopItemDetail>.Ok(
                SteamWorkshopDetailCache.Restore(cached, id, fallbackName));
        }

        var result = await _steam.GetDetailAsync(id, cancellationToken).ConfigureAwait(true);
        if (result is { IsSuccess: true, Value: { } detail })
        {
            _info.SetDetail(
                cacheKey,
                detail.Description,
                null,
                detail.CreatorSteamId,
                detail.TagList,
                detail.Views,
                detail.LifetimeSubscriptions);
        }

        return result;
    }

    /// <summary>取 PDX Mods 条目详情；成功时把正文写回缓存，供下次断网兜底。</summary>
    /// <param name="id">工坊条目 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppResult<PdxModsDetail>> GetPdxDetailAsync(
        string id,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var result = await _pdx
            .GetDetailAsync(id, _account.Session, cancellationToken)
            .ConfigureAwait(true);

        if (result is { IsSuccess: true, Value: { } detail })
        {
            _info.SetDetail(
                WorkshopInfoCache.Key(WorkshopSource.Pdx, id),
                detail.LongDescription,
                detail.ScreenshotUrls);
        }

        return result;
    }

    /// <summary>
    /// 取 Steam 物品页补充信息（作者主页、描述配图、首屏评论）：缓存 → 远端 → 写回缓存。
    /// <para>
    /// 这是整个流程里最大的一个响应（1–3 MB HTML），所以在 TTL 内重开同一个 mod 直接复用解析结果；
    /// 请求失败时若有旧副本就降级用它（断网仍能看到作者、画廊与首屏评论），由调用方决定怎么提示。
    /// </para>
    /// </summary>
    /// <param name="id">工坊条目 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppResult<SteamWorkshopItemPage>> GetSteamItemPageAsync(
        string id,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        CachedSteamItemPage? cached = null;
        if (_itemPages.TryGet(id, out var hit))
        {
            cached = hit;
        }

        if (cached is not null && _itemPages.IsFresh(cached))
        {
            return AppResult<SteamWorkshopItemPage>.Ok(SteamItemPageCache.Restore(cached));
        }

        var result = await _steam.GetItemPageAsync(id, cancellationToken).ConfigureAwait(true);
        if (result is { IsSuccess: true, Value: { } page })
        {
            _itemPages.Set(id, page);
            return result;
        }

        if (cached is not null)
        {
            _logger.Info("物品页使用缓存：" + id + "（请求失败，改用旧副本）");
            return AppResult<SteamWorkshopItemPage>.Ok(SteamItemPageCache.Restore(cached));
        }

        return result;
    }

    /// <summary>取一页 Steam 评论（翻页接口）。</summary>
    /// <param name="creatorSteamId">条目作者 SteamID64。</param>
    /// <param name="id">工坊条目 id。</param>
    /// <param name="start">起始偏移（0 基）。</param>
    /// <param name="count">本页条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<AppResult<SteamWorkshopCommentPage>> GetSteamCommentsAsync(
        string creatorSteamId,
        string id,
        int start,
        int count,
        CancellationToken cancellationToken) =>
        _steam.GetCommentsAsync(creatorSteamId, id, start, count, cancellationToken);

    /// <summary>
    /// 读本机 mod 库与当前播放集的启用情况，供依赖检查比对。
    /// <para>任何一步读不到都返回「未知」（两个字段为 null），让界面显示「无法判断」而不是误报缺依赖。</para>
    /// </summary>
    /// <param name="game">当前游戏；null 表示没有选中游戏。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<WorkshopLocalMods> LoadLocalModsAsync(
        GameInstallation? game,
        CancellationToken cancellationToken)
    {
        if (game is null)
        {
            return WorkshopLocalMods.Unknown;
        }

        try
        {
            var library = await _playSets.GetModLibraryAsync(game, cancellationToken).ConfigureAwait(true);
            var active = await _playSets.GetActivePlaySetAsync(game, cancellationToken).ConfigureAwait(true);
            if (active is null)
            {
                return new WorkshopLocalMods(library, new HashSet<Guid>());
            }

            var mods = await _playSets
                .GetPlaySetModsAsync(game, active.Id, cancellationToken)
                .ConfigureAwait(true);

            var enabled = mods
                .Where(static mod => mod.Enabled)
                .Select(static mod => mod.ModId)
                .ToHashSet();

            return new WorkshopLocalMods(library, enabled);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("读取本机 mod 库失败，依赖状态按未知处理: " + ex.Message);
            return WorkshopLocalMods.Unknown;
        }
    }

    /// <summary>上次抓到过的详情正文；没有就返回 null（断网兜底用）。</summary>
    /// <param name="source">来源。</param>
    /// <param name="id">工坊条目 id。</param>
    public string? TryGetCachedDescription(WorkshopSource source, string id)
    {
        var cacheKey = WorkshopInfoCache.Key(source, id);
        if (!_info.TryGet(cacheKey, out var cached) || string.IsNullOrWhiteSpace(cached.DetailText))
        {
            return null;
        }

        _logger.Info("工坊详情使用缓存正文：" + id);
        return cached.DetailText;
    }
}

/// <summary>本机 mod 库的快照：字段为 null 表示读不到（依赖状态只能显示「未知」）。</summary>
/// <param name="Installed">已安装的 mod 行。</param>
/// <param name="Enabled">当前活动播放集里启用中的 mod id。</param>
public sealed record WorkshopLocalMods(
    IReadOnlyList<ModLibraryEntry>? Installed,
    IReadOnlySet<Guid>? Enabled)
{
    /// <summary>读不到任何本机信息。</summary>
    public static WorkshopLocalMods Unknown { get; } = new(null, null);
}
