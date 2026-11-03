namespace PdxLauncher.Core.Online.Translation;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 内置免费翻译引擎：Google 翻译的公开 Web 接口
/// <c>GET https://translate.google.com/translate_a/t?client=dict-chrome-ex&amp;sl=auto&amp;tl=&lt;目标&gt;&amp;q=…</c>。
/// <para>
/// 不需要用户配置，是「自填 API 没配 / 调不通」时的兜底。这个接口是公开但非官方承诺的，
/// 随时可能变；解析不出来就返回 null，让上层决定要不要再回落或干脆不显示译文。
/// </para>
/// <para>
/// 三台主机用同一套请求与返回结构，按 <see cref="Hosts"/> 顺序逐台尝试：
/// 连不上的进冷却名单（默认 5 分钟），被限流（HTTP 429）的进短冷却（<see cref="RateLimitedHostTtl"/>），
/// 不再让每一段译文都白等一次超时、也不再对限流主机连撞。
/// </para>
/// <para>
/// 请求按 <see cref="MaxTextsPerRequest"/> 段 / <see cref="MaxRequestUrlLength"/> 字符合并，
/// 一页几十段只要几条请求；评论批量翻译时这是不触发限流的关键。
/// </para>
/// </summary>
public sealed class FreeTranslationEngine : ITranslationEngine
{
    /// <summary>引擎标识；缓存键会带上它。</summary>
    public const string EngineId = "free";

    /// <summary>同时进行的请求数上限（一个请求可带多段文本）。</summary>
    public const int MaxConcurrency = 4;

    /// <summary>单个请求最多合并多少段文本；再多就分组。</summary>
    public const int MaxTextsPerRequest = 8;

    /// <summary>
    /// 单个请求地址的长度上限。接口对 <c>q</c> 的长度没有文档，但网关对 URL 有上限；
    /// 1500 字符是实测稳定值（2000 也通过，留余量给转义后的扩展字符）。
    /// </summary>
    public const int MaxRequestUrlLength = 1500;

    /// <summary>
    /// 单请求超时。这个端点在很多网络下根本连不上（TLS 握手失败或直接丢包），
    /// 若沿用传输层 60 秒超时，一页 30 行会让界面静默等上半小时。
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 端点按顺序尝试：首台连不上（TLS 握手失败/超时/DNS）时，换备用主机再来一次。
    /// 三台主机用的是同一个公开接口、同一套路径与返回结构。
    /// </summary>
    public static readonly IReadOnlyList<string> Hosts =
    [
        OnlineHosts.GoogleTranslateWebHost,
        OnlineHosts.Clients5TranslationHost,
        OnlineHosts.TranslationHost,
    ];

    /// <summary>主机被判定为「连不上」后冷却多久。网络环境一般不会几分钟就变，这个时长足够省掉整页的超时。</summary>
    public static readonly TimeSpan DeadHostTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 主机返回 HTTP 429（限流）后冷却多久。被限流的主机过一会儿通常就好了，
    /// 冷却时间不能像「连不上」那么长，否则整段时间都用不了这台。
    /// </summary>
    public static readonly TimeSpan RateLimitedHostTtl = TimeSpan.FromSeconds(60);

    /// <summary>目标语言代码到接口参数的映射（接口只认这两个短码）。</summary>
    private const string SimplifiedChineseCode = "zh-CN";

    private const string EnglishCode = "en";

    /// <summary>429 时把响应体前多少字符写进日志；再长只是噪音。</summary>
    private const int MaxLoggedBodyLength = 120;

    private readonly ITranslationTransport _transport;
    private readonly IAppLogger _logger;
    private readonly TranslationEndpointHealth _health;

    /// <summary>最近一次失败的人话原因；成功时置回 null。</summary>
    private string? _lastFailure;

