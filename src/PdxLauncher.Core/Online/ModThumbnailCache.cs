namespace PdxLauncher.Core.Online;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Storage;

/// <summary>
/// mod 缩略图的磁盘缓存：索引 mod-thumbnails.json + 图片目录 mod-thumbnails/。
/// <para>
/// 键是 mods.id（<see cref="Guid"/>），不是 URL —— 创意工坊的图片地址会随作者更新变化，
/// 用 mod id 才能让「换了一张图」仍然命中同一个缓存槽位并覆盖旧文件。
/// </para>
/// <para>
/// 读取容错与主题缓存一致：索引缺失/损坏一律视为无缓存，绝不抛异常。
/// </para>
/// </summary>
public sealed class ModThumbnailCache
{
    /// <summary>索引文件名（位于缓存根目录下）。</summary>
    public const string IndexFileName = "mod-thumbnails.json";

    /// <summary>图片目录名（位于缓存根目录下）。</summary>
    public const string ImageFolderName = "mod-thumbnails";

    /// <summary>单次失败后的退避时长：期间不再为同一个 mod 发起请求。</summary>
    public static readonly TimeSpan FailureBackoff = TimeSpan.FromHours(6);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly IClock _clock;
    private readonly string _cacheRoot;
    private readonly IDocumentFile _indexDocument;
    private readonly object _gate = new();
    private readonly Dictionary<string, ModThumbnailCacheEntry> _entries;

    public ModThumbnailCache(
        IFileSystem fileSystem,
        IAppLogger logger,
        IClock clock,
        string cacheRoot,
        IDocumentFile? indexDocument = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _fileSystem = fileSystem;
        _logger = logger;
        _clock = clock;
        _cacheRoot = cacheRoot;
        _indexDocument = indexDocument ?? new FileDocumentFile(fileSystem, IndexPath);
        _entries = Load(_indexDocument, logger);
    }

    /// <summary>缓存根目录（图片与索引都在其下）。</summary>
    public string CacheRoot => _cacheRoot;

    /// <summary>索引的旧文件路径（图片仍在 <see cref="CacheRoot"/> 下，索引已搬进自有库）。</summary>
    public string IndexPath => Path.Combine(_cacheRoot, IndexFileName);

    /// <summary>索引落点（<c>db:cache:mod-thumbnails</c> 或旧文件）。</summary>
    public string IndexLocation => _indexDocument.DisplayPath;

    /// <summary>某个 mod 图片的落盘路径前缀（下载器会在后面补扩展名）。</summary>
    public string TargetPathWithoutExtension(Guid modId) =>
        Path.Combine(_cacheRoot, ImageFolderName, modId.ToString("N"));

