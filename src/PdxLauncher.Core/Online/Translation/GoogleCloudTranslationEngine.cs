namespace PdxLauncher.Core.Online.Translation;

using System.Net.Http;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 谷歌云端翻译（Cloud Translation v2）引擎：
/// <c>POST https://translation.googleapis.com/language/translate/v2?key=&lt;API Key&gt;</c>。
/// <para>
/// 需要一个 API Key（免费层每月 50 万字符）。Key 走查询参数，只在发请求时拼进 URL，
/// 不写进日志。
/// </para>
/// </summary>
public sealed class GoogleCloudTranslationEngine : ITranslationEngine
{
    /// <summary>引擎标识；缓存键会带上它。</summary>
    public const string EngineId = "google-cloud";

    /// <summary>一次请求最多带多少条文本（官方上限 128）。</summary>
    public const int MaxItemsPerRequest = 128;

    /// <summary>简体中文在谷歌接口里的语言码。</summary>
    private const string SimplifiedChineseCode = "zh-CN";

    /// <summary>英文在谷歌接口里的语言码。</summary>
    private const string EnglishCode = "en";

    private readonly ITranslationTransport _transport;
    private readonly IAppLogger _logger;
    private readonly string? _apiKey;

    /// <summary>创建引擎。</summary>
    /// <param name="transport">HTTP 传输。</param>
    /// <param name="logger">日志。</param>
    /// <param name="apiKey">API Key；空则视为未配置。</param>
    public GoogleCloudTranslationEngine(
        ITranslationTransport transport,
        IAppLogger logger,
        string? apiKey)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(logger);

        _transport = transport;
        _logger = logger;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    /// <inheritdoc />
    public string Id => EngineId;

    /// <inheritdoc />
    public bool IsConfigured => _apiKey is not null;

    /// <inheritdoc />
    public async Task<string?> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var results = await TranslateBatchAsync([text], targetLanguage, cancellationToken).ConfigureAwait(false);
        return results.Count > 0 ? results[0] : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string?>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            return [];
        }

        if (!IsConfigured)
        {
            return new string?[texts.Count];
        }

        var results = new string?[texts.Count];
        var offset = 0;
        foreach (var chunk in Chunk(texts))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return results;
            }

            var translated = await RequestAsync(chunk, targetLanguage, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < chunk.Count; i++)
            {
                results[offset + i] = translated is not null && i < translated.Count ? translated[i] : null;
            }

            offset += chunk.Count;
        }

        return results;
    }

    /// <summary>按官方限制（128 条）把原文切成若干批。</summary>
    private static List<List<string>> Chunk(IReadOnlyList<string> texts)
    {
        var chunks = new List<List<string>>();
        var current = new List<string>();
        foreach (var text in texts)
        {
            if (current.Count >= MaxItemsPerRequest)
            {
                chunks.Add(current);
                current = new List<string>();
            }

            current.Add(text);
        }

        if (current.Count > 0)
        {
            chunks.Add(current);
        }

        return chunks;
    }

    /// <summary>发一批请求并解析译文；失败返回 null。</summary>
    private async Task<IReadOnlyList<string?>?> RequestAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint();
        if (endpoint is null)
        {
            return null;
        }

        var body = BuildRequestBody(texts, targetLanguage);
        TranslationResponse response;
        try
        {
            response = await _transport
                .SendAsync(HttpMethod.Post, endpoint, body, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("谷歌云翻译接口请求失败：" + ex.Message);
            return null;
        }

        if (!response.Success)
        {
            _logger.Warn("谷歌云翻译接口返回 HTTP " + response.StatusCode);
            return null;
        }

        return ExtractTranslations(response.Body);
    }

    /// <summary>拼出接口地址（Key 只在这里拼一次，不进日志）。</summary>
    private Uri? BuildEndpoint()
    {
        var url = "https://" + OnlineHosts.GoogleCloudTranslationHost
            + "/language/translate/v2?key=" + Uri.EscapeDataString(_apiKey!);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>拼请求体：<c>{"q":[..],"target":"zh-CN","format":"text"}</c>。</summary>
    private static string BuildRequestBody(IReadOnlyList<string> texts, string targetLanguage)
    {
        var code = targetLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? SimplifiedChineseCode
            : EnglishCode;

        var payload = new System.Text.StringBuilder("{\"q\":[");
        for (var i = 0; i < texts.Count; i++)
        {
            if (i > 0)
            {
                payload.Append(',');
            }

            payload.Append(JsonSerializer.Serialize(texts[i]));
        }

        payload.Append("],\"target\":").Append(JsonSerializer.Serialize(code))
            .Append(",\"format\":\"text\"}");
        return payload.ToString();
    }

    /// <summary>
    /// 解析返回：<c>{"data":{"translations":[{"translatedText":"译文"}]}}</c>。
    /// </summary>
    private static IReadOnlyList<string?>? ExtractTranslations(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("translations", out var translations)
                || translations.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var results = new List<string?>();
            foreach (var item in translations.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("translatedText", out var textElement)
                    && textElement.ValueKind == JsonValueKind.String)
                {
                    results.Add(textElement.GetString());
                }
                else
                {
                    results.Add(null);
                }
            }

            return results;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
