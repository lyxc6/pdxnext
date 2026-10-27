namespace PdxLauncher.Core.Online;

using PdxLauncher.Core.Configuration;

/// <summary>
/// Steam 商店接口的 <c>l=</c> 语言参数。这是「界面语言 → 商店文案语言」的唯一映射点，
/// 新增界面语言时只要在这里补一条，DLC 简介链路不用改。
/// </summary>
public static class SteamStoreLanguage
{
    /// <summary>缺省语言：官方简体中文（不是机翻）。</summary>
    public const string Default = "schinese";

    /// <summary>官方简体中文。</summary>
    public const string SimplifiedChinese = "schinese";

    /// <summary>官方英文。</summary>
    public const string English = "english";

    /// <summary>接口认识的语言值；目前只用到前两个，其余是给未来界面语言预留的已知值。</summary>
    public static IReadOnlyList<string> Known { get; } = ["schinese", "tchinese", "english", "japanese", "koreana", "russian"];

    /// <summary>
    /// 把任意语言字符串规范成 <see cref="Known"/> 里的值（大小写与空白无关）；
    /// 空值或未知值回退到 <see cref="Default"/>，避免把脏值写进缓存键或 <c>l=</c> 参数。
    /// </summary>
    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return Default;
        }

        var trimmed = language.Trim();
        foreach (var known in Known)
        {
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return Default;
    }

    /// <summary>界面语言键（zh-CN / en-US / null）→ 商店语言值；无法判断时回落到简中。</summary>
    public static string FromUiLanguage(string? uiLanguage)
    {
        var normalized = UiLanguages.Normalize(uiLanguage);
        return string.Equals(normalized, UiLanguages.English, StringComparison.OrdinalIgnoreCase) ? English : Default;
    }
}
