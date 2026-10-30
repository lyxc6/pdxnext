namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Accounts;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Mods;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Online.Translation;
using PdxLauncher.Core.Storage;

/// <summary>一组共享同一个 <see cref="HttpFetcher"/> 的联网服务。</summary>
/// <param name="Http">连接池属主；重建代理时必须 Dispose 旧的。</param>
/// <param name="PdxHttp">PDX 平台专用传输（PUT/DELETE + JSON 认证头），与 <paramref name="Http"/> 同时重建。</param>
/// <param name="Account">Paradox 账号客户端（登录/续期/账号资料）。</param>
/// <param name="PdxMods">PDX Mods 平台客户端（搜索 / 详情）；与 <paramref name="PdxHttp"/> 同源。</param>
/// <param name="SteamWorkshop">Steam 创意工坊客户端（列表 / 详情 / 能力探测）；匿名 keyless。</param>
/// <param name="WorkshopInfo">创意工坊 mod 信息缓存（同一 id 的元数据 / 封面 / 详情复用）。</param>
/// <param name="WorkshopPages">创意工坊列表页的内存缓存（翻回看过的页不必重抓 SSR 页面）。</param>
/// <param name="Throttle">
/// 出口闸门：按 host 限并发/间隔，被 429 后进入冷却。与 <paramref name="Http"/> 一一对应
/// （闸门只装在普通 HTTP 上；PDX 传输有自己的节奏，且从来没有被 Steam 限流过）。
/// </param>
/// <param name="ModThumbnails">mod 缩略图服务。</param>
/// <param name="TranslationTransport">翻译专用传输（要带自定义认证头，不走普通 <see cref="HttpFetcher"/>）。</param>
/// <param name="TranslationCache">译文缓存（按「引擎 + 目标语言 + 原文」存）。</param>
internal sealed record NetworkServices(
    HttpFetcher Http,
    PdxHttpTransport PdxHttp,
    PdxAccountClient Account,
    PdxModsClient PdxMods,
    SteamWorkshopClient SteamWorkshop,
    WorkshopInfoCache WorkshopInfo,
    WorkshopPageCache WorkshopPages,
    SteamItemPageCache WorkshopItemPages,
    RequestThrottle Throttle,
    MetadataClient Metadata,
    AssetDownloader Assets,
    ThemeResolver Themes,
    DlcDescriptionService Descriptions,
    NewsFeedService NewsFeed,
    IModThumbnailService ModThumbnails,
    TranslationHttpTransport TranslationTransport,
    TranslationCache TranslationCache);

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
        AppSettings settings,
        IJsonDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(documents);

        var proxy = ProxyResolver.Resolve(settings, logger);

        // 出口闸门必须先于所有联网服务建好：HttpFetcher 构造时就把它接上，
        // 之后每个服务共享同一个 HttpFetcher，自然共享同一份冷却状态。
        var throttle = new RequestThrottle(logger, clock);
        var http = new HttpFetcher(logger, proxy, null, throttle);

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
            AppServices.WorkshopCacheRoot,
            new StoreDocumentFile(documents, JsonDocumentKeys.WorkshopInfo));

        // 条目页解析结果缓存：整个流程里最大的响应是 1–3 MB 的 item HTML，
        // 存下解析结果后同一个 mod 重开不再抓页面（详情页一条请求就能出全部内容）。
        var workshopItemPages = new SteamItemPageCache(
            fileSystem,
            logger,
            clock,
            AppServices.WorkshopCacheRoot,
            new StoreDocumentFile(documents, JsonDocumentKeys.WorkshopPages));

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
            new BundledThemes(),
            gameId => new StoreDocumentFile(documents, JsonDocumentKeys.ThemeAssets(gameId)));
        var descriptions = new DlcDescriptionService(
            fileSystem,
            http,
            clock,
            logger,
            AppServices.DataRoot,
            new StoreDocumentFile(documents, JsonDocumentKeys.DlcDescriptions));
        var newsFeed = new NewsFeedService(
            fileSystem,
            http,
            assets,
            clock,
            logger,
            AppServices.NewsCacheRoot,
            new StoreDocumentFile(documents, JsonDocumentKeys.NewsCards));
        var modThumbnails = new ModThumbnailService(
            fileSystem,
            assets,
            clock,
            logger,
            AppServices.ModThumbnailCacheRoot,
            new StoreDocumentFile(documents, JsonDocumentKeys.ModThumbnails));

        // 翻译专用传输：OpenAI 兼容接口要带 Authorization 头，普通 IHttpFetcher 表达不了；
        // 白名单闸门与其它传输共用（用户自填的主机由 TranslationCoordinator 精确授权）。
        var translationTransport = new TranslationHttpTransport(logger, proxy);

        // 译文缓存：与引擎无关，跨引擎切换也能复用同一份文档存储。
        var translationCache = new TranslationCache(documents, clock, logger);
        return new NetworkServices(
            http,
            pdxHttp,
            account,
            pdxMods,
            steamWorkshop,
            workshopInfo,
            workshopPages,
            workshopItemPages,
            throttle,
            metadata,
            assets,
            themes,
            descriptions,
            newsFeed,
            modThumbnails,
            translationTransport,
            translationCache);
    }
}

/// <summary>
/// 两条连接池的属主（普通 HTTP 与 PDX 传输）。
/// <para>
/// 只换连接池、不换服务对象：设置页改代理时调用 <see cref="HttpFetcher.UseProxy"/> /
/// <see cref="PdxHttpTransport.UseProxy"/> 原地换池。页面在构造函数里缓存了
/// <see cref="SteamWorkshopClient"/> / <see cref="PdxModsClient"/> 等对象，换对象会让这些引用
/// 指向已释放的 HttpClient（实测报 <c>ObjectDisposedException: Cannot access a disposed object</c>）。
/// </para>
/// </summary>
public sealed class NetworkLink : IDisposable
{
    public NetworkLink(HttpFetcher http, PdxHttpTransport pdxHttp, TranslationHttpTransport translationHttp)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(pdxHttp);
        ArgumentNullException.ThrowIfNull(translationHttp);

        Http = http;
        PdxHttp = pdxHttp;
        TranslationHttp = translationHttp;
    }

    /// <summary>普通 HTTP 连接池（Steam 工坊、素材、元数据、新闻等共用）。</summary>
    public HttpFetcher Http { get; }

    /// <summary>PDX 平台专用连接池（登录、Mods 搜索/详情）。</summary>
    public PdxHttpTransport PdxHttp { get; }

    /// <summary>翻译专用连接池（要带 Authorization 头，且走独立的代理设置）。</summary>
    public TranslationHttpTransport TranslationHttp { get; }

    public void Dispose()
    {
        foreach (var item in new IDisposable[] { Http, PdxHttp, TranslationHttp })
        {
            try
            {
                item.Dispose();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // 释放连接池失败不该影响退出流程。
            }
        }
    }
}
