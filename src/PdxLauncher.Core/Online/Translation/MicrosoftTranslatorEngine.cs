namespace PdxLauncher.Core.Online.Translation;

using System.Net.Http;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 微软 Azure Translator（文本翻译 v3）引擎：
/// <c>POST https://api.cognitiveservices.azure.com/translator/text/v3/translate?api-version=3.0&amp;to=&lt;目标&gt;</c>。
/// <para>
/// 需要「订阅密钥 + 区域」两个凭据（请求头 <c>Ocp-Apim-Subscription-Key</c> 与
/// <c>Ocp-Apim-Subscription-Region</c>）。免费层每月 200 万字符。
/// 密钥与区域只进请求头，不写进日志。
/// </para>
/// </summary>
public sealed class MicrosoftTranslatorEngine : ITranslationEngine
{
    /// <summary>引擎标识；缓存键会带上它。</summary>
    public const string EngineId = "microsoft";

    /// <summary>一次请求最多带多少条文本（官方上限 100）。</summary>
    public const int MaxItemsPerRequest = 100;

    /// <summary>一次请求最多带多少字符（官方上限 50000）。</summary>
    public const int MaxCharsPerRequest = 50_000;

    /// <summary>简体中文在微软接口里的语言码。</summary>
    private const string SimplifiedChineseCode = "zh-Hans";

    /// <summary>英文在微软接口里的语言码。</summary>
    private const string EnglishCode = "en";

    private readonly ITranslationTransport _transport;
    private readonly IAppLogger _logger;
    private readonly string? _key;
    private readonly string? _region;

    /// <summary>创建引擎。</summary>
    /// <param name="transport">HTTP 传输。</param>
    /// <param name="logger">日志。</param>
    /// <param name="subscriptionKey">订阅密钥；空则视为未配置。</param>
    /// <param name="region">资源区域（如 eastasia）；空则视为未配置。</param>
    public MicrosoftTranslatorEngine(
        ITranslationTransport transport,
        IAppLogger logger,
        string? subscriptionKey,
        string? region)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(logger);

        _transport = transport;
        _logger = logger;
        _key = string.IsNullOrWhiteSpace(subscriptionKey) ? null : subscriptionKey.Trim();
        _region = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
    }

    /// <inheritdoc />
    public string Id => EngineId;

    /// <inheritdoc />
    public bool IsConfigured => _key is not null && _region is not null;

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

    /// <summary>按官方限制（100 条 / 50000 字符）把原文切成若干批。</summary>
    private static List<List<string>> Chunk(IReadOnlyList<string> texts)
    {
        var chunks = new List<List<string>>();
        var current = new List<string>();
        var currentChars = 0;
        foreach (var text in texts)
        {
            var length = text.Length;
            if (current.Count > 0
                && (current.Count >= MaxItemsPerRequest || currentChars + length > MaxCharsPerRequest))
            {
                chunks.Add(current);
                current = new List<string>();
                currentChars = 0;
            }

            current.Add(text);
            currentChars += length;
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
        var endpoint = BuildEndpoint(targetLanguage);
        if (endpoint is null)
        {
            return null;
        }

        var body = BuildRequestBody(texts);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ocp-Apim-Subscription-Key"] = _key!,
            ["Ocp-Apim-Subscription-Region"] = _region!,
        };

        TranslationResponse response;
        try
        {
            response = await _transport
                .SendAsync(HttpMethod.Post, endpoint, body, cancellationToken, headers)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("微软翻译接口请求失败：" + ex.Message);
            return null;
        }

        if (!response.Success)
        {
            _logger.Warn("微软翻译接口返回 HTTP " + response.StatusCode);
            return null;
        }

        return ExtractTranslations(response.Body);
    }

    /// <summary>拼出接口地址。</summary>
    private static Uri? BuildEndpoint(string targetLanguage)
    {
        var code = targetLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? SimplifiedChineseCode
            : EnglishCode;
        var url = "https://" + OnlineHosts.MicrosoftTranslatorHost
            + "/translator/text/v3/translate?api-version=3.0&to=" + Uri.EscapeDataString(code);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>拼请求体：官方要求是 JSON 数组，元素形如 <c>{"Text":"原文"}</c>。</summary>
    private static string BuildRequestBody(IReadOnlyList<string> texts)
    {
        var payload = new System.Text.StringBuilder("[");
        for (var i = 0; i < texts.Count; i++)
        {
            if (i > 0)
            {
                payload.Append(',');
            }

            payload.Append("{\"Text\":").Append(JsonSerializer.Serialize(texts[i])).Append('}');
        }

        payload.Append(']');
        return payload.ToString();
    }

    /// <summary>
    /// 解析返回：<c>[{"translations":[{"text":"译文"}]}]</c>。
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
            if (root.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var results = new List<string?>();
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("translations", out var translations)
                    || translations.ValueKind != JsonValueKind.Array
                    || translations.GetArrayLength() == 0)
                {
                    results.Add(null);
                    continue;
                }

                var first = translations[0];
                if (first.ValueKind == JsonValueKind.Object
                    && first.TryGetProperty("text", out var textElement)
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
