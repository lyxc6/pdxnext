namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Online.Translation;
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
    private readonly SemaphoreSlim _gate = new(MaxConcurrency, MaxConcurrency);
    private readonly object _stateLock = new();

    private CancellationTokenSource _settingsCts = new();
    private bool _disposed;

    /// <summary>创建协调器。</summary>
    /// <param name="transport">翻译专用 HTTP 传输。</param>
    /// <param name="cache">译文缓存；null 表示不缓存。</param>
    /// <param name="logger">日志。</param>
    public TranslationCoordinator(
        ITranslationTransport transport,
        TranslationCache? cache,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(logger);

        _transport = transport;
        _cache = cache;
        _logger = logger;

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
            return await service.TranslateAsync(texts, options, linked.Token).ConfigureAwait(false);
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

    /// <summary>按当前设置临时构建翻译服务（引擎随设置变，不能提前烤好）。</summary>
    private TranslationService CreateService(TranslationOptions options)
    {
        ITranslationEngine? custom = null;
        if (options.HasCustomConfiguration)
        {
            custom = new OpenAiCompatibleTranslationEngine(
                _transport,
                _logger,
                options.BaseUrl,
                options.ApiKey,
                options.Model);
        }

        var builtin = new FreeTranslationEngine(_transport, _logger);
        return new TranslationService(custom, builtin, _cache, _logger);
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
