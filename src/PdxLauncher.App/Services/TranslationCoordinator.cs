namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Online.Translation;
using PdxLauncher.Core.Online.Translation.Local;
using PdxLauncher.Core.Text;

/// <summary>
/// 应用层翻译入口：把「当前设置 + 界面语言」翻译成一次翻译任务。
/// <para>
/// 三件事在这里收口：
/// <list type="number">
/// <item>引擎按当前设置临时构建（自填 API 的地址/密钥/模型随时可能改，不能把引擎烤进构造函数）。</item>
/// <item>用户自填的 Base URL 主机要临时授权给白名单（精确匹配，不放行子域）。</item>
/// <item>设置变了就把在途批次作废：旧设置下翻出来的译文没有意义。</item>
/// </list>
/// 译文缓存与引擎无关，挂在服务上复用。
/// </para>
/// </summary>
public sealed class TranslationCoordinator : IDisposable
{
    /// <summary>同时进行的翻译批次数上限。</summary>
    public const int MaxConcurrency = 4;

    private readonly ITranslationTransport _transport;
    private readonly TranslationCache? _cache;
    private readonly IAppLogger _logger;
    private readonly LocalTranslationRuntime? _local;
    private readonly SemaphoreSlim _gate = new(MaxConcurrency, MaxConcurrency);
    private readonly object _stateLock = new();

    private CancellationTokenSource _settingsCts = new();
    private bool _disposed;

    /// <summary>创建协调器。</summary>
    /// <param name="transport">翻译专用 HTTP 传输。</param>
    /// <param name="cache">译文缓存；null 表示不缓存。</param>
    /// <param name="logger">日志。</param>
    /// <param name="local">本地离线翻译运行时；null 表示不提供本地引擎。</param>
    public TranslationCoordinator(
        ITranslationTransport transport,
        TranslationCache? cache,
        IAppLogger logger,
        LocalTranslationRuntime? local = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(logger);

        _transport = transport;
        _cache = cache;
        _logger = logger;
        _local = local;

        // 界面语言变了，目标语言（默认跟随界面）也会变：作废在途批次并重新授权主机。
        Localizer.Instance.LanguageChanged += OnLanguageChanged;

        // 自填 API 的主机要在第一次翻译前就授权好。
        AuthorizeCustomHost();
    }

