namespace PdxLauncher.Core.Configuration;

/// <summary>
/// 翻译引擎的选择方式（settings.json 的 translateEngineMode 字段）。
/// 用字符串常量而不是枚举：AppSettingsStore 的 JSON 反序列化遇到未知枚举值会抛异常，
/// 进而让整份设置回退默认值；字符串可以安全兜底。
/// </summary>
public static class TranslationEngineModes
{
    /// <summary>自动：优先用用户自填的 OpenAI 兼容 API，逐级回落到免密钥的兜底引擎（默认）。</summary>
    public const string Auto = "auto";

    /// <summary>仅使用用户自填的 API：没配置或调用失败就是失败，不回落。</summary>
    public const string Custom = "custom";

    /// <summary>仅使用内置免费服务：完全不需要用户配置。</summary>
    public const string Builtin = "builtin";

    /// <summary>
    /// 仅使用 MyMemory 免密钥接口：同样不需要配置。
    /// 与 <see cref="Builtin"/> 的区别是走另一台主机，在谷歌端点不可达的网络里仍能出译文。
    /// </summary>
    public const string MyMemory = "mymemory";

    /// <summary>
    /// 仅使用微软 Azure Translator：需要订阅密钥与区域两个凭据，失败不回落。
    /// </summary>
    public const string Microsoft = "microsoft";

    /// <summary>
    /// 仅使用谷歌云端翻译（Cloud Translation）：需要 API Key，失败不回落。
    /// </summary>
    public const string GoogleCloud = "google-cloud";

    /// <summary>
    /// 仅使用本地离线引擎：Index-Translate 优先，失败回落 Bergamot。
    /// 需要先在设置页下载对应模型。
    /// </summary>
    public const string Local = "local";

    /// <summary>
    /// 仅使用本地 Index-Translate-2B（质量最好，需要 1.31GB 模型与显存/内存）。
    /// </summary>
    public const string IndexTranslate = "index-translate";

    /// <summary>
    /// 仅使用本地 Bergamot（Firefox 翻译同款 NMT，模型小、纯 CPU、毫秒级）。
    /// </summary>
    public const string Bergamot = "bergamot";

    /// <summary>全部可选值，顺序即设置页下拉框的顺序。</summary>
    public static IReadOnlyList<string> All { get; } =
        [Auto, Custom, Builtin, MyMemory, Microsoft, GoogleCloud, Local, IndexTranslate, Bergamot];

    /// <summary>把设置里的值归一化成合法模式；null / 空白 / 未知值落到 <see cref="Auto"/>。</summary>
    public static string Normalize(string? mode)
    {
        var trimmed = mode?.Trim();
        foreach (var value in All)
        {
            if (string.Equals(trimmed, value, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return Auto;
    }
}