    /// <summary>
    /// 清空缓存：删索引文件、删图片目录，并清掉内存索引。
    /// <para>供设置页「清除缓存」使用；删除失败只记 Warn，不抛给调用方。</para>
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }

        try
        {
            _indexDocument.Delete();
            CacheStorage.Clear(_fileSystem, Path.Combine(_cacheRoot, ImageFolderName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("mod 缩略图缓存清理失败：" + exception.Message);
        }
    }

    /// <summary>索引里当前的条目数（设置页展示用）。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>取已缓存且文件确实存在的图片路径；没有或文件被删返回 null。</summary>
    public string? TryGetCachedPath(Guid modId)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(Key(modId), out var entry) || string.IsNullOrWhiteSpace(entry.FileName))
            {
                return null;
            }

            string path;
            try
            {
                path = Path.Combine(_cacheRoot, ImageFolderName, entry.FileName);
            }
            catch (Exception exception) when (exception is ArgumentException
                                                  or NotSupportedException
                                                  or PathTooLongException
                                                  or IOException)
            {
                return null;
            }

            return _fileSystem.FileExists(path) ? path : null;
        }
    }

    /// <summary>该 mod 是否处于失败退避窗口内（窗口内不发起请求）。</summary>
    public bool IsInBackoff(Guid modId)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(Key(modId), out var entry) || entry.FailedUtc is null)
            {
                return false;
            }

            var elapsed = _clock.UtcNow - entry.FailedUtc.Value;
            return elapsed >= TimeSpan.Zero && elapsed < FailureBackoff;
        }
    }

    /// <summary>记录一次成功下载；旧文件名不同的话删除旧文件。</summary>
    public void RecordSuccess(Guid modId, string url, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fileName = Path.GetFileName(filePath);

        lock (_gate)
        {
            var key = Key(modId);
            if (_entries.TryGetValue(key, out var existing) &&
                !string.IsNullOrWhiteSpace(existing.FileName) &&
                !string.Equals(existing.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                DeleteQuietly(Path.Combine(_cacheRoot, ImageFolderName, existing.FileName));
            }

            _entries[key] = new ModThumbnailCacheEntry
            {
                Url = url,
                FileName = fileName,
                FetchedUtc = _clock.UtcNow,
            };
        }

        Save();
    }

    /// <summary>记录一次失败，进入退避窗口。</summary>
    public void RecordFailure(Guid modId, string url)
    {
        lock (_gate)
        {
            var key = Key(modId);
            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new ModThumbnailCacheEntry();
                _entries[key] = entry;
            }

            entry.Url = url;
            entry.FailedUtc = _clock.UtcNow;
        }

        Save();
    }

    /// <summary>
    /// 清理：删除超过保留天数的图片，以及索引里没有引用的孤儿文件。返回删除的文件数。
    /// </summary>
    public int Prune(int retentionDays)
    {
        if (retentionDays < 1)
        {
            retentionDays = 1;
        }

        var cutoff = _clock.UtcNow - TimeSpan.FromDays(retentionDays);
        var deleted = 0;
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            // 只失败过的条目没有 FetchedUtc，用 FailedUtc 兜底；否则一次清理就会抹掉退避窗口，
            // 让每个失败的 mod 在下一轮又被重试。
            var expired = _entries
                .Where(pair =>
                {
                    var stamp = pair.Value.FetchedUtc ?? pair.Value.FailedUtc;
                    return stamp is null || stamp.Value < cutoff;
                })
                .Select(pair => pair.Key)
                .ToList();

            foreach (var key in expired)
            {
                var entry = _entries[key];
                if (!string.IsNullOrWhiteSpace(entry.FileName) &&
                    DeleteQuietly(Path.Combine(_cacheRoot, ImageFolderName, entry.FileName)))
                {
                    deleted++;
                }

                _entries.Remove(key);
            }

            foreach (var entry in _entries.Values)
            {
                if (!string.IsNullOrWhiteSpace(entry.FileName))
                {
                    keep.Add(entry.FileName);
                }
            }
        }

        var imageDirectory = Path.Combine(_cacheRoot, ImageFolderName);
        foreach (var file in _fileSystem.EnumerateFiles(imageDirectory, "*", recursive: false))
        {
            var fileName = _fileSystem.GetFileName(file);
            if (!keep.Contains(fileName) && DeleteQuietly(file))
            {
                deleted++;
            }
        }

        if (deleted > 0)
        {
            _logger.Info("mod 缩略图缓存：清理 " + deleted.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 个过期文件");
            Save();
        }

        return deleted;
    }

    /// <summary>原子写索引；写入失败只记日志。</summary>
    public void Save()
    {
        IndexFile snapshot;
        lock (_gate)
        {
            snapshot = new IndexFile
            {
                Entries = new Dictionary<string, ModThumbnailCacheEntry>(_entries, StringComparer.OrdinalIgnoreCase),
            };
        }

        try
        {
            if (!_indexDocument.WriteText(JsonSerializer.Serialize(snapshot, Options)))
            {
                _logger.Warn("mod 缩略图缓存索引写入失败：" + _indexDocument.DisplayPath);
            }
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or ArgumentException
                                              or NotSupportedException
                                              or PathTooLongException)
        {
            _logger.Warn("mod 缩略图缓存索引写入失败：" + exception.Message);
        }
    }

    private bool DeleteQuietly(string path)
    {
        try
        {
            if (!_fileSystem.FileExists(path))
            {
                return false;
            }

            _fileSystem.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("mod 缩略图缓存文件删除失败：" + path + "：" + exception.Message);
            return false;
        }
    }

    private static string Key(Guid modId) => modId.ToString("N");

    private static Dictionary<string, ModThumbnailCacheEntry> Load(
        IDocumentFile document,
        IAppLogger logger)
    {
        var entries = new Dictionary<string, ModThumbnailCacheEntry>(StringComparer.OrdinalIgnoreCase);
        var json = document.ReadText();
        if (json is null)
        {
            return entries;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<IndexFile>(json, Options);
            if (loaded?.Entries is null)
            {
                return entries;
            }

            foreach (var pair in loaded.Entries)
            {
                if (pair.Value is not null && !string.IsNullOrWhiteSpace(pair.Key))
                {
                    entries[pair.Key] = pair.Value;
                }
            }
        }
        catch (Exception exception) when (exception is JsonException
                                              or IOException
                                              or UnauthorizedAccessException)
        {
            logger.Warn("mod 缩略图缓存索引损坏，视为无缓存：" + exception.Message);
        }

        return entries;
    }

    private sealed class IndexFile
    {
        public int Version { get; set; } = 1;

        public Dictionary<string, ModThumbnailCacheEntry>? Entries { get; set; }
    }
}