    /// <summary>设置或界面语言变了：作废在途批次，重新授权自填 API 的主机。</summary>
    public void NotifySettingsChanged()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            var previous = _settingsCts;
            _settingsCts = new CancellationTokenSource();
            previous.Cancel();
            previous.Dispose();
        }

        AuthorizeCustomHost();
    }

    /// <summary>当前翻译选项（设置 + 界面语言）。</summary>
    public TranslationOptions CurrentOptions =>
        TranslationOptions.FromSettings(AppServices.Settings, Localizer.Instance.Language);

    /// <summary>翻译功能当前是否开启。</summary>
    public bool IsEnabled => CurrentOptions.Enabled;

    /// <summary>
    /// 探针文本：设置页「测试翻译」用它让每个引擎各翻一句。
    /// 短句 + 不写缓存，重复点击不会污染真实译文缓存。
    /// </summary>
    public const string ProbeText = "Hello, this is a translation test.";

    /// <summary>
    /// 最近一次批次「一条都没翻出来」时的原因（形如 <c>mymemory: 未返回译文</c>）；
    /// 有任意一条成功就是 null。界面据此决定要不要提示用户去设置页。
    /// </summary>
    public string? LastFailure { get; private set; }

    /// <summary>
    /// 在途状态：只用来看「此刻有没有翻译请求在跑」，界面据此显示「正在翻译…」的环形进度与文字。
    /// 计数点在 <see cref="TranslateBatchAsync"/>，所有翻译调用都会经过那里。
    /// </summary>
    public TranslationActivityMonitor Activity { get; } = new();

    /// <summary>清空译文缓存（设置页的「清理缓存」用）；没有缓存时什么都不做。</summary>
    public void ClearCache() => _cache?.Clear();

    /// <summary>
    /// 翻译一段文本；失败或未开启返回 null。
    /// </summary>
    /// <param name="text">原文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<string?> TranslateAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var results = await TranslateBatchAsync([text], cancellationToken).ConfigureAwait(false);
        return results.Count > 0 ? results[0] : null;
    }

    /// <summary>
    /// 翻译一批文本；返回数组与入参一一对应，失败的位置为 null。
    /// </summary>
    /// <param name="texts">原文列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<string?>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var results = new string?[texts.Count];
        if (texts.Count == 0)
        {
            return results;
        }

        var options = CurrentOptions;
        if (!options.Enabled)
        {
            return results;
        }

        // 自填 API 的主机每次都确认一遍授权：设置改了地址、或者协调器先于设置装配时都靠这一步兜住。
        AuthorizeCustomHost();

        // 从这里到批次返回都算「在途」：排队等并发闸门的批次也计入，
        // 否则并发 4 以外排队的那些批次会让界面的提示提前消失。
        using var activity = Activity.Begin();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, SettingsToken());
        try
        {
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return results;
        }

        try
        {
            var service = CreateService(options);
            var translated = await service.TranslateAsync(texts, options, linked.Token).ConfigureAwait(false);

            // 整批失败的原因留在协调器上，页面用它拼一句可读提示；
            // 成功时置空，免得旧原因一直挂着。
            LastFailure = service.LastFailure;
            return translated;
        }
        catch (OperationCanceledException)
        {
            return results;
        }
        catch (Exception ex)
        {
            _logger.Warn("翻译失败：" + ex.Message);
            return results;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 翻译整份 Markdown 文档；结构保持不变，译不出来的地方保留原文。
    /// </summary>
    /// <param name="document">原文文档。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<MarkdownDocument> TranslateDocumentAsync(
        MarkdownDocument? document,
        CancellationToken cancellationToken)
    {
        if (document is null || document.IsEmpty)
        {
            return MarkdownDocument.Empty;
        }

        var options = CurrentOptions;
        if (!options.Enabled)
        {
            return document;
        }

        var translator = new MarkdownTranslator(async (text, token) =>
        {
            var results = await TranslateBatchAsync([text], token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        });

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, SettingsToken());
        try
        {
            return await translator.TranslateAsync(document, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return document;
        }
        catch (Exception ex)
        {
            _logger.Warn("翻译文档失败：" + ex.Message);
            return document;
        }
    }

    /// <summary>
    /// 逐个引擎各译一句探针文本，供设置页「测试翻译」判断到底哪个引擎能用。
    /// <para>
    /// 每个引擎单独起一个服务、且不带缓存：这里要的是真实连通性，不是缓存命中；
    /// 探测不看当前模式（模式只决定正式翻译走哪条链），所有「已配置」的引擎都会试一次。
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<TranslationProbeResult>> ProbeAsync(CancellationToken cancellationToken)
    {
        AuthorizeCustomHost();

        // 探测与「翻译开关」无关：关着也能测，否则用户没法先验证再开启。
        var options = CurrentOptions with { Enabled = true };
        var candidates = CreateProbeEngines(options);

        var results = new List<TranslationProbeResult>(candidates.Count);
        foreach (var (key, engine) in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var service = new TranslationService([engine], cache: null, _logger);
            string? text = null;
            _logger.Info("探测翻译引擎：" + engine.Id);
            try
            {
                var translated = await service.TranslateAsync([ProbeText], options, cancellationToken)
                    .ConfigureAwait(false);
                text = translated.Count > 0 ? translated[0] : null;
                _logger.Info("探测结果（" + engine.Id + "）：" + (text is null ? "无译文" : "有译文"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warn("翻译探测失败（" + engine.Id + "）：" + ex.Message);
            }

            var ok = !string.IsNullOrWhiteSpace(text);
            results.Add(new TranslationProbeResult(key, ok, ok ? text! : (service.LastFailure ?? "未返回译文")));
        }

        return results;
    }

    /// <summary>探测用引擎清单：每个引擎单独一项，与当前模式无关。</summary>
    private List<(string Key, ITranslationEngine Engine)> CreateProbeEngines(TranslationOptions options)
    {
        var engines = new List<(string Key, ITranslationEngine Engine)>();

        if (options.HasCustomConfiguration)
        {
            engines.Add((TranslationEngineModes.Custom, new OpenAiCompatibleTranslationEngine(
                _transport,
                _logger,
                options.BaseUrl,
                options.ApiKey,
                options.Model)));
        }

        if (options.HasMicrosoftConfiguration)
        {
            engines.Add((TranslationEngineModes.Microsoft, new MicrosoftTranslatorEngine(
                _transport,
                _logger,
                options.MicrosoftKey,
                options.MicrosoftRegion)));
        }

        if (options.HasGoogleCloudConfiguration)
        {
            engines.Add((TranslationEngineModes.GoogleCloud, new GoogleCloudTranslationEngine(
                _transport,
                _logger,
                options.GoogleKey)));
        }

        engines.Add((TranslationEngineModes.MyMemory, new MyMemoryTranslationEngine(_transport, _logger)));
        engines.Add((TranslationEngineModes.Builtin, new FreeTranslationEngine(_transport, _logger)));

        return engines;
    }

    /// <summary>
    /// 按当前设置临时构建翻译服务（引擎随设置变，不能提前烤好）。
    /// <para>
    /// 引擎列表的顺序即回落优先级：「自动」按 本地离线 → 自填 API → 微软 → 谷歌云 → MyMemory → 内置免费 依次尝试；
    /// 其余模式只放对应的引擎，失败不回落。
    /// MyMemory 排在谷歌免费前面，是因为谷歌那个公开端点在不少网络里根本连不上
    /// （TLS 握手失败），排在前面等于每页都先白等一次超时。
    /// </para>
    /// </summary>
    private TranslationService CreateService(TranslationOptions options)
    {
        var engines = new List<ITranslationEngine>();
        var mode = TranslationEngineModes.Normalize(options.EngineMode);

        // 本地离线引擎排在最前：装好模型后不需要联网，也不需要任何凭据。
        if (_local is not null &&
            (mode == TranslationEngineModes.Auto ||
             mode == TranslationEngineModes.Local ||
             mode == TranslationEngineModes.IndexTranslate ||
             mode == TranslationEngineModes.Bergamot))
        {
            var local = _local.CreateEngines();
            if (mode == TranslationEngineModes.IndexTranslate)
            {
                engines.AddRange(local.Where(engine => engine.Id == IndexTranslateEngine.EngineId));
            }
            else if (mode == TranslationEngineModes.Bergamot)
            {
                engines.AddRange(local.Where(engine => engine.Id == BergamotTranslationEngine.EngineId));
            }
            else
            {
                engines.AddRange(local);
            }
        }

        if (mode == TranslationEngineModes.Auto || mode == TranslationEngineModes.Custom)
        {
            if (options.HasCustomConfiguration)
            {
                engines.Add(new OpenAiCompatibleTranslationEngine(
                    _transport,
                    _logger,
                    options.BaseUrl,
                    options.ApiKey,
                    options.Model));
            }
        }

        if (mode == TranslationEngineModes.Auto || mode == TranslationEngineModes.Microsoft)
        {
            if (options.HasMicrosoftConfiguration)
            {
                engines.Add(new MicrosoftTranslatorEngine(
                    _transport,
                    _logger,
                    options.MicrosoftKey,
                    options.MicrosoftRegion));
            }
        }

        if (mode == TranslationEngineModes.Auto || mode == TranslationEngineModes.GoogleCloud)
        {
            if (options.HasGoogleCloudConfiguration)
            {
                engines.Add(new GoogleCloudTranslationEngine(
                    _transport,
                    _logger,
                    options.GoogleKey));
            }
        }

        if (mode == TranslationEngineModes.Auto || mode == TranslationEngineModes.MyMemory)
        {
            engines.Add(new MyMemoryTranslationEngine(_transport, _logger));
        }

        if (mode == TranslationEngineModes.Auto || mode == TranslationEngineModes.Builtin)
        {
            engines.Add(new FreeTranslationEngine(_transport, _logger));
        }

        return new TranslationService(engines, _cache, _logger);
    }

    /// <summary>把用户自填的 Base URL 主机加入精确白名单。</summary>
    private void AuthorizeCustomHost()
    {
        var options = CurrentOptions;
        if (!options.HasCustomConfiguration || options.BaseUrl is null)
        {
            return;
        }

        if (!OnlineHosts.AuthorizeBaseUrl(options.BaseUrl))
        {
            _logger.Warn("翻译接口地址无法解析出主机名，未加入白名单");
        }
    }

    /// <summary>当前设置批次的取消令牌。</summary>
    private CancellationToken SettingsToken()
    {
        lock (_stateLock)
        {
            return _settingsCts.Token;
        }
    }

    /// <summary>界面语言变了：目标语言跟着变，在途批次作废。</summary>
    private void OnLanguageChanged(object? sender, EventArgs e) => NotifySettingsChanged();

    /// <summary>释放资源：取消在途批次，退订界面语言事件。</summary>
    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Localizer.Instance.LanguageChanged -= OnLanguageChanged;

        var cts = _settingsCts;
        _settingsCts = new CancellationTokenSource();
        cts.Cancel();
        cts.Dispose();

        // 不释放 _gate：在途批次可能还在 Release，释放后 Release 会抛 ObjectDisposedException。
    }
}
