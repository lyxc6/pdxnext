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
    private readonly IAppLogger _logger;

    /// <summary>构造。</summary>
    /// <param name="steam">Steam 创意工坊客户端。</param>
    /// <param name="pdx">PDX Mods 客户端。</param>
    /// <param name="account">账号服务（PDX 详情要带登录态）。</param>
    /// <param name="info">工坊信息缓存。</param>
    /// <param name="logger">日志。</param>
    public WorkshopDetailSource(
        SteamWorkshopClient steam,
        PdxModsClient pdx,
        AccountService account,
        WorkshopInfoCache info,
        IAppLogger logger)
    {
        _steam = steam;
        _pdx = pdx;
        _account = account;
        _info = info;
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
            return AppResult<SteamWorkshopItemDetail>.Ok(new SteamWorkshopItemDetail(
                id,
                cached.Name ?? fallbackName,
                cached.DetailText!,
                cached.CoverUrl,
                cached.SizeBytes ?? 0,
                cached.FetchedUtc ?? DateTimeOffset.UtcNow,
                cached.UpdatedUtc ?? DateTimeOffset.UtcNow,
                null));
        }

        var result = await _steam.GetDetailAsync(id, cancellationToken).ConfigureAwait(true);
        if (result is { IsSuccess: true, Value: { } detail })
        {
            _info.SetDetail(cacheKey, detail.Description, null);
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
