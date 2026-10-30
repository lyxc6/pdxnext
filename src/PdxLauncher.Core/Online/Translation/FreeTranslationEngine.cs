namespace PdxLauncher.Core.Online.Translation;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 内置免费翻译引擎：Google 翻译的公开 Web 接口
/// <c>GET https://translate.googleapis.com/translate_a/single?client=gtx&amp;dt=t&amp;sl=auto&amp;tl=&lt;目标&gt;&amp;q=&lt;原文&gt;</c>。
/// <para>
/// 不需要用户配置，是「自填 API 没配 / 调不通」时的兜底。这个接口是公开但非官方承诺的，
/// 随时可能变；解析不出来就返回 null，让上层决定要不要再回落或干脆不显示译文。
/// </para>
/// </summary>
public sealed class FreeTranslationEngine : ITranslationEngine
{
    /// <summary>引擎标识；缓存键会带上它。</summary>
    public const string EngineId = "free";

    /// <summary>目标语言代码到接口参数的映射（接口只认这两个短码）。</summary>
    private const string SimplifiedChineseCode = "zh-CN";

    private const string EnglishCode = "en";

    private readonly ITranslationTransport _transport;
    private readonly IAppLogger _logger;

    /// <summary>创建引擎。</summary>
    /// <param name="transport">HTTP 传输。</param>
    /// <param name="logger">日志。</param>
    public FreeTranslationEngine(ITranslationTransport transport, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(logger);

        _transport = transport;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => EngineId;

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    public async Task<string?> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var endpoint = BuildEndpoint(text, targetLanguage);
        if (endpoint is null)
        {
            return null;
        }

        try
        {
            var response = await _transport
                .SendAsync(HttpMethod.Get, endpoint, null, cancellationToken)
                .ConfigureAwait(false);
            if (!response.Success)
            {
                _logger.Warn("内置翻译服务返回 HTTP " + response.StatusCode);
                return null;
            }

            return ExtractTranslation(response.Body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("内置翻译服务请求失败：" + ex.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string?>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var results = new string?[texts.Count];
        for (var i = 0; i < texts.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return results;
            }

            results[i] = await TranslateAsync(texts[i], targetLanguage, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    /// <summary>拼出接口地址；文本过长时返回 null（超长文本交给自填引擎更合适）。</summary>
    private static Uri? BuildEndpoint(string text, string targetLanguage)
    {
        var code = targetLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? SimplifiedChineseCode
            : EnglishCode;
        var query = string.Join(
            "&",
            "client=gtx",
            "dt=t",
            "sl=auto",
            "tl=" + Uri.EscapeDataString(code),
            "q=" + Uri.EscapeDataString(text));
        var url = "https://" + OnlineHosts.TranslationHost + "/translate_a/single?" + query;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>
    /// 解析接口返回的 JSON 数组：<c>[[["译文","原文",...]],...]</c>，多段译文要拼起来。
    /// </summary>
    private static string? ExtractTranslation(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                return null;
            }

            var segments = root[0];
            if (segments.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var builder = new StringBuilder();
            foreach (var segment in segments.EnumerateArray())
            {
                if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0
                    && segment[0].ValueKind == JsonValueKind.String)
                {
                    builder.Append(segment[0].GetString());
                }
            }

            var text = builder.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
