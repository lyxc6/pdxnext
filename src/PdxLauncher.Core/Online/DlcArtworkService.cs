namespace PdxLauncher.Core.Online;

using System.Globalization;
using System.Text;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// <see cref="IDlcArtworkService"/> 实现：<c>appdetails</c> 取 <c>header_image</c>，再下载成
/// <c>&lt;缓存目录&gt;\&lt;appid&gt;.jpg</c>。
/// <para>索引（appid → 图片 URL + 抓取时间）写一个 JSON 文件，URL 有效 14 天；单张下载失败记 30 分钟负缓存，
/// 避免反复失败的行每进一次页面都重试。索引损坏时当成空索引重建，不影响列表本身。</para>
/// <para>只请求 <see cref="OnlineHosts"/> 白名单内的主机；下载上限 2 MiB，先写 <c>.tmp</c> 再原子改名。</para>
/// </summary>
public sealed class DlcArtworkService : IDlcArtworkService
{
    private const int IndexVersion = 1;
    private const int MaxImageBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan UrlTtl = TimeSpan.FromDays(14);
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IFileSystem _fileSystem;
    private readonly IHttpFetcher _http;
    private readonly IClock _clock;
    private readonly IAppLogger _logger;
    private readonly string _root;
    private readonly string _indexPath;

    /// <summary>创建 DLC 头图服务。</summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <param name="http">HTTP 抓取器（只应请求白名单内的域名）。</param>
    /// <param name="clock">时钟（URL 有效期与失败退避）。</param>
    /// <param name="logger">日志。</param>
    /// <param name="cacheRoot">缓存目录，通常是 <c>data\dlc-artwork</c>。</param>
    public DlcArtworkService(
        IFileSystem fileSystem,
        IHttpFetcher http,
        IClock clock,
        IAppLogger logger,
        string cacheRoot)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);

        _fileSystem = fileSystem;
        _http = http;
        _clock = clock;
        _logger = logger;
        _root = cacheRoot;
        _indexPath = Path.Combine(cacheRoot, "dlc-artwork.json");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetArtworkAsync(
        IReadOnlyCollection<string> steamIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(steamIds);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var requested = NormalizeIds(steamIds);
        if (requested.Count == 0)
        {
            return result;
        }

        var index = LoadIndex();
        var now = _clock.UtcNow;
        var touched = false;

        try
        {
            _fileSystem.EnsureDirectory(_root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("DLC 头图缓存目录不可用（只影响图片）：" + ex.Message);
            return result;
        }

        foreach (var id in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = Path.Combine(_root, id + ".jpg");
            if (IsUsableImage(path))
            {
                result[id] = path;
                continue;
            }

            if (index.Failures.TryGetValue(id, out var failedUtc) && now - failedUtc < FailureBackoff)
            {
                continue;
            }

            try
            {
                var url = await ResolveUrlAsync(index, id, now, cancellationToken).ConfigureAwait(false);
                if (url is null)
                {
                    index.Failures[id] = now;
                    touched = true;
                    _logger.Warn(
                        "DLC 头图：Steam appdetails 未返回可用头图（appid=" + id
                        + "，可能是 bundle / 地区限制，或 header_image 不在白名单）");
                    continue;
                }

                var bytes = await DownloadAsync(url, cancellationToken).ConfigureAwait(false);
                if (bytes is null || bytes.Length == 0 || bytes.Length > MaxImageBytes)
                {
                    index.Failures[id] = now;
                    touched = true;
                    _logger.Warn("DLC 头图下载失败或体积异常（appid=" + id + "）");
                    continue;
                }

                _fileSystem.WriteAllBytesAtomic(path, bytes);
                result[id] = path;
                index.Failures.Remove(id);
                touched = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                index.Failures[id] = now;
                touched = true;
                _logger.Warn("DLC 头图获取失败（appid=" + id + "，只影响图片）：" + ex.Message);
            }
        }

        if (touched)
        {
            TrySaveIndex(index);
        }

        return result;
    }

    private bool IsUsableImage(string path)
    {
        try
        {
            return _fileSystem.FileExists(path) && _fileSystem.GetFileSize(path) > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>取（必要时刷新）某 appid 的头图 URL；拿不到返回 null。</summary>
    private async Task<string?> ResolveUrlAsync(
        ArtworkIndex index,
        string id,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (index.Entries.TryGetValue(id, out var entry)
            && !string.IsNullOrWhiteSpace(entry.Url)
            && now - entry.FetchedUtc < UrlTtl)
        {
            return entry.Url;
        }

        var url = await FetchHeaderImageUrlAsync(id, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        index.Entries[id] = new ArtworkEntry { Url = url, FetchedUtc = now };
        return url;
    }

    private async Task<string?> FetchHeaderImageUrlAsync(string id, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            "https://" + OnlineHosts.SteamStoreHost + "/api/appdetails?appids=" + Uri.EscapeDataString(id)
            + "&filters=basic",
            UriKind.Absolute);
        if (!OnlineHosts.IsAllowed(uri))
        {
            _logger.Warn("DLC 头图接口不在白名单内，已跳过：" + uri);
            return null;
        }

        string json;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(RequestTimeout);
            using var stream = await _http.GetAsync(uri, timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            json = await reader.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
        }

        return TryParseHeaderImage(json, id);
    }

    private static string? TryParseHeaderImage(string json, string id)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty(id, out var app) || app.ValueKind != JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                app = property.Value;
                break;
            }
        }

        if (app.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!app.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
        {
            return null;
        }

        if (!app.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!data.TryGetProperty("header_image", out var header) || header.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = header.GetString();
        if (string.IsNullOrWhiteSpace(text)
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !OnlineHosts.IsAllowed(uri))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        var uri = new Uri(url, UriKind.Absolute);
        if (!OnlineHosts.IsAllowed(uri))
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var stream = await _http.GetAsync(uri, timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxImageBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static List<string> NormalizeIds(IReadOnlyCollection<string> steamIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var raw in steamIds)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var id = raw.Trim();
            if (!id.All(char.IsAsciiDigit))
            {
                continue;
            }

            if (seen.Add(id))
            {
                list.Add(id);
            }
        }

        return list;
    }

    private ArtworkIndex LoadIndex()
    {
        try
        {
            if (!_fileSystem.FileExists(_indexPath))
            {
                return new ArtworkIndex();
            }

            var text = _fileSystem.ReadAllText(_indexPath);
            var index = JsonSerializer.Deserialize<ArtworkIndex>(text, JsonOptions);
            if (index is null || index.Version != IndexVersion)
            {
                return new ArtworkIndex();
            }

            index.Entries ??= new Dictionary<string, ArtworkEntry>(StringComparer.Ordinal);
            index.Failures ??= new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
            return index;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Warn("DLC 头图索引损坏，已重建：" + ex.Message);
            return new ArtworkIndex();
        }
    }

    private void TrySaveIndex(ArtworkIndex index)
    {
        try
        {
            index.Version = IndexVersion;
            _fileSystem.WriteAllTextAtomic(_indexPath, JsonSerializer.Serialize(index, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("DLC 头图索引写入失败（下次重新抓取）：" + ex.Message);
        }
    }

    private sealed class ArtworkIndex
    {
        public int Version { get; set; } = IndexVersion;

        public Dictionary<string, ArtworkEntry> Entries { get; set; } = new(StringComparer.Ordinal);

        public Dictionary<string, DateTimeOffset> Failures { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class ArtworkEntry
    {
        public string Url { get; set; } = string.Empty;

        public DateTimeOffset FetchedUtc { get; set; }
    }
}
