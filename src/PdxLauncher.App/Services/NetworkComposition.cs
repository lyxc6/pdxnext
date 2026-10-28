namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Accounts;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Mods;
using PdxLauncher.Core.Online;

/// <summary>一组共享同一个 <see cref="HttpFetcher"/> 的联网服务。</summary>
/// <param name="Http">连接池属主；重建代理时必须 Dispose 旧的。</param>
/// <param name="PdxHttp">PDX 平台专用传输（PUT/DELETE + JSON 认证头），与 <paramref name="Http"/> 同时重建。</param>
/// <param name="Account">Paradox 账号客户端（登录/续期/账号资料）。</param>
/// <param name="PdxMods">PDX Mods 平台客户端（搜索 / 详情）；与 <paramref name="PdxHttp"/> 同源。</param>
/// <param name="SteamWorkshop">Steam 创意工坊客户端（列表 / 详情 / 能力探测）；匿名 keyless。</param>
/// <param name="WorkshopInfo">创意工坊 mod 信息缓存（同一 id 的元数据 / 封面 / 详情复用）。</param>
/// <param name="WorkshopPages">创意工坊列表页的内存缓存（翻回看过的页不必重抓 SSR 页面）。</param>
internal sealed record NetworkServices(
    HttpFetcher Http,
    PdxHttpTransport PdxHttp,
    PdxAccountClient Account,
    PdxModsClient PdxMods,
    SteamWorkshopClient SteamWorkshop,
    WorkshopInfoCache WorkshopInfo,
    WorkshopPageCache WorkshopPages,
    MetadataClient Metadata,
    AssetDownloader Assets,
    ThemeResolver Themes,
    DlcDescriptionService Descriptions,
    NewsFeedService NewsFeed,
    IModThumbnailService ModThumbnails);

/// <summary>
/// 联网服务的装配点。集中在一处的原因：设置页改代理地址后要整条链路重建
/// （<see cref="HttpFetcher"/> 的 SocketsHttpHandler 在构造时固定了代理），
/// 不能让 App.xaml.cs 与设置页各写一份装配顺序。
/// </summary>
internal static class NetworkComposition
{
    internal static NetworkServices Create(
        IFileSystem fileSystem,
        IAppLogger logger,
        IClock clock,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(settings);

        var proxy = ProxyResolver.Resolve(settings, logger);
        var http = new HttpFetcher(logger, proxy);

        // PDX 平台（登录 + mods）用的是 PUT/DELETE + JSON 认证头，IHttpFetcher 表达不了；
        // 单独一条传输，但共用同一份代理决策，代理改了两条一起换。
        var pdxHttp = new PdxHttpTransport(logger, proxy);
        var account = new PdxAccountClient(pdxHttp, logger, clock);

        // PDX Mods 平台（搜索/详情）与账号客户端共用同一条 PDX 传输：同一个代理、
        // 同一份白名单，代理改了自然也一起换。
        var pdxMods = new PdxModsClient(pdxHttp, logger);

        // Steam 创意工坊走普通 IHttpFetcher：匿名公开页 + keyless 接口，不需要认证头。
        var steamWorkshop = new SteamWorkshopClient(http, logger);

        // 工坊 mod 信息缓存：按「来源 + id」存元数据与详情，封面文件落在同一个目录下。
        var workshopPages = new WorkshopPageCache(clock);
        var workshopInfo = new WorkshopInfoCache(
            fileSystem,
            logger,
            clock,
            AppServices.WorkshopCacheRoot);

        var metadata = new MetadataClient(http, logger);
        var assets = new AssetDownloader(http, logger, fileSystem);
        var steamCovers = new SteamCoverUrlResolver(http, logger);
        var themes = new ThemeResolver(
            fileSystem,
            clock,
            logger,
            metadata,
            assets,
            AppServices.ThemeCacheRoot,
            steamCovers,
            new BundledThemes());
        var descriptions = new DlcDescriptionService(fileSystem, http, clock, logger, AppServices.DataRoot);
        var newsFeed = new NewsFeedService(fileSystem, http, assets, clock, logger, AppServices.NewsCacheRoot);
        var modThumbnails = new ModThumbnailService(fileSystem, assets, clock, logger, AppServices.ModThumbnailCacheRoot);
        return new NetworkServices(
            http,
            pdxHttp,
            account,
            pdxMods,
            steamWorkshop,
            workshopInfo,
            workshopPages,
            metadata,
            assets,
            themes,
            descriptions,
            newsFeed,
            modThumbnails);
    }
}

/// <summary>
/// 把多个 <see cref="IDisposable"/> 合成一个：代理重建时一次性释放旧链路里的
/// <see cref="HttpFetcher"/> 与 <see cref="PdxHttpTransport"/>（各自持有连接池）。
/// </summary>
internal sealed class DisposableGroup : IDisposable
{
    private readonly IDisposable[] _items;

    internal DisposableGroup(params IDisposable[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
    }

    public void Dispose()
    {
        foreach (var item in _items)
        {
            try
            {
                item.Dispose();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // 释放连接池失败不该影响重建流程。
            }
        }
    }
}
