namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Online;

/// <summary>一组共享同一个 <see cref="HttpFetcher"/> 的联网服务。</summary>
/// <param name="Http">连接池属主；重建代理时必须 Dispose 旧的。</param>
internal sealed record NetworkServices(
    HttpFetcher Http,
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
        return new NetworkServices(http, metadata, assets, themes, descriptions, newsFeed, modThumbnails);
    }
}
