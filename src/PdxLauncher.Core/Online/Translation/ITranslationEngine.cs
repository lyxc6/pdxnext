namespace PdxLauncher.Core.Online.Translation;

/// <summary>
/// 一个翻译引擎：把一段文本译成目标语言。
/// <para>
/// 实现有两种：用户自填的 OpenAI 兼容 API（<see cref="OpenAiCompatibleTranslationEngine"/>）
/// 与内置免费服务（<see cref="FreeTranslationEngine"/>）。两者都不抛异常表示「译不了」，
/// 而是用 null 表示失败，由 <see cref="TranslationService"/> 决定是否回落到另一个引擎。
/// </para>
/// </summary>
public interface ITranslationEngine
{
    /// <summary>引擎标识（会写进缓存键，换引擎自然换缓存）。</summary>
    string Id { get; }

    /// <summary>引擎是否已经配置好可用（自填引擎没填地址/密钥时为 false）。</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// 最近一次失败的人话原因（形如「额度已用完，约 2 小时后恢复」「连不上（TLS/超时）」）；
    /// 成功时实现应当置回 null。默认实现返回 null，表示「就是没拿到译文」。
    /// <para>
    /// 用途：整批失败时由 <see cref="TranslationService"/> 逐个引擎拼成一句提示，
    /// 让用户知道到底卡在额度、网络还是响应格式，而不是只看到第一个引擎的笼统备注。
    /// </para>
    /// </summary>
    string? LastFailure => null;

    /// <summary>
    /// 翻译一段文本；失败（网络、鉴权、返回格式不对）返回 null，不抛异常。
    /// </summary>
    /// <param name="text">原文。</param>
    /// <param name="targetLanguage">目标语言代码（zh-CN / en-US）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<string?> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken);

    /// <summary>
    /// 批量翻译；返回的数组与入参一一对应，失败的位置为 null。
    /// 实现应当尽量一次请求搞定全部文本。
    /// </summary>
    /// <param name="texts">原文列表。</param>
    /// <param name="targetLanguage">目标语言代码（zh-CN / en-US）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<string?>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken);
}
