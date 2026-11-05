namespace PdxLauncher.Core.Online.Translation.Local;

using System.Collections.Concurrent;
using System.Text;
using BergamotTranslatorSharp;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online.Translation;

/// <summary>
/// 基于 Bergamot 的本地翻译引擎（Firefox 网页翻译同款 NMT 模型，纯 CPU）。
/// <para>
/// 特点：模型小（英译中 48MB、中译英 70MB）、毫秒级响应、完全离线。
/// 弱点：不带上下文、不认识游戏专名 —— 专名靠 <see cref="LocalTranslationGlossary"/>
/// 的术语表纠正。
/// </para>
/// </summary>
public sealed class BergamotTranslationEngine : ITranslationEngine, IDisposable
{
    /// <summary>引擎标识。</summary>
    public const string EngineId = "bergamot";

    private readonly IAppLogger _logger;
    private readonly LocalTranslationGlossary _glossary;
    private readonly LocalTranslationModelStore? _store;
    private readonly ConcurrentDictionary<string, BlockingService> _services = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    private string? _lastFailure;
    private bool _disposed;

    /// <summary>创建引擎。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="glossary">术语词典。</param>
    /// <param name="store">模型仓库；用来判断模型是否已安装。</param>
    public BergamotTranslationEngine(
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
    public bool IsConfigured => HasModel(LocalTranslationModelKind.BergamotEnZh)
        || HasModel(LocalTranslationModelKind.BergamotZhEn);

    /// <summary>
    /// 某个方向的模型是否真的能加载：用 Core 的 <c>IsRunnable</c>（文件齐、尺寸对、
    /// <c>config.txt</c> 也在），而不是只看文件在不在——缺配置时浏览器插件根本起不来。
    /// </summary>
    private bool HasModel(LocalTranslationModelKind kind)
    {
        if (_store is not null)
        {
            return _store.GetInstallState(kind).IsRunnable;
        }

        // 没有仓库实例时无法逐文件核对，只能保守看配置文件。
        var config = Path.Combine(
            LocalTranslationModelStore.GetModelDirectory(kind),
            LocalTranslationModelConfigWriter.ConfigFileName);
        return File.Exists(config);
    }

    /// <inheritdoc />
    public Task<string?> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<string?>(text);
        }

        var kind = ResolveDirection(targetLanguage);
        if (kind is null)
        {
            return Task.FromResult<string?>(null);
        }

        return Task.Run(
            () => TranslateCore(text, kind.Value),
            cancellationToken);
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

    private static LocalTranslationModelKind? ResolveDirection(string targetLanguage)
    {
        var normalized = targetLanguage?.ToLowerInvariant() ?? string.Empty;
        if (normalized.StartsWith("zh", StringComparison.Ordinal))
        {
            return LocalTranslationModelKind.BergamotEnZh;
        }

        if (normalized.StartsWith("en", StringComparison.Ordinal))
        {
            return LocalTranslationModelKind.BergamotZhEn;
        }

        return null;
    }

    private string? TranslateCore(string text, LocalTranslationModelKind kind)
    {
        try
        {
            var service = GetService(kind);
            if (service is null)
            {
                return null;
            }

            // 术语表走「占位符替换」：先把原文里的专名换成模型不会拆开的占位符，
            // 翻译完再换回目标词。不能用 BergamotTranslatorSharp 自带的
            // Translate(text, dictionary)：那套依赖原生库的 <bts-term> 标签回填，
            // 实测标签内容会被原生层丢掉并把译文拼到标签后面，结果重复且错乱。
            var terms = _glossary.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Source) && !string.IsNullOrWhiteSpace(entry.Target))
                .OrderByDescending(entry => entry.Source.Length)
                .ThenBy(entry => entry.Source, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (terms.Length == 0)
            {
                var plain = service.Translate(text, false);
                return string.IsNullOrWhiteSpace(plain) ? null : plain;
            }

            var prepared = SubstituteTerms(text, terms, out var replacements);
            var translated = service.Translate(prepared, false);
            if (string.IsNullOrWhiteSpace(translated))
            {
                return null;
            }

            return RestoreTerms(translated, replacements);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastFailure, ex.Message);
            _logger.Error("Bergamot 本地翻译失败", ex);
            return null;
        }
    }

    /// <summary>
    /// 把原文中的术语替换成占位符。
    /// </summary>
    /// <param name="text">原文。</param>
    /// <param name="terms">术语表（已按长度降序，长词优先避免短词先命中）。</param>
    /// <param name="replacements">占位符到目标词的映射。</param>
    /// <returns>替换后的文本。</returns>
    private static string SubstituteTerms(
        string text,
        IReadOnlyList<LocalGlossaryEntry> terms,
        out Dictionary<string, string> replacements)
    {
        replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        var builder = new StringBuilder(text.Length + 32);
        var index = 0;
        var position = 0;
        while (position < text.Length)
        {
            var matched = false;
            foreach (var term in terms)
            {
                if (position + term.Source.Length > text.Length)
                {
                    continue;
                }

                if (!text.AsSpan(position, term.Source.Length).Equals(term.Source, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var placeholder = PlaceholderFor(index++);
                replacements[placeholder] = term.Target;
                builder.Append(placeholder);
                position += term.Source.Length;
                matched = true;
                break;
            }

            if (!matched)
            {
                builder.Append(text[position]);
                position++;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// 把译文里的占位符换回目标词。
    /// </summary>
    /// <param name="translated">译文。</param>
    /// <param name="replacements">占位符到目标词的映射。</param>
    /// <returns>还原后的译文。</returns>
    private static string RestoreTerms(string translated, IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0)
        {
            return translated;
        }

        var result = translated;
        foreach (var pair in replacements)
        {
            result = result.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    /// <summary>
    /// 生成占位符。模型对纯 ASCII 大写字母数字组合最稳定，实测不会被拆开或改写。
    /// </summary>
    /// <param name="index">占位符序号。</param>
    /// <returns>占位符文本。</returns>
    private static string PlaceholderFor(int index) => "XQZTERM" + index + "QXZ";

    private BlockingService? GetService(LocalTranslationModelKind kind)
    {
        var key = kind.ToString();
        if (_services.TryGetValue(key, out var existing))
        {
            return existing;
        }

        lock (_sync)
        {
            if (_services.TryGetValue(key, out existing))
            {
                return existing;
            }

            var directory = LocalTranslationModelStore.GetModelDirectory(kind);
            var config = Path.Combine(directory, LocalTranslationModelConfigWriter.ConfigFileName);
            if (!File.Exists(config))
            {
                Volatile.Write(ref _lastFailure, "本地模型尚未下载。");
                return null;
            }

            try
            {
                var service = new BlockingService(new[] { config });
                _services[key] = service;
                return service;
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _lastFailure, ex.Message);
                _logger.Error("Bergamot 模型加载失败", ex);
                return null;
            }
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
        foreach (var service in _services.Values)
        {
            service.Dispose();
        }

        _services.Clear();
    }
}
