namespace PdxLauncher.Core.Online.Translation.Local;

using System.Text;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online.Translation;

/// <summary>
/// 基于 Index-Translate-2B 的本地翻译引擎（进程内 GGUF 推理，无需外部运行时进程）。
/// <para>
/// 走 instTrans 的「硬性约束」提示格式：把术语表写进 prompt，模型会强制对齐专名。
/// 这是三个方向里质量最好的一档，代价是 1.31GB 模型 + 每次翻译几百毫秒。
/// </para>
/// </summary>
public sealed class IndexTranslateEngine : IStreamingTranslationEngine, IDisposable
{
    /// <summary>引擎标识。</summary>
    public const string EngineId = "index-translate";

    /// <summary>单次生成的最大 token 数（长文上限）。</summary>
    public const int MaxTokens = 512;

    /// <summary>短文本（工坊标题这类）的生成上限：空转/回吐时最多白跑这么多步，不再固定 512。</summary>
    public const int ShortTextMaxTokens = 128;

    /// <summary>超过这个长度按长文上限生成；短文本用 <see cref="ShortTextMaxTokens"/>。</summary>
    public const int ShortTextCharLimit = 120;

    /// <summary>同一批连续多少条空译文后熔断剩余条目，避免 30 条标题各跑一遍满额生成。</summary>
    public const int ConsecutiveEmptyBreaker = 3;

    /// <summary>
    /// ChatML 与 think 块的结束记号。llama.cpp 生成到这些记号就该停；
    /// 不设停止序列时，回吐/空输出会一路跑满 <see cref="MaxTokens"/>。
    /// </summary>
    internal static readonly string[] GenerationStopMarkers =
    [
        "<|im_end|>",
        "<|endoftext|>",
        "<|im_start|>",
        "</think>",
    ];

    /// <summary>上下文窗口大小；2B 模型下 4096 的 KV 缓存约多占 60MB，2048 对逐段翻译够用。</summary>
    public const int ContextSize = 2048;

    /// <summary>
    /// 单块输入的最大字符数。提示词里还要塞指令和术语表，所以正文不能把 2048 上下文吃满。
    /// </summary>
    public const int MaxInputChars = 900;

    private const string NewLine = "\n";

    /// <summary>
    /// 按输入长度决定生成上限：短标题用小上限，长简介才用满 512。
    /// 抽成纯函数供自检直接断言，避免上限改了却没人守。
    /// </summary>
    internal static int ResolveMaxTokens(string text) =>
        text.Length <= ShortTextCharLimit ? ShortTextMaxTokens : MaxTokens;

    /// <summary>
    /// llama.cpp 默认按物理核心数开线程；在这台 16 核机器上会把 CPU 打满。
    /// 本地翻译是后台活，限制到 1/4（2–8）足够用，配合停止序列整体更省。
    /// </summary>
    private static int ResolveThreadCount() => Math.Clamp(Environment.ProcessorCount / 4, 2, 8);

    private static readonly string[] SupportedTargets = { "zh", "en" };

    private readonly IAppLogger _logger;
    private readonly LocalTranslationGlossary _glossary;
    private readonly LocalTranslationModelStore? _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();

    private LLamaWeights? _weights;
    private bool _disposed;
    private string? _lastFailure;

    /// <summary>创建引擎。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="glossary">术语词典。</param>
    /// <param name="store">模型仓库；用来判断模型是否已安装。</param>
    public IndexTranslateEngine(
        IAppLogger logger,
        LocalTranslationGlossary glossary,
        LocalTranslationModelStore? store = null)
    {
        _logger = logger;
        _glossary = glossary;
        _store = store;
    }

    /// <inheritdoc />
    public string Id => EngineId;

    /// <inheritdoc />
    public string? LastFailure => Volatile.Read(ref _lastFailure);

    /// <summary>
    /// 引擎是否真的可跑：用 Core 的 <c>IsRunnable</c>，与设置页展示的五态口径一致；
    /// 只看 <c>IsInstalled</c> 会把「缺 config.txt」误报成可用。
    /// </summary>
    public bool IsConfigured => _store is not null
        ? _store.GetInstallState(LocalTranslationModelKind.IndexTranslate).IsRunnable
        : File.Exists(ModelPath);

    private static string ModelPath =>
        Path.Combine(
            LocalTranslationModelStore.GetModelDirectory(LocalTranslationModelKind.IndexTranslate),
            LocalTranslationModelStore.IndexTranslateFileName);

    /// <inheritdoc />
    public async Task<string?> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (!TryResolveTargetLanguage(targetLanguage, out var target))
        {
            Volatile.Write(ref _lastFailure, $"暂不支持的目标语言：{targetLanguage}");
            return null;
        }

