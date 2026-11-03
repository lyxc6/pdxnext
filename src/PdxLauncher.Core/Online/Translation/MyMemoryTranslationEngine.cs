namespace PdxLauncher.Core.Online.Translation;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 免密钥翻译引擎：MyMemory 公开接口
/// <c>GET https://api.mymemory.translated.net/get?q=&lt;原文&gt;&amp;langpair=Autodetect|&lt;目标&gt;</c>。
/// <para>
/// 为什么不用谷歌那个免费端点：谷歌 <c>translate.googleapis.com</c> 在部分网络下完全不可达
/// （TLS 握手失败），而 MyMemory 同一网络可直连。它的接口限制很硬：
/// 单次 <c>q</c> 最多 500 个字符（超出直接返回 <c>QUERY LENGTH LIMIT EXCEEDED</c>），
/// 源语言必须写 <c>Autodetect</c>（写 <c>auto</c> 会被判非法），匿名每天还有字数额度。
/// 所以超过 <see cref="MaxChunkChars"/> 的文本先按句读切成若干块，逐块翻译后再拼回去。
/// </para>
/// <para>
/// 与其它引擎一致：译不了就返回 null，不抛异常；日志里只出现状态码与短原因，不打印正文。
/// </para>
/// </summary>
public sealed class MyMemoryTranslationEngine : ITranslationEngine
{
    /// <summary>引擎标识；缓存键会带上它。</summary>
    public const string EngineId = "mymemory";

    /// <summary>
    /// 单个请求最多带多少字符。接口硬上限 500，这里留 50 给 URL 编码后的长度膨胀
    /// （中文按 UTF-8 百分号编码后一个字会变 9 个字符，所以实际能带的原文更短）。
    /// </summary>
    public const int MaxChunkChars = 450;

    /// <summary>同时进行的请求数上限。</summary>
    public const int MaxConcurrency = 4;

    /// <summary>单请求超时：这个接口要么很快回，要么根本回不来，不值得占着连接等一分钟。</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>接口对超长文本的错误前缀，会原样出现在 <c>responseData.translatedText</c> 里。</summary>
    private const string QueryLengthMarker = "QUERY LENGTH LIMIT EXCEEDED";

    /// <summary>额度用尽时正文里的固定句子（HTTP 状态同时是 429）。</summary>
    private const string QuotaMarker = "USED ALL AVAILABLE FREE TRANSLATIONS";

    /// <summary>解析不出恢复时间时的兜底等待：宁可半小时后再试，也不要每段译文都撞一次 429。</summary>
    private static readonly TimeSpan DefaultQuotaWait = TimeSpan.FromMinutes(30);

    /// <summary>额度恢复时间的写法：<c>NEXT AVAILABLE IN  02 HOURS 21 MINUTES 29 SECONDS</c>。</summary>
    private static readonly System.Text.RegularExpressions.Regex QuotaWaitPattern = new(
        @"NEXT AVAILABLE IN\s+(\d+)\s+HOURS?\s+(\d+)\s+MINUTES?",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>简体中文在接口里的语言码。</summary>
    private const string SimplifiedChineseCode = "zh-CN";

    /// <summary>英文在接口里的语言码。</summary>
    private const string EnglishCode = "en";

    private readonly ITranslationTransport _transport;
    private readonly IAppLogger _logger;
    private readonly TranslationEndpointHealth _health;

    /// <summary>最近一次失败的人话原因；成功时置回 null。</summary>
    private string? _lastFailure;

    /// <summary>创建引擎。</summary>
    /// <param name="transport">HTTP 传输。</param>
    /// <param name="logger">日志。</param>
    /// <param name="health">端点健康状态（额度冷却记在这里）；null 表示用进程级共享实例。</param>
    public MyMemoryTranslationEngine(
        ITranslationTransport transport,
        IAppLogger logger,
        TranslationEndpointHealth? health = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(logger);

        _transport = transport;
        _logger = logger;
        _health = health ?? TranslationEndpointHealth.Shared;
    }

    /// <inheritdoc />
    public string Id => EngineId;

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    public string? LastFailure => Volatile.Read(ref _lastFailure);

    /// <inheritdoc />
    public async Task<string?> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        var (translated, _) = await TranslateOnceAsync(text, targetLanguage, cancellationToken).ConfigureAwait(false);
        return translated;
    }

