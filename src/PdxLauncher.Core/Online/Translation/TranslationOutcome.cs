namespace PdxLauncher.Core.Online.Translation;

/// <summary>一条译文的结果，同时记下它是哪个引擎翻的（写缓存要用）。</summary>
/// <param name="Text">译文；失败为 null。</param>
/// <param name="EngineId">产出这条译文的引擎标识；失败为 null。</param>
public readonly record struct TranslationOutcome(string? Text, string? EngineId);
