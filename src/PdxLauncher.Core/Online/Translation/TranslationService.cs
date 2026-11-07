namespace PdxLauncher.Core.Online.Translation;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;

/// <summary>
/// 翻译服务：缓存优先，缓存未命中才请求引擎；引擎按模式选择与回落。
/// <para>
/// 模式语义：
/// <list type="bullet">
/// <item><see cref="TranslationEngineModes.Auto"/>：按列表顺序依次尝试，前面的引擎没配或失败时由后面的补齐。</item>
/// <item>其余模式（<see cref="TranslationEngineModes.Custom"/>、<see cref="TranslationEngineModes.Builtin"/>、
/// <see cref="TranslationEngineModes.Microsoft"/>、<see cref="TranslationEngineModes.GoogleCloud"/>）：
/// 只用列表里的第一个已配置引擎，失败就是失败，不回落。</item>
/// </list>
/// 列表里没配好的引擎会被跳过；引擎最多同时 4 个请求，免得把公开接口打爆。
/// </para>
/// </summary>
public sealed class TranslationService
{
    /// <summary>引擎的最大并发请求数。</summary>
    public const int MaxBuiltinConcurrency = 4;

    private readonly ITranslationEngine[] _engines;
    private readonly TranslationCache? _cache;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _builtinGate = new(MaxBuiltinConcurrency, MaxBuiltinConcurrency);

    /// <summary>本批里各引擎失败的说明（形如 <c>mymemory: 免费额度已用完…</c>），按尝试顺序排列。</summary>
    private readonly List<string> _engineFailureNotes = [];

    /// <summary>失败说明的整体长度上限：太长会把界面顶部的提示条撑成一段论文。</summary>
    private const int MaxFailureNoteLength = 300;

    /// <summary>
    /// 最近一次整批翻译「一条都没翻出来」时的原因；有任意一条成功就是 null。
    /// <para>
    /// 用来把「静默失败」变成可解释的提示：引擎不可用时用户至少知道去找设置页，
    /// 而不是盯着没变化的创意工坊怀疑功能不存在。多个引擎都失败时逐条列出，
    /// 免得只看到第一个引擎的笼统备注（例如额度用完的 MyMemory），却不知道后面那个为什么也不行。
    /// </para>
    /// </summary>
    public string? LastFailure { get; private set; }

    /// <summary>创建翻译服务。</summary>
    /// <param name="engines">引擎列表，顺序即回落优先级；null 或空表示没有可用引擎。</param>
    /// <param name="cache">译文缓存；null 表示不缓存。</param>
    /// <param name="logger">日志。</param>
    public TranslationService(
        IReadOnlyList<ITranslationEngine>? engines,
        TranslationCache? cache,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _engines = engines is null ? [] : engines.Where(engine => engine is not null).ToArray();
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// 翻译一批文本；返回数组与入参一一对应，失败的位置为 null。
    /// </summary>
    /// <param name="texts">原文列表。</param>
    /// <param name="options">选项（引擎模式、目标语言、缓存天数）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="onItemTranslated">
    /// 每条译文就绪时的进度回调（索引与 <paramref name="texts"/> 对应）：缓存命中和引擎结果都会回调。
    /// 可能在线程池线程上触发，写界面属性前要自行切回 UI 线程。
    /// </param>
    public async Task<IReadOnlyList<string?>> TranslateAsync(
        IReadOnlyList<string> texts,
        TranslationOptions options,
        CancellationToken cancellationToken,
        Action<int, string?>? onItemTranslated = null)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var results = new string?[texts.Count];
        if (texts.Count == 0 || !options.Enabled)
        {
            return results;
        }

        var pending = new List<int>();
        for (var i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            if (string.IsNullOrWhiteSpace(text))
            {
                results[i] = text;
                continue;
            }

            var cached = await TryGetCachedAsync(options, text, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                results[i] = cached;
                NotifyItemTranslated(onItemTranslated, i, cached);
                continue;
            }

            pending.Add(i);
        }

        if (pending.Count == 0)
        {
            return results;
        }

        var pendingTexts = pending.Select(index => texts[index]).ToList();
        var outcomes = await TranslateUncachedAsync(pendingTexts, options, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < pending.Count && i < outcomes.Length; i++)
        {
            var outcome = outcomes[i];
            results[pending[i]] = outcome.Text;
            if (!string.IsNullOrEmpty(outcome.Text) && outcome.EngineId is not null)
            {
                await TrySetCachedAsync(outcome.EngineId, options, texts[pending[i]], outcome.Text, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(outcome.Text))
            {
                NotifyItemTranslated(onItemTranslated, pending[i], outcome.Text);
            }
        }

        return results;
    }

    /// <summary>
    /// 翻译一段文本；失败返回 null。
    /// </summary>
    /// <param name="text">原文。</param>
    /// <param name="options">选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<string?> TranslateAsync(string text, TranslationOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var results = await TranslateAsync([text], options, cancellationToken).ConfigureAwait(false);
        return results.Count > 0 ? results[0] : null;
    }