    /// <summary>
    /// 翻译一段文本，并告诉调用方这次失败是不是「主机级」的（超时、TLS、DNS）。
    /// 批处理据此决定要不要把同一台主机上的后续请求也停掉，免得几十条一起赔超时。
    /// </summary>
    /// <param name="text">原文。</param>
    /// <param name="targetLanguage">目标语言。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>译文与「是否主机级失败」。</returns>
    private async Task<(string? Text, bool Fatal)> TranslateOnceAsync(
        string text,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (text, false);
        }

        var chunks = SplitText(text, MaxChunkChars);
        if (chunks.Count == 1)
        {
            return await RequestAsync(chunks[0], targetLanguage, cancellationToken).ConfigureAwait(false);
        }

        // 多块：并发翻译，任何一块失败就整段算失败（保留原文，不拼半截译文）。
        using var gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var parts = new string?[chunks.Count];
        var abort = false;
        var fatal = false;

        var workers = new Task[chunks.Count];
        for (var i = 0; i < chunks.Count; i++)
        {
            var index = i;
            workers[i] = Task.Run(
                async () =>
                {
                    if (Volatile.Read(ref abort))
                    {
                        return;
                    }

                    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (Volatile.Read(ref abort))
                        {
                            return;
                        }

                        var (part, chunkFatal) = await RequestAsync(chunks[index], targetLanguage, cancellationToken)
                            .ConfigureAwait(false);
                        parts[index] = part;
                        if (part is null)
                        {
                            // 任何一块失败都让整段算失败，并让剩下的块别再白等。
                            Volatile.Write(ref abort, true);
                            if (chunkFatal)
                            {
                                Volatile.Write(ref fatal, true);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        Volatile.Write(ref abort, true);
                        Volatile.Write(ref fatal, true);
                    }
                    finally
                    {
                        gate.Release();
                    }
                },
                cancellationToken);
        }

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return (null, true);
        }

        var builder = new StringBuilder(text.Length);
        foreach (var part in parts)
        {
            if (part is null)
            {
                return (null, Volatile.Read(ref fatal));
            }

            builder.Append(part);
        }

        return (builder.ToString(), false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string?>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var results = new string?[texts.Count];
        if (texts.Count == 0)
        {
            return results;
        }

        using var gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var abort = false;

        var workers = new Task[texts.Count];
        for (var i = 0; i < texts.Count; i++)
        {
            var index = i;
            workers[i] = Task.Run(
                async () =>
                {
                    if (Volatile.Read(ref abort) || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (Volatile.Read(ref abort) || cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        var (translated, fatal) = await TranslateOnceAsync(texts[index], targetLanguage, cancellationToken)
                            .ConfigureAwait(false);
                        results[index] = translated;
                        if (fatal)
                        {
                            // 主机级故障（DNS/TLS/超时/主机未授权）：同一主机的后续请求也不会通，直接短路。
                            Volatile.Write(ref abort, true);
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // 单请求超时按失败处理，并让本批剩下的别再白等。
                        Volatile.Write(ref abort, true);
                    }
                    catch (Exception ex)
                    {
                        // 网络层故障（DNS/TLS/主机未授权）：同一主机后面的请求也不会通，直接短路。
                        _logger.Warn("MyMemory 翻译请求失败：" + ex.Message);
                        Volatile.Write(ref abort, true);
                    }
                    finally
                    {
                        gate.Release();
                    }
                },
                cancellationToken);
        }

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 调用方取消：返回已有结果（未完成的位置保持 null）。
        }

        return results;
    }

    /// <summary>
    /// 把文本切成每块不超过 <paramref name="maxChars"/> 的若干段，按句读/换行处下刀。
    /// 切点是「分隔符之后」，所以按顺序拼接这些块能还原原文（译文拼接后也保留段落结构）。
    /// </summary>
    /// <param name="text">原文。</param>
    /// <param name="maxChars">单块最大字符数。</param>
    public static IReadOnlyList<string> SplitText(string text, int maxChars = MaxChunkChars)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChars);

        if (text.Length <= maxChars)
        {
            return [text];
        }

        var chunks = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var remaining = text.Length - start;
            if (remaining <= maxChars)
            {
                chunks.Add(text[start..]);
                break;
            }

            var window = text.AsSpan(start, maxChars);
            var cut = -1;
            for (var i = window.Length - 1; i > 0; i--)
            {
                if (IsCutCharacter(window[i]))
                {
                    cut = i + 1;
                    break;
                }
            }

