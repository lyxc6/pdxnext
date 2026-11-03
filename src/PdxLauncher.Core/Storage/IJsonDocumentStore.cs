namespace PdxLauncher.Core.Storage;

/// <summary>
/// 「整份 JSON 文档」的存储抽象：设置、登录态、各缓存索引都按这一形态读写。
/// <para>
/// 这些数据的既有实现都是「整份加载 → 内存态 → 整份落盘」，拆成关系列只会把
/// <c>AppSettings</c> 40+ 字段与嵌套 model 全部重写一遍，风险与收益不成比例。
/// 领域数据（mod、播放集）才用 <see cref="PdxDatabase"/> 里的真表。
/// </para>
/// <para>实现一律容错：读失败/缺键/JSON 坏掉都当「没有」，写失败只记日志，不抛给 UI。</para>
/// </summary>
public interface IJsonDocumentStore
{
    /// <summary>读文档；键不存在或读取失败返回 false。</summary>
    bool TryRead(string key, out string? json);

    /// <summary>写文档（整份覆盖）；返回是否写成功。</summary>
    bool Write(string key, string json);

    /// <summary>删文档；键本来就不存在也算成功。</summary>
    bool Delete(string key);

    /// <summary>键是否存在。</summary>
    bool Exists(string key);

    /// <summary>某个键前缀下所有文档的 JSON 字节数之和（用于设置页的缓存占用统计）。</summary>
    long MeasureBytes(string prefix);

    /// <summary>列出某个键前缀下的全部键（升序）。</summary>
    IReadOnlyList<string> List(string prefix);
}

/// <summary>
/// 文档键的统一命名：<c>settings</c> / <c>session</c> 是单行表，其余一律 <c>cache:&lt;名字&gt;[:&lt;gameId&gt;]</c>。
/// </summary>
public static class JsonDocumentKeys
{
    /// <summary>应用设置（<c>settings</c> 表第 1 行）。</summary>
    public const string Settings = "settings";

    /// <summary>PDX 登录态（<c>session</c> 表第 1 行）。</summary>
    public const string Session = "session";

    /// <summary>缓存键前缀。</summary>
    public const string CachePrefix = "cache:";

    /// <summary>Steam 创意工坊详情缓存索引。</summary>
    public const string WorkshopInfo = CachePrefix + "workshop-info";

    /// <summary>Steam 工坊物品页正文缓存索引。</summary>
    public const string WorkshopPages = CachePrefix + "workshop-pages";

    /// <summary>新闻卡片缓存索引。</summary>
    public const string NewsCards = CachePrefix + "news-cards";

    /// <summary>DLC 简介缓存索引。</summary>
    public const string DlcDescriptions = CachePrefix + "dlc-descriptions";

    /// <summary>Mod 缩略图下载索引。</summary>
    public const string ModThumbnails = CachePrefix + "mod-thumbnails";

    /// <summary>译文缓存索引。</summary>
    public const string Translations = CachePrefix + "translations";

    /// <summary>
    /// 页面状态（Mod 库的搜索/排序/滚动、Mod 创建页的面板与表单草稿）。
    /// 不是缓存，删了只丢「上次看到哪儿」，所以不走 <see cref="CachePrefix"/>。
    /// </summary>
    public const string UiPageState = "ui:page-state";

    /// <summary>主题资源解析结果缓存索引（按游戏分开）。</summary>
    public static string ThemeAssets(string gameId) =>
        CachePrefix + "theme-assets:" + (gameId ?? string.Empty).Trim();

    /// <summary>是否是「单行表」保留键（走 settings/session 表，而不是 documents）。</summary>
    public static bool IsSingleton(string key) =>
        string.Equals(key, Settings, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, Session, StringComparison.OrdinalIgnoreCase);
}
