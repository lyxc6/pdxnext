namespace PdxLauncher.Core.Online;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Storage;

/// <summary>
/// 主题缓存索引，文件名固定 &lt;gameId&gt;.assets.json，位于 ThemeCacheRoot 下。
/// 作用：让 allowOnlineRefresh=false 的冷启动也能知道「哪个缓存文件对应哪个素材键」，
/// 以及记录上次检查时间供设置页展示。读取必须容错：缺失/损坏一律视为无缓存。
/// </summary>
public sealed class AssetIndex
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>素材键（appIcon/background/...）到缓存条目的映射。</summary>
    public Dictionary<string, AssetIndexEntry> Assets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 上次成功联网检查该游戏主题的时间（UTC）。ThemeResolver 用它判 TTL（5 天），
    /// 不用缓存文件 mtime —— 因为「URL 未变就跳过下载」会让 mtime 永远不更新。
    /// </summary>
    public DateTimeOffset? LastCheckedUtc { get; set; }

    /// <summary>
    /// 上次刷新有素材没下下来时记录的「最早可重试时间」（UTC）。
    /// 与 LastCheckedUtc 的区别：LastCheckedUtc 只代表**全部素材都就绪**的那次检查；
    /// 有失败时它不前移，改由本字段安排 10 分钟后的短重试，避免一次网络抖动把该游戏主题锁死 5 天。
    /// 为 null 表示没有待重试的失败。
    /// </summary>
    public DateTimeOffset? RetryAfterUtc { get; set; }

    /// <summary>读取索引；文件缺失、JSON 损坏或 IO 失败时返回空索引并记 warn，绝不抛。</summary>
    public static AssetIndex Load(IFileSystem fs, IAppLogger logger, string path)
    {
        ArgumentNullException.ThrowIfNull(fs);
        return Load(new FileDocumentFile(fs, path), logger);
    }

    /// <summary>从任意文档落点读取索引（自有库里是 <c>cache:theme-assets:&lt;gameId&gt;</c>）。</summary>
    public static AssetIndex Load(IDocumentFile document, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(logger);

        var json = document.ReadText();
        if (json is null)
        {
            return new AssetIndex();
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<AssetIndex>(json, Options);
            if (loaded is null)
            {
                return new AssetIndex();
            }

            // 反序列化后的 Dictionary 会丢掉 OrdinalIgnoreCase 比较器，这里重建一份。
            // JSON 里显式写 "assets": null 时反序列化结果也会是 null，必须兜住。
            var normalized = new Dictionary<string, AssetIndexEntry>(StringComparer.OrdinalIgnoreCase);
            if (loaded.Assets is null)
            {
                loaded.Assets = normalized;
                return loaded;
            }

            foreach (var pair in loaded.Assets)
            {
                if (pair.Value is not null && !string.IsNullOrEmpty(pair.Key))
                {
                    normalized[pair.Key] = pair.Value;
                }
            }

            loaded.Assets = normalized;
            return loaded;
        }
        catch (JsonException ex)
        {
            logger.Warn("主题缓存索引损坏，视为无缓存: " + ex.Message);
            return new AssetIndex();
        }
        catch (IOException ex)
        {
            logger.Warn("主题缓存索引读取失败，视为无缓存: " + ex.Message);
            return new AssetIndex();
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.Warn("主题缓存索引无权限读取，视为无缓存: " + ex.Message);
            return new AssetIndex();
        }
    }

    /// <summary>原子写索引；写入失败只记日志，不影响主题解析。</summary>
    public void Save(IFileSystem fs, IAppLogger logger, string path)
    {
        ArgumentNullException.ThrowIfNull(fs);
        Save(new FileDocumentFile(fs, path), logger);
    }

    /// <summary>写到任意文档落点。</summary>
    public void Save(IDocumentFile document, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            if (!document.WriteText(JsonSerializer.Serialize(this, Options)))
            {
                logger.Warn("主题缓存索引写入失败: " + document.DisplayPath);
            }
        }
        catch (IOException ex)
        {
            logger.Warn("主题缓存索引写入失败: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.Warn("主题缓存索引无权限写入: " + ex.Message);
        }
    }

    /// <summary>按键查找条目；不存在返回 null。</summary>
    public AssetIndexEntry? Find(string key) =>
        Assets.TryGetValue(key, out var entry) ? entry : null;

    /// <summary>记录一次成功下载。</summary>
    public void Set(string key, string url, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Assets[key] = new AssetIndexEntry { Url = url, FileName = fileName };
    }
}
