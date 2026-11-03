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
public sealed class IndexTranslateEngine : ITranslationEngine, IDisposable
{
    /// <summary>引擎标识。</summary>
    public const string EngineId = "index-translate";

    /// <summary>单次生成的最大 token 数。</summary>
    public const int MaxTokens = 512;

    /// <summary>上下文窗口大小。</summary>
    public const int ContextSize = 4096;

    private const string NewLine = "\n";

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

    /// <inheritdoc />
    public bool IsConfigured => _store is not null
        ? _store.IsInstalled(LocalTranslationModelKind.IndexTranslate)
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

        var prompt = BuildPrompt(text, target);

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var output = await InferAsync(prompt, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(output))
                {
                    return null;
                }

                return output.Trim();
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
        var results = new List<string?>(texts.Count);
        foreach (var text in texts)
        {
            results.Add(await TranslateAsync(text, targetLanguage, cancellationToken).ConfigureAwait(false));
        }

        return results;
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

    /// <summary>
    /// 拼提示词：词典为空时用最简指令，有词条时改用 instTrans 的硬约束格式。
    /// <para>
    /// 硬约束格式里的「专名/术语对照」是模型必须逐条对齐的清单，实测能压住专名臆造
    /// （例如把 Shinra 译成「神电公司」），所以有词条时不再附会最简指令。
    /// </para>
    /// </summary>
    private string BuildPrompt(string text, string target)
    {
        var language = target == "zh" ? "中文" : "英文";
        var builder = new StringBuilder();

        var entries = _glossary.Entries;
        if (entries.Count == 0)
        {
            builder.Append("请将以下文本翻译为");
            builder.Append(language);
            builder.AppendLine("，直接输出翻译结果，不要进行任何解释。");
            builder.AppendLine();
            builder.Append(text);
            return builder.ToString();
        }

        builder.Append("请将以下文本翻译为");
        builder.Append(language);
        builder.AppendLine("，并且严格遵循所有约束要求。");
        builder.AppendLine();
        builder.AppendLine("【源文】");
        builder.AppendLine(text);
        builder.AppendLine();
        builder.AppendLine("【约束要求】");
        builder.AppendLine("1. 【硬性要求】专名/术语对照: " + string.Join("、", entries.Select(e => $"{e.Source}→{e.Target}")));
        builder.AppendLine();
        builder.AppendLine("只输出译文，不要有任何额外说明。");
        return builder.ToString();
    }

    private async Task<string> InferAsync(string prompt, CancellationToken cancellationToken)
    {
        var weights = GetWeights();
        var modelParams = new ModelParams(ModelPath)
        {
            GpuLayerCount = 99,
            ContextSize = ContextSize,
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
            MaxTokens = MaxTokens,
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

            var modelParams = new ModelParams(ModelPath)
            {
                GpuLayerCount = 0,
                ContextSize = ContextSize,
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
