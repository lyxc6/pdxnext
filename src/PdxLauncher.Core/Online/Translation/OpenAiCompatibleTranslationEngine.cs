namespace PdxLauncher.Core.Online.Translation;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 用户自填的 OpenAI 兼容 API 引擎：<c>POST {BaseUrl}/chat/completions</c>。
/// <para>
/// 批量翻译用「分隔符协议」：把所有原文用 <see cref="BatchSeparator"/> 连成一段发过去，
/// 要求模型用同一个分隔符把译文分开。返回的段数与原文数不一致时（模型没听话）
/// 退化成逐条翻译，保证正确性优先。
/// </para>
/// </summary>
public sealed class OpenAiCompatibleTranslationEngine : ITranslationEngine
{
    /// <summary>批量翻译的分隔符（模型必须原样回吐）。</summary>
    public const string BatchSeparator = "\n<<<PDX_SPLIT>>>\n";

    /// <summary>引擎标识前缀；缓存键会带上它。</summary>
    public const string EngineId = "openai-compatible";

    private readonly ITranslationTransport _transport;
    private readonly IAppLogger _logger;
    private readonly string? _baseUrl;
    private readonly string? _apiKey;
    private readonly string _model;

    /// <summary>创建引擎。</summary>
    /// <param name="transport">HTTP 传输。</param>
    /// <param name="logger">日志。</param>
    /// <param name="baseUrl">API 根地址（如 https://api.openai.com/v1）；空则视为未配置。</param>
    /// <param name="apiKey">密钥；空则视为未配置。</param>
    /// <param name="model">模型名；空时用默认值。</param>
    public OpenAiCompatibleTranslationEngine(
        ITranslationTransport transport,
        IAppLogger logger,
        string? baseUrl,
        string? apiKey,
        string? model = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(logger);

        _transport = transport;
        _logger = logger;
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/');
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _model = string.IsNullOrWhiteSpace(model) ? Configuration.TranslationModels.Default : model.Trim();
    }

    /// <inheritdoc />
    public string Id => EngineId + ":" + _model;

    /// <inheritdoc />
    public bool IsConfigured => _baseUrl is not null && _apiKey is not null;

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

        // 先试一次批量；模型不守规矩就逐条重来。
        var batched = await RequestBatchAsync(texts, targetLanguage, cancellationToken).ConfigureAwait(false);
        if (batched is not null)
        {
            return batched;
        }

        var results = new string?[texts.Count];
        for (var i = 0; i < texts.Count; i++)
        {
            var single = await RequestBatchAsync([texts[i]], targetLanguage, cancellationToken).ConfigureAwait(false);
            results[i] = single is { Count: 1 } ? single[0] : null;
            if (results[i] is null && !cancellationToken.IsCancellationRequested)
            {
                _logger.Warn("自填翻译 API 单条翻译失败（第 " + (i + 1) + " 条）");
            }
        }

        return results;
    }

    /// <summary>
    /// 发一次请求并解析结果；返回 null 表示这次调用失败（网络、鉴权、格式）。
    /// </summary>
    private async Task<IReadOnlyList<string?>?> RequestBatchAsync(
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
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = "Bearer " + _apiKey,
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
            _logger.Warn("自填翻译 API 请求失败：" + ex.Message);
            return null;
        }

        if (!response.Success)
        {
            _logger.Warn("自填翻译 API 返回 HTTP " + response.StatusCode);
            return null;
        }

        var content = ExtractContent(response.Body);
        if (content is null)
        {
            _logger.Warn("自填翻译 API 响应格式无法解析");
            return null;
        }

        if (texts.Count == 1)
        {
            return [content];
        }

        var parts = content.Split(BatchSeparator, StringSplitOptions.None);
        if (parts.Length != texts.Count)
        {
            _logger.Warn(
                "自填翻译 API 批量结果条数不符（得到 " + parts.Length + "，期望 " + texts.Count + "），改为逐条翻译");
            return null;
        }

        var results = new string?[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            results[i] = parts[i].Trim();
        }

        return results;
    }

    /// <summary>拼出 chat/completions 端点；地址非法时返回 null。</summary>
    private Uri? BuildEndpoint()
    {
        if (_baseUrl is null)
        {
            return null;
        }

        if (!Uri.TryCreate(_baseUrl + "/chat/completions", UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri;
    }

    /// <summary>构造请求正文。</summary>
    private string BuildRequestBody(IReadOnlyList<string> texts, string targetLanguage)
    {
        var joined = string.Join(BatchSeparator, texts);
        var prompt = texts.Count == 1
            ? "把下面这段文本翻译成 " + targetLanguage + "，只输出译文，不要解释。"
                + "保留原文里的 BBCode 标签与换行。\n\n" + joined
            : "把下面 " + texts.Count + " 段文本分别翻译成 " + targetLanguage + "。"
                + "只输出译文，用一行 " + BatchSeparator.Trim() + " 把译文分开，段数必须与原文一致，不要解释。\n\n"
                + joined;

        var payload = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = "You are a translation engine for game mods. Output only the translation." },
                new { role = "user", content = prompt },
            },
            temperature = 0,
        };

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>从响应 JSON 里取 <c>choices[0].message.content</c>。</summary>
    private static string? ExtractContent(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var first = choices[0];
            if (!first.TryGetProperty("message", out var message))
            {
                return null;
            }

            if (!message.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = content.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>引擎标识里的模型名（供缓存键使用）。</summary>
    public string Model => _model;

    /// <summary>调试用：拼好的请求正文（不含密钥）。</summary>
    /// <param name="texts">原文列表。</param>
    /// <param name="targetLanguage">目标语言。</param>
    public string BuildDebugBody(IReadOnlyList<string> texts, string targetLanguage) =>
        BuildRequestBody(texts, targetLanguage);

    /// <summary>调试用：请求正文里用到的分隔符文本。</summary>
    public static string Separator => BatchSeparator;

    /// <summary>调试用：把批量译文按分隔符拆开。</summary>
    /// <param name="text">合并后的译文。</param>
    public static string[] Split(string text) =>
        text.Split(BatchSeparator, StringSplitOptions.None);

    /// <summary>调试用：请求正文的 UTF-8 字节数。</summary>
    /// <param name="texts">原文列表。</param>
    /// <param name="targetLanguage">目标语言。</param>
    public int MeasureBodySize(IReadOnlyList<string> texts, string targetLanguage) =>
        Encoding.UTF8.GetByteCount(BuildRequestBody(texts, targetLanguage));
}
