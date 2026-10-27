namespace PdxLauncher.Core.Configuration;

using System.Globalization;

/// <summary>
/// 界面语言的可选值。设置里存的就是这些键（zh-CN / en-US），null 表示「跟随系统」，
/// 由 <see cref="ResolveFromCulture"/> 把系统 UI 语言折到这两档之一。
/// </summary>
public static class UiLanguages
{
    /// <summary>简体中文（默认）。</summary>
    public const string SimplifiedChinese = "zh-CN";

    /// <summary>英文（英文文案同时充当缺键回退语言）。</summary>
    public const string English = "en-US";

    /// <summary>默认界面语言：简体中文。</summary>
    public const string Default = SimplifiedChinese;

    /// <summary>受支持的语言键，顺序即设置页下拉框的顺序。</summary>
    public static IReadOnlyList<string> All { get; } = [SimplifiedChinese, English];

    /// <summary>设置值是否为「跟随系统」（null / 空白）。</summary>
    public static bool IsFollowSystem(string? language) => string.IsNullOrWhiteSpace(language);

    /// <summary>是否是受支持的语言键（大小写无关，不含「跟随系统」）。</summary>
    public static bool IsSupported(string? language) =>
        string.Equals(language?.Trim(), SimplifiedChinese, StringComparison.OrdinalIgnoreCase)
        || string.Equals(language?.Trim(), English, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 把设置里的值归一化成受支持的语言键：null / 空白表示跟随系统，用系统 UI 语言判断；
    /// 形如 zh-Hans-CN 的中文变体也算中文；其它未知值落到 <see cref="Default"/>（不能跟着系统漂）。
    /// </summary>
    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return ResolveFromCulture(CultureInfo.CurrentUICulture.Name);
        }

        var trimmed = language.Trim();
        if (IsSupported(trimmed))
        {
            return string.Equals(trimmed, English, StringComparison.OrdinalIgnoreCase) ? English : SimplifiedChinese;
        }

        if (trimmed.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return SimplifiedChinese;
        }

        return Default;
    }

    /// <summary>按系统 UI 语言名（zh-Hans-CN / en-GB / …）猜测界面语言：中文变体 → 简中，其余 → 英文。</summary>
    public static string ResolveFromCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return Default;
        }

        return cultureName.Trim().StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? SimplifiedChinese : English;
    }
}
