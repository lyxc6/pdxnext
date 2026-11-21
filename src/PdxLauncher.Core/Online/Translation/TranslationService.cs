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
    private readonly TranslationNegativeCache? _negativeCache;
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
    /// <param name="negativeCache">短 TTL 的翻不出来标记；null 表示不记。由调用方跨批次持有。</param>
    public TranslationService(
        IReadOnlyList<ITranslationEngine>? engines,
        TranslationCache? cache,
        IAppLogger logger,
        TranslationNegativeCache? negativeCache = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _engines = engines is null ? [] : engines.Where(engine => engine is not null).ToArray();
        _cache = cache;
        _negativeCache = negativeCache;
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
    /// <param name="bypassCache">
    /// 为 true 时跳过读缓存，全部文本直接交给引擎（「刷新译文」用）；结果照常写回缓存覆盖旧值。
    /// </param>
    public async Task<IReadOnlyList<string?>> TranslateAsync(
        IReadOnlyList<string> texts,
        TranslationOptions options,
        CancellationToken cancellationToken,
        Action<int, string?>? onItemTranslated = null,
        bool bypassCache = false)
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

            // 短 TTL 的翻不出来标记：本地模型对同一批短标题反复空转时，直接跳过引擎。
            // 「刷新译文」必须绕过它，否则用户点了重试还是不翻。
            var negativeKey = _negativeCache is null || bypassCache
                ? string.Empty
                : TranslationNegativeCache.BuildKey(options, text);
            if (negativeKey.Length > 0 && _negativeCache!.IsSuppressed(negativeKey, DateTimeOffset.UtcNow))
            {
                continue;
            }

            var cached = bypassCache ? null : await TryGetCachedAsync(options, text, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                if (IsIdentityTranslation(text, cached))
                {
                    // 与原文逐字相同等于没翻（中文原文最常见）：不展示、不算失败。
                    continue;
                }

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

        // 只有真有未命中文本、要请求引擎时才登记在途（D）：
        // 旧实现从「排队等协调器并发闸门」就开始亮，缓存全命中的批次也要等闸门 +
        // 查完缓存才灭，让用户误以为早就翻过的内容又重翻了一遍。
        var engineActivity = pending.Count > 0;
        if (engineActivity)
        {
            OnEngineActivityStarted?.Invoke();
        }

        try
        {
            var pendingTexts = pending.Select(index => texts[index]).ToList();

            // 流式回调只负责「立刻让界面看到译文」：同步、去重，不在这里 await 写缓存
            // （回调是 Action，异步写盘会是 fire-and-forget，还可能拿到已取消的 token）。
            var reported = new bool[texts.Count];
            var outcomes = await TranslateUncachedAsync(
                pendingTexts,
                options,
                cancellationToken,
                (position, outcome) =>
                {
                    if (position < 0 || position >= pending.Count)
                    {
                        return;
                    }

                    var index = pending[position];
                    var translated = IsIdentityTranslation(texts[index], outcome.Text) ? null : outcome.Text;
                    if (string.IsNullOrEmpty(translated) || reported[index])
                    {
                        return;
                    }

                    reported[index] = true;
                    NotifyItemTranslated(onItemTranslated, index, translated);
                }).ConfigureAwait(false);

            // 缓存写入按最终结果统一收口：await 落盘，只写一次，失败只记日志。
            for (var i = 0; i < pending.Count && i < outcomes.Length; i++)
            {
                var outcome = outcomes[i];
                var source = texts[pending[i]];
                var translated = IsIdentityTranslation(source, outcome.Text) ? null : outcome.Text;
                results[pending[i]] = translated;
                if (string.IsNullOrEmpty(translated))
                {
                    // 翻不出来也记一笔短 TTL，免得工坊每次翻页都把同一批标题再喂给本地模型。
                    if (_negativeCache is not null)
                    {
                        _negativeCache.Add(
                            TranslationNegativeCache.BuildKey(options, source),
                            DateTimeOffset.UtcNow);
                    }
                    continue;
                }

                if (outcome.EngineId is not null)
                {
                    await TrySetCachedAsync(outcome.EngineId, options, source, translated!, cancellationToken)
                        .ConfigureAwait(false);
                }

                // 非流式路径没在过程中回调过，这里兜底补一次上报。
                if (!reported[pending[i]])
                {
                    NotifyItemTranslated(onItemTranslated, pending[i], translated);
                }
            }

            return results;
        }
        finally
        {
            if (engineActivity)
            {
                OnEngineActivityEnded?.Invoke();
            }
        }
    }

    /// <summary>
    /// 真有未命中文本、即将请求引擎时触发（应用层据此显示「正在翻译…」）；
    /// 与 <see cref="OnEngineActivityEnded"/> 成对，默认无订阅。
    /// </summary>
    public Action? OnEngineActivityStarted { get; set; }

    /// <summary>本批引擎请求全部结束时触发；与 <see cref="OnEngineActivityStarted"/> 成对。</summary>
    public Action? OnEngineActivityEnded { get; set; }

    /// <summary>
    /// 译文与原文去掉首尾空白后逐字相同 = 没翻出来（本地模型遇到中文原文、或名字本来就不该译时
    /// 会原样回一句）。这类结果不展示、不算失败、也不写缓存：否则界面上会多出一个和标题一样的「译文」框，
    /// 详情页还会因为「整批都是这种结果」弹出一条假的翻译失败提示。
    /// </summary>
    private static bool IsIdentityTranslation(string source, string? translation)
    {
        return !string.IsNullOrEmpty(translation)
            && string.Equals(translation.Trim(), source.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 翻译一段文本；失败返回 null。
    /// </summary>
    /// <param name="text">原文。</param>
    /// <param name="options">选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="bypassCache">为 true 时跳过读缓存，直接交给引擎；结果照常写回缓存覆盖旧值。</param>
    public async Task<string?> TranslateAsync(
        string text,
        TranslationOptions options,
        CancellationToken cancellationToken,
        bool bypassCache = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var results = await TranslateAsync([text], options, cancellationToken, bypassCache: bypassCache).ConfigureAwait(false);
        return results.Count > 0 ? results[0] : null;
    }

    /// <summary>没命中缓存的一批文本交给引擎；每条译文就绪时立刻回调。</summary>
    /// <param name="onOutcome">
    /// 某引擎返回某条译文时触发（位置对应 <paramref name="texts"/> 索引）。
    /// 只负责即时上报进度；写缓存在 TranslateAsync 的最终汇总里统一 await。
    /// </param>
    private async Task<TranslationOutcome[]> TranslateUncachedAsync(
        IReadOnlyList<string> texts,
        TranslationOptions options,
        CancellationToken cancellationToken,
        Action<int, TranslationOutcome>? onOutcome = null)
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
            var engineOutcomes = await RunEngineAsync(
                engine,
                batch,
                options,
                cancellationToken,
                // 引擎回调给的是 batch 内位置（对齐 remaining），外层按原文在 texts 里的下标
                // 记账；回落时 remaining 是子集，必须先把 batch 位置换回原始下标，否则译名填错行。
                onOutcome is null ? null : (position, text) =>
                {
                    if (position >= 0 && position < remaining.Count)
                    {
                        onOutcome(remaining[position], new TranslationOutcome(text, engine.Id));
                    }
                }).ConfigureAwait(false);
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
                // RunEngineAsync 对流式引擎已逐条回调过；这里只补非流式引擎的进度，
                // 位置必须用 pending 下标 remaining[i]（回落时 i 只是子集内的序号）。
                if (engine is not IStreamingTranslationEngine)
                {
                    onOutcome?.Invoke(remaining[i], outcome);
                }
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

    /// <summary>走一个引擎（限并发）；支持流式引擎逐条上报进度。</summary>
    private async Task<TranslationOutcome[]> RunEngineAsync(
        ITranslationEngine engine,
        IReadOnlyList<string> texts,
        TranslationOptions options,
        CancellationToken cancellationToken,
        Action<int, string?>? onItem = null)
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
            if (engine is IStreamingTranslationEngine streaming)
            {
                // 本地 2B / 自填 API 的逐条路径：每翻出一条就回调，界面立刻回填，
                // 不再等整批（22 条 × 数百 ms~数秒）跑完才批量回调。
                var streamed = new string?[texts.Count];
                await streaming.TranslateBatchStreamingAsync(
                    texts,
                    options.TargetLanguage,
                    cancellationToken,
                    (index, text) =>
                    {
                        if (index >= 0 && index < streamed.Length)
                        {
                            streamed[index] = text;
                        }
                        onItem?.Invoke(index, text);
                    }).ConfigureAwait(false);
                return ToOutcomes(streamed, engine.Id);
            }

            // 非流式引擎等整批返回；逐条进度由 TranslateUncachedAsync 的汇总循环统一上报，
            // 避免这里和那里重复回调同一条。
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
