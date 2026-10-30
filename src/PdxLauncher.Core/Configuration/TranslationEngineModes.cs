namespace PdxLauncher.Core.Configuration;

/// <summary>
/// 翻译引擎的选择方式（settings.json 的 translateEngineMode 字段）。
/// 用字符串常量而不是枚举：AppSettingsStore 的 JSON 反序列化遇到未知枚举值会抛异常，
/// 进而让整份设置回退默认值；字符串可以安全兜底。
/// </summary>
public static class TranslationEngineModes
{
    /// <summary>自动：优先用用户自填的 OpenAI 兼容 API，失败或未配置时回落到内置免费服务（默认）。</summary>
    public const string Auto = "auto";

    /// <summary>仅使用用户自填的 API：没配置或调用失败就是失败，不回落。</summary>
    public const string Custom = "custom";

    /// <summary>仅使用内置免费服务：完全不需要用户配置。</summary>
    public const string Builtin = "builtin";

    /// <summary>全部可选值，顺序即设置页下拉框的顺序。</summary>
    public static IReadOnlyList<string> All { get; } = [Auto, Custom, Builtin];

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
