namespace PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 本地模型种类。
/// </summary>
public enum LocalTranslationModelKind
{
    /// <summary>Index-Translate-2B（GGUF，走 LLamaSharp 进程内推理）。</summary>
    IndexTranslate,

    /// <summary>Bergamot 英译中（Firefox 翻译同款 NMT，纯 CPU）。</summary>
    BergamotEnZh,

    /// <summary>Bergamot 中译英（Firefox 翻译同款 NMT，纯 CPU）。</summary>
    BergamotZhEn,
}