    /// <summary>创建引擎。</summary>
    /// <param name="transport">HTTP 传输。</param>
    /// <param name="logger">日志。</param>
    /// <param name="health">端点健康状态；null 表示用进程级共享实例。</param>
    public FreeTranslationEngine(
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
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var (translated, _) = await RequestAsync([text], targetLanguage, cancellationToken).ConfigureAwait(false);
        return translated is { Length: > 0 } ? translated[0] : null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 先把文本按段数 / URL 长度合并成几组，再并发发起（组内保序）；
    /// 第一个网络层故障就让本批剩下的别再白等（同一个主机连不通，后面的请求也不会有好结果）。
    /// </remarks>
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

        var groups = GroupTexts(texts, targetLanguage);
        if (groups.Count == 0)
        {
            return results;
        }

        using var gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        var abort = false;

        var workers = new Task[groups.Count];
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
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

                        var (translated, fatal) = await RequestAsync(group.Texts, targetLanguage, cancellationToken)
                            .ConfigureAwait(false);
                        if (translated is not null)
                        {
                            for (var k = 0; k < group.Indices.Length; k++)
                            {
                                results[group.Indices[k]] = translated[k];
                            }
                        }

                        if (fatal)
                        {
                            Volatile.Write(ref abort, true);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
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
            // 调用方取消：返回已有结果，未完成的位置保持 null。
        }

        return results;
    }

    /// <summary>把待译文本按「≤<see cref="MaxTextsPerRequest"/> 段且 URL ≤<see cref="MaxRequestUrlLength"/>」分组。</summary>
    private static List<TextGroup> GroupTexts(IReadOnlyList<string> texts, string targetLanguage)
    {
        var groups = new List<TextGroup>();
        var indices = new List<int>();
        var parts = new List<string>();
        var length = EndpointLength(targetLanguage);

        for (var i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var escapedLength = Uri.EscapeDataString(text).Length + 3; // "&q="
            if (indices.Count > 0
                && (indices.Count >= MaxTextsPerRequest || length + escapedLength > MaxRequestUrlLength))
            {
                groups.Add(new TextGroup([.. indices], [.. parts]));
                indices.Clear();
                parts.Clear();
                length = EndpointLength(targetLanguage);
            }

            indices.Add(i);
            parts.Add(text);
            length += escapedLength;
        }

        if (indices.Count > 0)
        {
            groups.Add(new TextGroup([.. indices], [.. parts]));
        }

        return groups;
    }

    /// <summary>不含 <c>q</c> 的请求地址长度（取最长主机名，保证分组不会因主机不同而超长）。</summary>
    private static int EndpointLength(string targetLanguage)
    {
        var host = Hosts[0];
        foreach (var candidate in Hosts)
        {
            if (candidate.Length > host.Length)
            {
                host = candidate;
            }
        }

        return ("https://" + host + "/translate_a/t?client=dict-chrome-ex&sl=auto&tl=" + LanguageCode(targetLanguage)).Length;
    }

    /// <summary>
    /// 发一个请求（可带多段原文）并解析整组译文。
    /// </summary>
    /// <param name="texts">同一请求里的原文，顺序与结果一一对应。</param>
    /// <param name="targetLanguage">目标语言。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// <c>Texts</c> 为整组译文（失败 null）；<c>Fatal</c> 表示这次失败是网络层故障
    /// （连不上/超时），同一批的其它请求也没必要再试。
    /// </returns>
    private async Task<(string?[]? Texts, bool Fatal)> RequestAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        // 整组请求共用一个超时预算：第一台主机卡住时，第二台不会再白等一个完整超时。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        var attempts = 0;
        var networkFailures = 0;
        string? lastFailure = null;

