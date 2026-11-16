namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Online.Translation;

/// <summary>
/// mod 名称译名的唯一共享管道：Mod 库、播放集页、添加模组页三处的行译名都从这里取值与落盘。
/// <para>
/// 三处此前各走各的：Mod 库按「ModId + 目标语言」读写 <see cref="TranslationCache.Names"/>，
/// 播放集页只调引擎、从不写这张表，添加模组页干脆不翻译。结果是同一个 mod 在不同页面
/// 一句有一句无，换次进入还要重翻一遍。现在读写口径集中在这里，页面只负责把结果画到行上。
/// </para>
/// <para>
/// 本类刻意不引用 <c>AppServices</c> 静态与任何 WinUI 类型：目标语言解析、读盘与落盘都由
/// 构造注入，这样自检工程能原样链接它，用内存文档库直接钉住回填条件与写盘口径。
/// </para>
/// </summary>
public sealed class ModNameTranslator
{
    /// <summary>回填时要求「原文没变」的比对口径：逐字相等（大小写也算变化，因为改过的名字是新原文）。</summary>
    private readonly Dictionary<Guid, TranslationCache.NameTranslation> _translations = new();

    private readonly Func<string?> _resolveTargetLanguage;
    private readonly Func<string, CancellationToken, Task<IReadOnlyDictionary<Guid, TranslationCache.NameTranslation>>>? _load;
    private readonly Func<string, IReadOnlyList<TranslationCache.NameTranslation>, CancellationToken, Task>? _save;

    /// <summary>创建译名管道。</summary>
    /// <param name="resolveTargetLanguage">
    /// 当前该用的目标语言；返回 null 表示名称翻译关闭（「翻译名称」开关或翻译总开关关掉）。
    /// </param>
    /// <param name="load">按目标语言读取持久化的名称译名；null 表示不读盘（自检与降级用）。</param>
    /// <param name="save">按目标语言写入名称译名；null 表示不落盘。</param>
    public ModNameTranslator(
        Func<string?> resolveTargetLanguage,
        Func<string, CancellationToken, Task<IReadOnlyDictionary<Guid, TranslationCache.NameTranslation>>>? load = null,
        Func<string, IReadOnlyList<TranslationCache.NameTranslation>, CancellationToken, Task>? save = null)
    {
        ArgumentNullException.ThrowIfNull(resolveTargetLanguage);

        _resolveTargetLanguage = resolveTargetLanguage;
        _load = load;
        _save = save;
    }

    /// <summary>当前该用的目标语言；名称翻译关闭时为 null。</summary>
    public string? TargetLanguage => _resolveTargetLanguage();

    /// <summary>内存里已经知道的译名条数（排查与自检用）。</summary>
    public int Count => _translations.Count;

    /// <summary>
    /// 当前该用的名称翻译目标语言：两个开关都开时返回目标语言，否则 null。
    /// 三处页面共用这一份判定，避免某处漏判开关、把「已关闭」翻译成「翻译失败」。
    /// </summary>
    /// <param name="translateNamesEnabled">设置里的「翻译名称」开关。</param>
    /// <param name="options">当前翻译选项（含总开关与目标语言）。</param>
    public static string? ResolveTargetLanguage(bool translateNamesEnabled, TranslationOptions options)
        => translateNamesEnabled && options.Enabled ? options.TargetLanguage : null;

    /// <summary>清空内存里的译名（重新加载页面时从盘上再读一遍）。</summary>
    public void Clear() => _translations.Clear();

