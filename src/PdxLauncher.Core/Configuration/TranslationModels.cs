namespace PdxLauncher.Core.Configuration;

/// <summary>
/// 自填翻译 API 的默认模型名。用户在设置页可以改；这里只提供「没填时用哪个」。
/// </summary>
public static class TranslationModels
{
    /// <summary>默认模型名（体积小、速度快，适合批量翻译短文本）。</summary>
    public const string Default = "gpt-4o-mini";
}
