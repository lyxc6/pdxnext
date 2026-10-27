namespace PdxLauncher.Core.Abstractions;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 官方公告服务（Braze content cards）。
/// <para>返回的卡片只用于主页底部的展示，属于弱依赖：接口失败时返回上次缓存或空结果，绝不抛给调用方。</para>
/// </summary>
public interface INewsFeedService
{
    /// <summary>卡片缓存文件路径（%LOCALAPPDATA%\PdxNext\news-cache\news-cards.json）。</summary>
    string CachePath { get; }

    /// <summary>
    /// 清空本地公告缓存（缓存文件与公告图目录），并丢弃内存里已加载的那份。
    /// <para>供设置页「清除缓存」使用：删除失败只记 Warn，不抛给调用方。</para>
    /// </summary>
    void ClearCache();

    /// <summary>
    /// 取一门游戏的官方公告卡。
    /// <para><paramref name="allowOnline"/> 为 false 时绝不发起任何网络请求，只返回本地缓存。</para>
    /// <para>缓存 6 小时过期；过期但仍有内容的旧缓存会先返回，联网失败时继续沿用。</para>
    /// <para>卡片拿到了但公告图没下载成功时不算过期成功：不推进 6 小时 TTL，5 分钟后自动重试补图。</para>
    /// </summary>
    Task<NewsFeed> GetAsync(GameInstallation game, bool allowOnline, CancellationToken cancellationToken);
}