            if (cut <= 0)
            {
                // 一整段没有任何标点：硬切，总比整段译不出来强。
                cut = window.Length;
            }

            chunks.Add(text[start..(start + cut)]);
            start += cut;
        }

        return chunks;
    }

    /// <summary>可以作为切点的字符：换行与中英句读符号。</summary>
    private static bool IsCutCharacter(char value) =>
        value is '\n' or '。' or '！' or '？' or '；' or '，' or '、'
            or '.' or '!' or '?' or ';' or ',' or ' ';

    /// <summary>
    /// 发一个请求并解析译文。
    /// 返回值第二项标出「主机级失败」——超时、TLS、DNS 这类换一条文本也一样不通的原因；
    /// HTTP 4xx/5xx 与「响应不可用」只算本次失败，不影响后面的请求。
    /// </summary>
    /// <param name="chunk">待译片段。</param>
    /// <param name="targetLanguage">目标语言。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<(string? Text, bool Fatal)> RequestAsync(
        string chunk,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        // 额度冷却：上一轮已经把当天额度用完了。这里直接短路，别再逐块发请求。
        if (_health.IsQuotaExhausted && _health.QuotaResumeAt is { } resumeAt)
        {
            Volatile.Write(ref _lastFailure, DescribeQuota(resumeAt - _health.Now));
            _logger.Warn("MyMemory 免费额度已用完，跳过：" + _lastFailure);
            return (null, true);
        }

        var endpoint = BuildEndpoint(chunk, targetLanguage);
        if (endpoint is null)
        {
            _logger.Warn("MyMemory 请求地址拼装失败，本段跳过");
            Volatile.Write(ref _lastFailure, "请求地址拼装失败");
            return (null, false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            var response = await _transport
                .SendAsync(HttpMethod.Get, endpoint, null, timeout.Token)
                .ConfigureAwait(false);
            if (!response.Success)
            {
                // 额度用完时接口回 429，正文里写着几小时后恢复；这是主机级状态，本批不必再试。
                if (IsQuotaExhaustedResponse(response.StatusCode, response.Body))
                {
                    return MarkQuotaExhausted(response.Body);
                }

                _logger.Warn("MyMemory 翻译返回 HTTP " + response.StatusCode
                    + (string.IsNullOrEmpty(response.Error) ? string.Empty : "（" + response.Error + "）"));
                Volatile.Write(ref _lastFailure, "HTTP " + response.StatusCode);
                return (null, false);
            }

            var translated = ExtractTranslation(response.Body, out var error);
            if (translated is null)
            {
                _logger.Warn("MyMemory 翻译结果不可用：" + (error ?? "未知原因"));
                Volatile.Write(ref _lastFailure, "结果不可用（" + Shorten(error ?? "未知原因") + "）");
            }
            else
            {
                Volatile.Write(ref _lastFailure, null);
            }

            return (translated, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warn("MyMemory 翻译请求超时（" + (int)RequestTimeout.TotalSeconds + "s）");
            Volatile.Write(ref _lastFailure, "连接超时");
            return (null, true);
        }
        catch (Exception ex)
        {
            _logger.Warn("MyMemory 翻译请求失败：" + ex.Message);
            Volatile.Write(ref _lastFailure, "连接失败");
            return (null, true);
        }
    }

    /// <summary>额度用尽的响应：HTTP 429，或正文里带着 MyMemory 的额度警告。</summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="body">响应正文。</param>
    private static bool IsQuotaExhaustedResponse(int statusCode, string? body) =>
        statusCode == 429
        || (body is not null && body.Contains(QuotaMarker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 记下额度恢复时间并返回失败。
    /// <para>
    /// 正文里通常写着 <c>NEXT AVAILABLE IN 02 HOURS 21 MINUTES 29 SECONDS</c>；
    /// 解析不出来（例如只给了 429 状态码）就按 <see cref="DefaultQuotaWait"/> 兜底，
    /// 因为「一直重试」比「多等半小时」糟得多。
    /// </para>
    /// </summary>
    private (string? Text, bool Fatal) MarkQuotaExhausted(string? body)
    {
        var wait = ParseQuotaWait(body) ?? DefaultQuotaWait;
        var resumeAt = _health.Now + wait;
        _health.MarkQuotaExhaustedUntil(resumeAt);
        Volatile.Write(ref _lastFailure, DescribeQuota(wait));
        _logger.Warn("MyMemory 免费额度已用完：" + _lastFailure);
        return (null, true);
    }

    /// <summary>从响应正文里读出额度恢复还要多久；读不出来返回 null。</summary>
    /// <param name="body">响应正文。</param>
    internal static TimeSpan? ParseQuotaWait(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var match = QuotaWaitPattern.Match(body);
        if (!match.Success)
        {
            return null;
        }

        if (!int.TryParse(match.Groups[1].Value, out var hours)
            || !int.TryParse(match.Groups[2].Value, out var minutes))
        {
            return null;
        }

        var wait = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
        return wait > TimeSpan.Zero ? wait : null;
    }

    /// <summary>把剩余等待时间写成人话：「约 2 小时后恢复」。</summary>
    /// <param name="wait">还要等多久。</param>
    private static string DescribeQuota(TimeSpan wait)
    {
        if (wait <= TimeSpan.Zero)
        {
            return "免费额度已用完，稍后自动重试";
        }

        if (wait < TimeSpan.FromHours(1))
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes));
            return "免费额度已用完，约 " + minutes + " 分钟后恢复";
        }

        var hours = Math.Max(1, (int)Math.Ceiling(wait.TotalHours));
        return "免费额度已用完，约 " + hours + " 小时后恢复";
    }

    /// <summary>拼出接口地址；语言码不认识或网址非法时返回 null。</summary>
    private static Uri? BuildEndpoint(string text, string targetLanguage)
    {
        var code = targetLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? SimplifiedChineseCode
            : EnglishCode;
        var query = "q=" + Uri.EscapeDataString(text) + "&langpair=" + Uri.EscapeDataString("Autodetect|" + code);
        var url = "https://" + OnlineHosts.MyMemoryTranslationHost + "/get?" + query;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>
    /// 解析 <c>{"responseData":{"translatedText":"…"},"responseStatus":200}</c>。
    /// 注意 <c>responseStatus</c> 成功时是数字、出错时是字符串，
    /// 出错时 <c>responseData</c> 本身还会变成一段错误说明字符串。
    /// </summary>
    /// <param name="body">响应正文。</param>
    /// <param name="error">失败原因（成功时为 null）。</param>
    private static string? ExtractTranslation(string body, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            error = "响应为空";
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            error = "响应不是合法 JSON";
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "响应不是对象";
                return null;
            }

            var text = ReadTranslatedText(root);
            var status = ReadStatus(root);
            var details = ReadDetails(root);

            if (status is not null && status != 200)
            {
                error = "responseStatus=" + status
                    + (string.IsNullOrEmpty(details) ? string.Empty : "：" + details);
                return null;
            }

            if (text is null)
            {
                error = string.IsNullOrEmpty(details) ? "响应缺少译文" : details;
                return null;
            }

            if (text.Contains(QueryLengthMarker, StringComparison.OrdinalIgnoreCase)
                || text.Contains("IS AN INVALID", StringComparison.OrdinalIgnoreCase))
            {
                error = "接口拒绝本次请求：" + Shorten(text);
                return null;
            }

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }

    /// <summary>取 <c>responseData.translatedText</c>；不是对象或没有该字段返回 null。</summary>
    private static string? ReadTranslatedText(JsonElement root)
    {
        if (!root.TryGetProperty("responseData", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!data.TryGetProperty("translatedText", out var text) || text.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return text.GetString();
    }

    /// <summary>读 <c>responseStatus</c>：数字或数字字符串都认。</summary>
    private static int? ReadStatus(JsonElement root)
    {
        if (!root.TryGetProperty("responseStatus", out var status))
        {
            return null;
        }

        if (status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var number))
        {
            return number;
        }

        if (status.ValueKind == JsonValueKind.String
            && int.TryParse(status.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>读 <c>responseDetails</c>（出错时的说明）；没有就返回空串。</summary>
    private static string ReadDetails(JsonElement root)
    {
        if (root.TryGetProperty("responseDetails", out var details)
            && details.ValueKind == JsonValueKind.String)
        {
            return Shorten(details.GetString() ?? string.Empty);
        }

        return string.Empty;
    }

    /// <summary>把接口返回的长错误文案截短，免得日志一行刷屏。</summary>
    private static string Shorten(string value) =>
        value.Length <= 120 ? value : value[..120] + "…";
}
