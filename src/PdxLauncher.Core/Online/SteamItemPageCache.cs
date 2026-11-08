namespace PdxLauncher.Core.Online;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 创意工坊条目页（HTML）解析结果的持久缓存：按 publishedfileid 存作者、描述、内嵌配图、
/// 画廊截图与首屏评论。
/// <para>
/// 为什么值得单独存：条目页是整个流程里最大的一个响应（1–3 MB HTML），而解析出来的东西只有几十 KB。
/// 每次打开详情都重抓一遍既慢又最容易被 Steam 限流；存下解析结果后，同一个 mod 在 TTL 内重开
/// （包括断网时）不再发请求，详情页立刻可用。
/// </para>
/// <para>
/// v2 起数据落在自有库的 <c>workshop_pages</c> 真表（<see cref="IWorkshopPageStore"/>），
/// 一页一行；单条过大仍只记日志、不落库。缺失或损坏一律视为无缓存，绝不把异常抛给调用方。
/// </para>
/// </summary>
public sealed class SteamItemPageCache
{
    /// <summary>旧索引文件名（v1 遗留，仅用于识别与清理，不再写入）。</summary>
    public const string IndexFileName = "workshop-pages.json";

    /// <summary>条目页的默认新鲜期；超过后下次打开详情会重新抓取。</summary>
    public static readonly TimeSpan DefaultPageTtl = TimeSpan.FromHours(12);

    /// <summary>
    /// 最多保留的条目数；超出后按抓取时间淘汰最旧的。
    /// 条目页是整页正文，最坏情况按单条上限算：150 × 128KB ≈ 19MB。
    /// </summary>
    public const int MaxEntries = 150;

    /// <summary>单条序列化后超过这个字节数就不落库（描述里塞满 base64 图片的极端条目）。</summary>
    public const int MaxEntryBytes = 128 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IAppLogger _logger;
    private readonly IClock _clock;
    private readonly string _cacheRoot;
    private readonly IWorkshopPageStore _store;

    /// <summary>构造。</summary>
    /// <param name="fileSystem">文件系统抽象（保留参数以兼容旧装配）。</param>
    /// <param name="logger">日志。</param>
    /// <param name="clock">时钟（TTL 判定与新鲜度记账）。</param>
    /// <param name="cacheRoot">缓存根目录（与创意工坊信息缓存同根，清缓存时一起删掉）。</param>
    /// <param name="store">条目页持久层。</param>
    public SteamItemPageCache(
        IFileSystem fileSystem,
        IAppLogger logger,
        IClock clock,
        string cacheRoot,
        IWorkshopPageStore store)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        ArgumentNullException.ThrowIfNull(store);
        _logger = logger;
        _clock = clock;
        _cacheRoot = cacheRoot;
        _store = store;
    }

    /// <summary>缓存根目录。</summary>
    public string CacheRoot => _cacheRoot;

    /// <summary>缓存里的条目数。</summary>
    public int Count => _store.Count;

    /// <summary>取条目页缓存；没有则返回 false。</summary>
    /// <param name="publishedFileId">工坊条目 id。</param>
    /// <param name="entry">命中的缓存条目（副本）。</param>
    public bool TryGet(string publishedFileId, out CachedSteamItemPage entry)
    {
        var found = _store.Find(publishedFileId);
        if (found is null)
        {
            entry = null!;
            return false;
        }

        entry = found;
        return true;
    }

    /// <summary>条目是否还在新鲜期内（在期内直接用，不再发请求）。</summary>
    /// <param name="entry">缓存条目。</param>
    public bool IsFresh(CachedSteamItemPage entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _clock.UtcNow - entry.FetchedUtc <= DefaultPageTtl;
    }

    /// <summary>写入一条条目页解析结果；单条过大只记日志、不落库。</summary>
    /// <param name="publishedFileId">工坊条目 id。</param>
    /// <param name="page">条目页解析结果。</param>
    public void Set(string publishedFileId, SteamWorkshopItemPage page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedFileId);
        ArgumentNullException.ThrowIfNull(page);

        var entry = new CachedSteamItemPage
        {
            FetchedUtc = _clock.UtcNow,
            AuthorName = page.AuthorName,
            AuthorProfileUrl = page.AuthorProfileUrl,
            DescriptionHtml = page.DescriptionHtml,
            ImageUrls = [.. page.ImageUrls],
            ScreenshotUrls = [.. page.ScreenshotUrls],
            TotalComments = page.TotalComments,
            Comments = [.. page.Comments],
            CollectionItems = [.. page.CollectionItemList],
        };

        if (EstimateBytes(entry) > MaxEntryBytes)
        {
            _logger.Info("创意工坊条目页过大（超过 " + (MaxEntryBytes / 1024) + " KB），不落库：" + publishedFileId);
            return;
        }

        _store.Set(publishedFileId, entry);

        // 真表存储下没有内存字典兜底，超限必须当场淘汰，否则库会一直长。
        if (_store.Count > MaxEntries)
        {
            _store.Trim(MaxEntries);
        }
    }

    /// <summary>清空缓存。删除失败只记 Warn。</summary>
    public void Clear() => _store.Clear();

    /// <summary>兼容旧调用：v2 起每条写入即落库，这里无事可做。</summary>
    public void Flush()
    {
        // 真表存储下没有内存脏页，保留空实现避免调用点大改。
    }

    /// <summary>把缓存条目还原成解析结果（纯函数，供详情页直接使用）。</summary>
    /// <param name="entry">缓存条目。</param>
    public static SteamWorkshopItemPage Restore(CachedSteamItemPage entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new SteamWorkshopItemPage(
            entry.AuthorName,
            entry.AuthorProfileUrl,
            entry.DescriptionHtml,
            entry.ImageUrls,
            entry.ScreenshotUrls,
            entry.TotalComments,
            entry.Comments,
            entry.CollectionItems);
    }

    private static int EstimateBytes(CachedSteamItemPage entry) =>
        JsonSerializer.SerializeToUtf8Bytes(entry, Options).Length;
}