        var chunks = TranslationTextSplitter.SplitText(text, MaxInputChars);

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var builder = new StringBuilder();
                foreach (var chunk in chunks)
                {
                    // 一批 30 条标题、每条又要逐 token 生成，取消要能立刻生效，
                    // 否则用户翻页后旧批次还会白占 CPU 几十秒。
                    cancellationToken.ThrowIfCancellationRequested();

                    var output = await InferAsync(BuildPrompt(chunk, target), chunk, cancellationToken)
                        .ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(output))
                    {
                        // 输出为空但没抛异常：状态条会显示「未知」，这里补上真正的原因。
                        Volatile.Write(ref _lastFailure, "本地模型没有返回译文（输出为空）");
                        return null;
                    }

                    // 2B 模型偶尔会把提示词当成正文续写（中文原文、本来没什么可译时尤其明显）。
                    // 先截掉提示词尾巴：只剩提示词就判失败，绝不让【约束要求】那一段进界面或缓存。
                    // 与原文逐字相同的结果由 TranslationService 统一当「没翻」处理（不展示、不算失败）。
                    var hadPromptMarkup = TranslationEchoGuard.ContainsPromptMarkup(output);
                    var cleaned = TranslationEchoGuard.Trim(output);
                    if (cleaned is null)
                    {
                        Volatile.Write(ref _lastFailure, "本地模型只吐回了提示词（已丢弃）");
                        _logger.Warn("Index-Translate 输出只有提示词，已丢弃");
                        return null;
                    }

                    if (hadPromptMarkup)
                    {
                        _logger.Warn("Index-Translate 输出混入提示词，已按标记截断");
                    }