    /// <summary>
    /// 取一个 mod 的译名用于回填行：只有「同一 ModId + 同一目标语言 + 原文没变 + 译文非空」才命中。
    /// <para>
    /// 原文变了（mod 改名或换了别的语言版本）就当没有译名：显示旧译文比不显示更误导。
    /// </para>
    /// </summary>
    /// <param name="modId">mods.id。</param>
    /// <param name="sourceName">当前展示名（回填时作为原文比对）。</param>
    /// <param name="targetLanguage">目标语言；null 表示名称翻译关闭，直接不命中。</param>
    /// <param name="translated">命中的译文；未命中为 null。</param>
    public bool TryBackfill(Guid modId, string sourceName, string? targetLanguage, out string? translated)
    {
        translated = null;
        if (string.IsNullOrEmpty(targetLanguage)
            || string.IsNullOrWhiteSpace(sourceName)
            || !_translations.TryGetValue(modId, out var cached)
            || !string.Equals(cached.TargetLanguage, targetLanguage, StringComparison.Ordinal)
            || !string.Equals(cached.SourceName, sourceName, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(cached.Translation))
        {
            return false;
        }

        translated = cached.Translation;
        return true;
    }

    /// <summary>
    /// 记下一条刚译好的名称：内存字典与下一次 <see cref="BuildUpdates"/> 都会带上它，
    /// 于是搜索 / 排序重建行时能直接回填、不必重翻。
    /// </summary>
    /// <param name="modId">mods.id。</param>
    /// <param name="sourceName">译这条时的原文。</param>
    /// <param name="targetLanguage">目标语言。</param>
    /// <param name="translation">译文。</param>
    public void Remember(Guid modId, string sourceName, string targetLanguage, string translation)
    {
        if (string.IsNullOrWhiteSpace(sourceName)
            || string.IsNullOrEmpty(targetLanguage)
            || string.IsNullOrWhiteSpace(translation))
        {
            return;
        }

        _translations[modId] = new TranslationCache.NameTranslation(
            modId,
            sourceName,
            targetLanguage,
            translation);
    }

    /// <summary>
    /// 把内存里属于该目标语言的译名拼成落盘列表。
    /// 目标语言不同或原文为空的条目不会写进这份表（那张表本来就是按 ModId + 语言存的）。
    /// </summary>
    /// <param name="targetLanguage">目标语言。</param>
    public IReadOnlyList<TranslationCache.NameTranslation> BuildUpdates(string? targetLanguage)
    {
        if (string.IsNullOrEmpty(targetLanguage))
        {
            return Array.Empty<TranslationCache.NameTranslation>();
        }

        var updates = new List<TranslationCache.NameTranslation>(_translations.Count);
        foreach (var pair in _translations)
        {
            var entry = pair.Value;
            if (!string.Equals(entry.TargetLanguage, targetLanguage, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(entry.SourceName)
                || string.IsNullOrWhiteSpace(entry.Translation))
            {
                continue;
            }

            updates.Add(entry);
        }

        return updates;
    }

    /// <summary>
    /// 读回持久化的名称译名（按当前目标语言）；读不到不影响后续正常翻译。
    /// 调用方把这句丢到线程池（<c>Task.Run</c>）上，别在 UI 线程同步读缓存文档。
    /// </summary>
    /// <param name="targetLanguage">目标语言；null 表示名称翻译关闭，直接返回空。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task LoadAsync(string? targetLanguage, CancellationToken cancellationToken)
    {
        _translations.Clear();
        if (string.IsNullOrEmpty(targetLanguage) || _load is null)
        {
            return;
        }

        var cached = await _load(targetLanguage, cancellationToken).ConfigureAwait(false);
        foreach (var pair in cached)
        {
            _translations[pair.Key] = pair.Value;
        }
    }

    /// <summary>
    /// 把内存里属于该目标语言的译名写盘；没有内容就不写（避免空写一次文档）。
    /// 写盘失败由缓存层只记日志，这里不抛。
    /// </summary>
    /// <param name="targetLanguage">目标语言。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task SaveAsync(string? targetLanguage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(targetLanguage) || _save is null)
        {
            return;
        }

        var updates = BuildUpdates(targetLanguage);
        if (updates.Count == 0)
        {
            return;
        }

        await _save(targetLanguage, updates, cancellationToken).ConfigureAwait(false);
    }
}
