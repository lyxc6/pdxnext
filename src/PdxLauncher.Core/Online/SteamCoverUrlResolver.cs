namespace PdxLauncher.Core.Online;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;

/// <summary>把 Steam AppId 解析成一张可下载的封面图 URL。</summary>
public interface ISteamCoverUrlResolver
{
    /// <summary>解析失败（无此 App、被墙、返回畸形）时返回 null，不抛异常。</summary>
    Task<string?> ResolveHeaderImageUrlAsync(int appId, CancellationToken cancellationToken);
}

/// <summary>
/// 通过公开的商店 appdetails 接口拿 <c>header_image</c>。
/// 为什么不能拼固定路径：Steam 已把静态地址迁移成
/// <c>store_item_assets/steam/apps/&lt;appid&gt;/&lt;内容hash&gt;/header.jpg</c>，
/// 老式 <c>/steam/apps/&lt;appid&gt;/header.jpg</c> 对新游戏（如 EU5 3450310）直接 404。
/// 该接口无需 API key；每进程每 AppId 只问一次（成功或失败都记住）。
/// </summary>
public sealed class SteamCoverUrlResolver : ISteamCoverUrlResolver
{
    /// <summary>公开商店接口；<c>filters=basic</c> 只取基础字段，响应约 30 KB。</summary>
    public const string ApiUrlTemplate =
        "https://store.steampowered.com/api/appdetails?appids={0}&filters=basic&l=english";

    /// <summary>单个 AppId 的响应上限；超出视为异常响应，直接放弃。</summary>
    public const int MaximumResponseBytes = 512 * 1024;

    private readonly IHttpFetcher _fetcher;
    private readonly IAppLogger _logger;
    private readonly ConcurrentDictionary<int, string?> _memo = new();

    public SteamCoverUrlResolver(IHttpFetcher fetcher, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(logger);
        _fetcher = fetcher;
        _logger = logger;
    }

    public async Task<string?> ResolveHeaderImageUrlAsync(int appId, CancellationToken cancellationToken)
    {
        if (appId <= 0)
        {
            return null;
        }

        if (_memo.TryGetValue(appId, out var known))
        {
            return known;
        }

        string? url = null;
        try
        {
            var apiUrl = string.Format(CultureInfo.InvariantCulture, ApiUrlTemplate, appId);
            await using var stream = await _fetcher
                .GetAsync(new Uri(apiUrl), cancellationToken)
                .ConfigureAwait(false);
            var json = await ReadLimitedAsync(stream, cancellationToken).ConfigureAwait(false);
            url = ParseHeaderImage(json, appId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("Steam 封面地址解析失败: " + appId + "，" + ex);
        }

        _memo[appId] = url;
        return url;
    }

    /// <summary>从 appdetails 响应里取 <c>&lt;appid&gt;.data.header_image</c>；任何形状不符都返回 null。</summary>
    public static string? ParseHeaderImage(string json, int appId)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(
                    appId.ToString(CultureInfo.InvariantCulture),
                    out var entry)
                || entry.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!entry.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!data.TryGetProperty("header_image", out var header)
                || header.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var raw = header.GetString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaximumResponseBytes)
            {
                throw new InvalidOperationException("appdetails 响应过大");
            }
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