    /// <summary>没命中缓存的一批文本交给引擎。</summary>
    private async Task<TranslationOutcome[]> TranslateUncachedAsync(
        IReadOnlyList<string> texts,
        TranslationOptions options,
        CancellationToken cancellationToken)
    {
        var mode = TranslationEngineModes.Normalize(options.EngineMode);
        var allowFallback = mode == TranslationEngineModes.Auto;
        _engineFailureNotes.Clear();
        LastFailure = null;

        var candidates = _engines.Where(engine => engine.IsConfigured).ToList();
        if (candidates.Count == 0)
        {
            _logger.Warn("翻译模式为「" + DescribeMode(mode) + "」，但没有配置完整的可用引擎");
            LastFailure = "没有可用的翻译引擎（当前模式：" + DescribeMode(mode) + "）";
            return new TranslationOutcome[texts.Count];
        }

        var outcomes = new TranslationOutcome[texts.Count];
        var remaining = Enumerable.Range(0, texts.Count).ToList();

        foreach (var engine in candidates)
        {
            if (remaining.Count == 0 || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var batch = remaining.Select(index => texts[index]).ToList();
            var engineOutcomes = await RunEngineAsync(engine, batch, options, cancellationToken).ConfigureAwait(false);
            var stillMissing = new List<int>();
            var succeeded = 0;
            for (var i = 0; i < remaining.Count && i < engineOutcomes.Length; i++)
            {
                var outcome = engineOutcomes[i];
                if (outcome.Text is null)
                {
                    stillMissing.Add(remaining[i]);
                    continue;
                }

                outcomes[remaining[i]] = outcome;
                succeeded++;
            }

            // 没抛异常但一条都没翻出来（HTTP 非 2xx、或响应格式变了）也要留个话，
            // 否则界面只能显示「失败」，用户无从判断该改哪。
            if (succeeded == 0 && batch.Count > 0)
            {
                AddFailureNote(engine, null);
            }

            if (!allowFallback)
            {
                break;
            }

            remaining = stillMissing;
        }

        var anySuccess = outcomes.Any(outcome => outcome.Text is not null);
        if (!anySuccess && texts.Count > 0)
        {
            LastFailure = DescribeFailures();
            _logger.Warn("翻译全部失败：" + LastFailure);
        }

        return outcomes;
    }

    /// <summary>
    /// 通知调用方某一条译文已就绪；回调异常只记日志，不能把整批翻译打断。
    /// </summary>
    /// <param name="callback">调用方传入的进度回调；null 表示不需要进度。</param>
    /// <param name="index">原文在入参列表里的索引。</param>
    /// <param name="text">译文。</param>
    private void NotifyItemTranslated(Action<int, string?>? callback, int index, string? text)
    {
        if (callback is null)
        {
            return;
        }

        try
        {
            callback(index, text);
        }
        catch (Exception ex)
        {
            _logger.Warn("翻译进度回调失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 记下某个引擎这轮为什么没译出来（优先用它自己报的原因）。
    /// </summary>
    /// <param name="engine">引擎。</param>
    /// <param name="exceptionMessage">引擎抛异常时的消息；null 表示没抛异常、只是没拿到译文。</param>
    private void AddFailureNote(ITranslationEngine engine, string? exceptionMessage)
    {
        var reason = exceptionMessage ?? engine.LastFailure ?? "未返回译文";
        var note = engine.Id + ": " + reason;
        if (!_engineFailureNotes.Contains(note, StringComparer.Ordinal))
        {
            _engineFailureNotes.Add(note);
        }
    }

    /// <summary>把各引擎的失败原因拼成一句提示（超长时截断）。</summary>
    private string DescribeFailures()
    {
        if (_engineFailureNotes.Count == 0)
        {
            return "所有可用引擎都没有返回译文";
        }

        var text = string.Join("；", _engineFailureNotes);
        return text.Length <= MaxFailureNoteLength ? text : text[..MaxFailureNoteLength] + "…";
    }

    /// <summary>走一个引擎（限并发）。</summary>
    private async Task<TranslationOutcome[]> RunEngineAsync(
        ITranslationEngine engine,
        IReadOnlyList<string> texts,
        TranslationOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            await _builtinGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new TranslationOutcome[texts.Count];
        }

        try
        {
            var results = await engine.TranslateBatchAsync(texts, options.TargetLanguage, cancellationToken)
                .ConfigureAwait(false);
            return ToOutcomes(results, engine.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("翻译引擎「" + engine.Id + "」调用失败：" + ex.Message);
            AddFailureNote(engine, ex.Message);
            return new TranslationOutcome[texts.Count];
        }
        finally
        {
            _builtinGate.Release();
        }
    }

    /// <summary>把引擎返回值包装成带引擎标识的结果（写缓存时要知道写到哪个引擎的键下）。</summary>
    private static TranslationOutcome[] ToOutcomes(IReadOnlyList<string?> results, string engineId)
    {
        var outcomes = new TranslationOutcome[results.Count];
        for (var i = 0; i < results.Count; i++)
        {
            outcomes[i] = new TranslationOutcome(results[i], engineId);
        }

        return outcomes;
    }

    /// <summary>查缓存；按引擎列表顺序逐个查键。</summary>
    private async Task<string?> TryGetCachedAsync(
        TranslationOptions options,
        string text,
        CancellationToken cancellationToken)
    {
        if (_cache is null)
        {
            return null;
        }

        foreach (var engine in _engines)
        {
            try
            {
                var cached = await _cache
                    .TryGetAsync(engine.Id, options.TargetLanguage, text, options.CacheDays, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrEmpty(cached))
                {
                    return cached;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn("译文缓存读取失败：" + ex.Message);
            }
        }

        return null;
    }

    /// <summary>写缓存；失败只记日志。</summary>
    private async Task TrySetCachedAsync(
        string engineId,
        TranslationOptions options,
        string text,
        string translation,
        CancellationToken cancellationToken)
    {
        if (_cache is null)
        {
            return;
        }

        try
        {
            await _cache.SetAsync(engineId, options.TargetLanguage, text, translation, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("译文缓存写入失败：" + ex.Message);
        }
    }

    /// <summary>模式的人话描述，只用于日志。</summary>
    private static string DescribeMode(string mode) => mode switch
    {
        TranslationEngineModes.Custom => "仅自填 API",
        TranslationEngineModes.Builtin => "仅内置免费服务",
        TranslationEngineModes.Microsoft => "仅微软翻译",
        TranslationEngineModes.GoogleCloud => "仅谷歌云翻译",
        TranslationEngineModes.Local => "仅本地离线引擎",
        TranslationEngineModes.IndexTranslate => "仅本地 Index-Translate",
        TranslationEngineModes.Bergamot => "仅本地 Bergamot",
        _ => "自动",
    };
}
