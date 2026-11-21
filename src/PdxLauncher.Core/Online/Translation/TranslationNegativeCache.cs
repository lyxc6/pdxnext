namespace PdxLauncher.Core.Online.Translation;

/// <summary>
/// 短 TTL 的「这条文本翻不出来」记录。
/// <para>
/// 本地模型对短标题/中文原文经常返回空或与原文相同；这些结果不写译文缓存，
/// 于是工坊每次翻页都会把同一批标题再喂给 2B 模型跑一遍，白烧几十秒 CPU。
/// 这里按「引擎模式 + 目标语言 + 原文」记一条失败标记，短时间内直接跳过引擎；
/// 过 TTL 或设置变更后自动重试，不污染真正的译文缓存。
/// </para>
/// </summary>
public sealed class TranslationNegativeCache
{
    /// <summary>失败标记的保留时长。翻页间隔通常远小于此值，过期后仍会重试。</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(3);

    /// <summary>最多记多少条，避免长期运行后无上限增长。</summary>
    public const int MaxEntries = 512;

    private readonly object _sync = new();
    private readonly Dictionary<string, DateTimeOffset> _entries = new(StringComparer.Ordinal);

    /// <summary>当前记录条数（自检用）。</summary>
    public int Count { get { lock (_sync) { return _entries.Count; } } }

    /// <summary>构造键：只让相同选项的同一原文互相命中。</summary>
    public static string BuildKey(TranslationOptions options, string text) =>
        options.EngineMode + "|" + options.TargetLanguage + "|" + text;

    /// <summary>是否还在失败标记的有效期内。</summary>
    public bool IsSuppressed(string key, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out var expires))
            {
                return false;
            }

            if (expires > now)
            {
                return true;
            }

            _entries.Remove(key);
            return false;
        }
    }

    /// <summary>记一条失败标记；同键刷新 TTL，超上限时清掉最早过期的一批。</summary>
    public void Add(string key, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_entries.Count >= MaxEntries)
            {
                var expired = _entries.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToList();
                foreach (var stale in expired)
                {
                    _entries.Remove(stale);
                }

                if (_entries.Count >= MaxEntries)
                {
                    // 还满就丢掉最早到期的一批，保证有位置放新条目。
                    foreach (var oldest in _entries.OrderBy(pair => pair.Value).Take(MaxEntries / 4))
                    {
                        _entries.Remove(oldest.Key);
                    }
                }
            }

            _entries[key] = now + Ttl;
        }
    }

    /// <summary>清空全部失败标记（设置变更 / 用户重试时调用）。</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
        }
    }
}
