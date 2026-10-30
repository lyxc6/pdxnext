namespace PdxLauncher.Core.Online.Translation;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;

/// <summary>
/// 翻译服务：缓存优先，缓存未命中才请求引擎；引擎按模式选择与回落。
/// <para>
/// 模式语义：
/// <list type="bullet">
/// <item><see cref="TranslationEngineModes.Auto"/>：优先自填引擎；没配置或失败时回落到内置免费服务。</item>
/// <item><see cref="TranslationEngineModes.Custom"/>：只用自填引擎；失败就是失败，不回落。</item>
/// <item><see cref="TranslationEngineModes.Builtin"/>：只用内置免费服务。</item>
/// </list>
/// 内置免费服务最多同时 4 个请求，免得把公开接口打爆。
/// </para>
/// </summary>
public sealed class TranslationService
{
    /// <summary>内置免费服务的最大并发请求数。</summary>
    public const int MaxBuiltinConcurrency = 4;

    private readonly ITranslationEngine? _customEngine;
    private readonly ITranslationEngine? _builtinEngine;
    private readonly TranslationCache? _cache;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _builtinGate = new(MaxBuiltinConcurrency, MaxBuiltinConcurrency);

    /// <summary>创建翻译服务。</summary>
    /// <param name="customEngine">自填引擎；null 表示没有。</param>
    /// <param name="builtinEngine">内置免费引擎；null 表示没有。</param>
    /// <param name="cache">译文缓存；null 表示不缓存。</param>
    /// <param name="logger">日志。</param>
    public TranslationService(
        ITranslationEngine? customEngine,
        ITranslationEngine? builtinEngine,
        TranslationCache? cache,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _customEngine = customEngine;
        _builtinEngine = builtinEngine;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// 翻译一批文本；返回数组与入参一一对应，失败的位置为 null。
    /// </summary>
    /// <param name="texts">原文列表。</param>
    /// <param name="options">选项（引擎模式、目标语言、缓存天数）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<string?>> TranslateAsync(
        IReadOnlyList<string> texts,
        TranslationOptions options,
        CancellationToken cancellationToken)
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

        if (mode == TranslationEngineModes.Builtin)
        {
            return await RunBuiltinAsync(texts, options, cancellationToken).ConfigureAwait(false);
        }

        var customAvailable = _customEngine is not null && _customEngine.IsConfigured;
        if (customAvailable)
        {
            var customOutcomes = await RunCustomAsync(texts, options, cancellationToken).ConfigureAwait(false);
            if (mode == TranslationEngineModes.Custom)
            {
                return customOutcomes;
            }

            // auto：自填引擎全成功就用它；只要有失败项，就用内置服务补那些。
            if (customOutcomes.All(item => item.Text is not null))
            {
                return customOutcomes;
            }

            var missing = new List<int>();
            for (var i = 0; i < customOutcomes.Length; i++)
            {
                if (customOutcomes[i].Text is null)
                {
                    missing.Add(i);
                }
            }

            var fallbackTexts = missing.Select(index => texts[index]).ToList();
            var fallbackOutcomes = await RunBuiltinAsync(fallbackTexts, options, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < missing.Count && i < fallbackOutcomes.Length; i++)
            {
                customOutcomes[missing[i]] = fallbackOutcomes[i];
            }

            return customOutcomes;
        }

        if (mode == TranslationEngineModes.Custom)
        {
            _logger.Warn("翻译模式为「仅自填 API」，但没有配置完整的接口地址与密钥");
            return new TranslationOutcome[texts.Count];
        }

        return await RunBuiltinAsync(texts, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>走自填引擎。</summary>
    private async Task<TranslationOutcome[]> RunCustomAsync(
        IReadOnlyList<string> texts,
        TranslationOptions options,
        CancellationToken cancellationToken)
    {
        if (_customEngine is null)
        {
            return new TranslationOutcome[texts.Count];
        }

        try
        {
            var results = await _customEngine.TranslateBatchAsync(texts, options.TargetLanguage, cancellationToken)
                .ConfigureAwait(false);
            return ToOutcomes(results, _customEngine.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("自填翻译引擎调用失败：" + ex.Message);
            return new TranslationOutcome[texts.Count];
        }
    }

    /// <summary>走内置免费引擎（限并发）。</summary>
    private async Task<TranslationOutcome[]> RunBuiltinAsync(
        IReadOnlyList<string> texts,
        TranslationOptions options,
        CancellationToken cancellationToken)
    {
        if (_builtinEngine is null)
        {
            return new TranslationOutcome[texts.Count];
        }

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
            var results = await _builtinEngine.TranslateBatchAsync(texts, options.TargetLanguage, cancellationToken)
                .ConfigureAwait(false);
            return ToOutcomes(results, _builtinEngine.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("内置翻译引擎调用失败：" + ex.Message);
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

    /// <summary>查缓存；自填引擎的键优先，其次内置引擎的键。</summary>
    private async Task<string?> TryGetCachedAsync(
        TranslationOptions options,
        string text,
        CancellationToken cancellationToken)
    {
        if (_cache is null)
        {
            return null;
        }

        var engineIds = new List<string>();
        if (_customEngine is not null)
        {
            engineIds.Add(_customEngine.Id);
        }

        if (_builtinEngine is not null)
        {
            engineIds.Add(_builtinEngine.Id);
        }

        foreach (var engineId in engineIds)
        {
            try
            {
                var cached = await _cache
                    .TryGetAsync(engineId, options.TargetLanguage, text, options.CacheDays, cancellationToken)
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
}