        foreach (var host in Hosts)
        {
            if (_health.IsDead(host))
            {
                lastFailure ??= "连接失败（" + host + " 暂时跳过）";
                continue;
            }

            var endpoint = BuildEndpoint(host, texts, targetLanguage);
            if (endpoint is null)
            {
                continue;
            }

            attempts++;
            try
            {
                var response = await _transport
                    .SendAsync(HttpMethod.Get, endpoint, null, timeout.Token)
                    .ConfigureAwait(false);
                if (!response.Success)
                {
                    if (response.StatusCode == 429)
                    {
                        // 限流：这台过一会儿就恢复，先冷却短时间再换下一台；
                        // 不能像「连不上」那样拉黑 5 分钟，否则限流一次整段时间都用不上它。
                        _logger.Warn(
                            "内置翻译服务（" + host + "）返回 HTTP 429，先冷却 "
                            + (int)RateLimitedHostTtl.TotalSeconds + "s；响应："
                            + Shorten(response.Body));
                        _health.MarkDead(host, RateLimitedHostTtl);
                        lastFailure ??= "HTTP 429（" + host + "）";
                        continue;
                    }

                    // 其它 HTTP 非 2xx：主机是通的，只是这条请求被拒（额度、变更），换下一台但不拉黑。
                    _logger.Warn("内置翻译服务（" + host + "）返回 HTTP " + response.StatusCode);
                    lastFailure ??= "HTTP " + response.StatusCode + "（" + host + "）";
                    continue;
                }

                var translated = ExtractTranslations(response.Body, texts.Count);
                if (translated is null)
                {
                    _logger.Warn("内置翻译服务（" + host + "）的响应无法解析");
                    lastFailure ??= "返回内容无法解析（" + host + "）";
                    continue;
                }

                _health.MarkHealthy(host);
                Volatile.Write(ref _lastFailure, null);
                return (translated, false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // 超时预算用完：后面几台也不用试了。
                _logger.Warn("内置翻译服务请求超时（" + (int)RequestTimeout.TotalSeconds + "s，" + host + "）");
                _health.MarkDead(host, DeadHostTtl);
                lastFailure ??= "连接超时（" + host + "）";
                networkFailures++;
                break;
            }
            catch (Exception ex)
            {
                _logger.Warn("内置翻译服务（" + host + "）请求失败：" + ex.Message);
                _health.MarkDead(host, DeadHostTtl);
                lastFailure ??= "连接失败（" + host + "）";
                networkFailures++;
            }
        }

        Volatile.Write(ref _lastFailure, lastFailure ?? "没有可用的谷歌翻译主机");
        if (attempts == 0)
        {
            // 所有主机都在冷却：本批不必再试，直接让上层回落或报错。
            return (null, true);
        }

        return (null, networkFailures >= attempts);
    }

    /// <summary>目标语言到接口短码。</summary>
    private static string LanguageCode(string targetLanguage) =>
        targetLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? SimplifiedChineseCode
            : EnglishCode;

    /// <summary>拼出接口地址；文本为空或网址非法时返回 null。</summary>
    /// <param name="host">要用的主机名（见 <see cref="Hosts"/>）。</param>
    /// <param name="texts">同一请求里的原文。</param>
    /// <param name="targetLanguage">目标语言。</param>
    private static Uri? BuildEndpoint(string host, IReadOnlyList<string> texts, string targetLanguage)
    {
        if (texts.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append("https://").Append(host).Append("/translate_a/t?client=dict-chrome-ex&sl=auto&tl=")
            .Append(Uri.EscapeDataString(LanguageCode(targetLanguage)));
        foreach (var text in texts)
        {
            builder.Append("&q=").Append(Uri.EscapeDataString(text));
        }

        return Uri.TryCreate(builder.ToString(), UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>日志里只留响应体开头，避免 429 的长 HTML 把日志撑满。</summary>
    private static string Shorten(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "（空）";
        }

        var trimmed = body.Trim();
        return trimmed.Length <= MaxLoggedBodyLength
            ? trimmed
            : trimmed[..MaxLoggedBodyLength] + "…";
    }

    /// <summary>
    /// 解析接口返回的 JSON：<c>[["译文","源语言"],["译文","源语言"],…]</c>，条数必须与请求一致。
    /// </summary>
    private static string?[]? ExtractTranslations(string body, int expectedCount)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != expectedCount)
            {
                return null;
            }

            var result = new string?[expectedCount];
            for (var i = 0; i < expectedCount; i++)
            {
                var item = root[i];
                if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() == 0
                    || item[0].ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                result[i] = item[0].GetString();
            }

            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>一组要合并进同一个请求的文本：原下标 + 原文。</summary>
    private readonly record struct TextGroup(int[] Indices, string[] Texts);
}