                    builder.Append(cleaned);
                    AppendChunkTail(builder, chunk);
                }

                return builder.ToString().Trim();
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastFailure, ex.Message);
            _logger.Error("Index-Translate 本地翻译失败", ex);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string?>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var results = new string?[texts.Count];
        await TranslateBatchStreamingAsync(texts, targetLanguage, cancellationToken, (i, text) => results[i] = text)
            .ConfigureAwait(false);
        return results;
    }

    /// <inheritdoc />
    public async Task TranslateBatchStreamingAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken,
        Action<int, string?> onItem)
    {
        var consecutiveEmpty = 0;
        for (var i = 0; i < texts.Count; i++)
        {
            // 条目之间的取消点：上一页的 30 条还没翻完时用户已经翻到下一页了。
            cancellationToken.ThrowIfCancellationRequested();

            // 连续 N 条空/回吐说明这个模型对当前内容不出译文：再逐条跑满生成只是白烧 CPU，
            // 剩下几条直接按 null 上报，交给回落链或保留原文。熔断只作用于本批，下批仍会重试。
            if (consecutiveEmpty >= ConsecutiveEmptyBreaker)
            {
                var skipped = texts.Count - i;
                Volatile.Write(ref _lastFailure, $"本地模型连续 {ConsecutiveEmptyBreaker} 条无译文，本批停止（跳过 {skipped} 条）");
                _logger.Warn(_lastFailure);
                for (; i < texts.Count; i++)
                {
                    onItem(i, null);
                }
                return;
            }

            var translated = await TranslateAsync(texts[i], targetLanguage, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(translated) || TranslationEchoGuard.Trim(translated) is null)
            {
                consecutiveEmpty++;
            }
            else
            {
                consecutiveEmpty = 0;
            }
            onItem(i, translated);
        }
    }

    private static bool TryResolveTargetLanguage(string targetLanguage, out string target)
    {
        var normalized = targetLanguage?.ToLowerInvariant() ?? string.Empty;
        foreach (var candidate in SupportedTargets)
        {
            if (normalized.StartsWith(candidate, StringComparison.Ordinal))
            {
                target = candidate;
                return true;
            }
        }

        target = string.Empty;
        return false;
    }

    /// <summary>把分块末尾的分隔符补回来，拼接后的译文才保持原来的换行与空格。</summary>
    private static void AppendChunkTail(StringBuilder builder, string chunk)
    {
        if (chunk.Length == 0)
        {
            return;
        }

        var tail = chunk[^1];
        if (tail == '\n')
        {
            builder.Append('\n');
        }
        else if (tail == ' ')
        {
            builder.Append(' ');
        }
    }

    /// <summary>
    /// 用当前术语词典拼提示词；词典内容每次现读，用户在设置页改完词条立刻生效。
    /// </summary>
    private string BuildPrompt(string text, string target) => BuildPromptText(text, target, _glossary.Entries);

    /// <summary>
    /// 拼提示词：词典为空时用最简指令，有词条时改用 instTrans 的硬约束格式。
    /// <para>
    /// 约束块写在【源文】<b>之前</b>，源文放最后一行。这是一次实测返修：约束块放在源文后面时，
    /// 补全模型译完原文会顺着往下写，把「【约束要求】…只输出译文」原样吐进译文
    /// （中文原文、本来没什么可译时尤其明显）。源文收尾，模型写完译文就没有可续写的文本了。
    /// </para>
    /// <para>
    /// 硬约束格式里的「专名/术语对照」是模型必须逐条对齐的清单，实测能压住专名臆造
    /// （例如把 Shinra 译成「神电公司」），所以有词条时不再附会最简指令。
    /// </para>
    /// </summary>
    /// <param name="text">待翻译的原文。</param>
    /// <param name="target">目标语言码（zh / en）。</param>
    /// <param name="entries">术语词典词条；空表用最简指令。</param>
    internal static string BuildPromptText(string text, string target, IReadOnlyList<LocalGlossaryEntry> entries)
    {
        var language = target == "zh" ? "中文" : "英文";
        var builder = new StringBuilder();

        if (entries is null || entries.Count == 0)
        {
            builder.Append("请将以下文本翻译为");
            builder.Append(language);
            builder.AppendLine("，直接输出翻译结果，不要进行任何解释。");
            builder.AppendLine();
            builder.Append(text);
            return builder.ToString();
        }

        builder.Append("请将下面的文本翻译为");
        builder.Append(language);
        builder.AppendLine("，并且严格遵循所有约束要求，只输出译文。");
        builder.AppendLine();
        builder.AppendLine("【约束要求】");
        builder.AppendLine("1. 【硬性要求】专名/术语对照: " + string.Join("、", entries.Select(e => $"{e.Source}→{e.Target}")));
        builder.AppendLine("2. 保留原文的换行、标点与 Markdown/BBCode 记号。");
        builder.AppendLine();
        builder.AppendLine("【源文】");
        builder.Append(text);
        return builder.ToString();
    }

    private async Task<string> InferAsync(string prompt, string sourceText, CancellationToken cancellationToken)
    {
        // 首次调用要把 1.3 GB 权重读进内存（LLamaWeights.LoadFromFile 是同步的）。
        // 调用方可能是 UI 线程：绝不能让这一步跑在它上面，否则整个窗口冻结十几秒。
        var weights = await Task.Run(GetWeights, cancellationToken).ConfigureAwait(false);
        var threads = ResolveThreadCount();
        var modelParams = new ModelParams(ModelPath)
        {
            // 与 GetWeights 保持一致：CUDA / Vulkan 已关闭，全部层留在内存里，
            // 写 99 只会让 LLamaSharp 白算一遍「哪些层能上 GPU」。
            GpuLayerCount = 0,
            ContextSize = ContextSize,
            Threads = threads,
            BatchThreads = threads,
        };

        var sampling = new DefaultSamplingPipeline
        {
            Temperature = 0f,
            TopK = 20,
            TopP = 0.6f,
            RepeatPenalty = 1.05f,
        };

        var inferenceParams = new InferenceParams
        {
            // 短标题用 128、长文才用 512；配合停止序列，回吐时不再每条白跑满 512 步。
            MaxTokens = ResolveMaxTokens(sourceText),
            AntiPrompts = GenerationStopMarkers,
            // 默认 false 会让特殊记号对解码器不可见，AntiPrompts 就匹配不到 ChatML 结束符。
            DecodeSpecialTokens = true,
            SamplingPipeline = sampling,
        };

        // Index-Translate 的 chat template 默认带 thinking，LLamaSharp 0.30 没法关，
        // 所以这里自己拼 ChatML 模板并关闭 ApplyTemplate。
        var chatPrompt = "<|im_start|>user" + NewLine + prompt + "<|im_end|>" + NewLine
            + "<|im_start|>assistant" + NewLine + "<think>" + NewLine + NewLine + "</think>" + NewLine + NewLine;

        var executor = new StatelessExecutor(weights, modelParams, null)
        {
            ApplyTemplate = false,
        };

        var builder = new StringBuilder();
        await foreach (var token in executor.InferAsync(chatPrompt, inferenceParams, cancellationToken).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(token))
            {
                builder.Append(token);
            }
        }

        return builder.ToString();
    }

    private LLamaWeights GetWeights()
    {
        if (_weights is not null)
        {
            return _weights;
        }

        lock (_sync)
        {
            if (_weights is not null)
            {
                return _weights;
            }

            NativeLibraryConfig.All.WithCuda(false).WithVulkan(false).WithAutoFallback(true);

            var threads = ResolveThreadCount();
            var modelParams = new ModelParams(ModelPath)
            {
                GpuLayerCount = 0,
                ContextSize = ContextSize,
                Threads = threads,
                BatchThreads = threads,
            };

            _weights = LLamaWeights.LoadFromFile(modelParams);
            _logger.Info($"本地模型已加载：{_weights.ParameterCount} 个参数");
            return _weights;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _weights?.Dispose();
        _weights = null;
        _gate.Dispose();
    }
}
