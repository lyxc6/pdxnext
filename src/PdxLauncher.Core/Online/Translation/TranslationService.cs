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
        var allowFallback = mode == TranslationEngineModes.Auto;

        var candidates = _engines.Where(engine => engine.IsConfigured).ToList();
        if (candidates.Count == 0)
        {
            _logger.Warn("翻译模式为「" + DescribeMode(mode) + "」，但没有配置完整的可用引擎");
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
            for (var i = 0; i < remaining.Count && i < engineOutcomes.Length; i++)
            {
                var outcome = engineOutcomes[i];
                if (outcome.Text is null)
                {
                    stillMissing.Add(remaining[i]);
                    continue;
                }

                outcomes[remaining[i]] = outcome;
            }

            if (!allowFallback)
            {
                break;
            }

            remaining = stillMissing;
        }

        return outcomes;
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
        _ => "自动",
    };
}
