namespace PdxLauncher.Core.Online.Translation;

/// <summary>
/// 可选能力：逐条翻译并把每条结果立刻上报，不必等整批都翻完。
/// <para>
/// 本地 2B 模型批内是串行推理（每条几百毫秒~数秒），批内 22 条若等整批返回再统一回调，
/// 界面上一条译名都不出，看起来像卡死。实现这个接口的引擎让服务层边翻边回填。
/// 不实现的引擎走 <see cref="ITranslationEngine.TranslateBatchAsync"/> 的整批路径，行为不变。
/// </para>
/// </summary>
public interface IStreamingTranslationEngine : ITranslationEngine
{
    /// <summary>
    /// 逐条翻译；每条完成（成功或失败）立刻回调 <paramref name="onItem"/>，位置与入参一一对应。
    /// </summary>
    /// <param name="texts">原文列表。</param>
    /// <param name="targetLanguage">目标语言。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="onItem">每条结果回调（索引 + 译文；null 表示这条没翻出来）。</param>
    Task TranslateBatchStreamingAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken,
        Action<int, string?> onItem);
}
