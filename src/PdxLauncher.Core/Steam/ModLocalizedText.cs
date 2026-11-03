namespace PdxLauncher.Core.Steam;

/// <summary>创意工坊条目在某一语言下的标题与描述。</summary>
/// <param name="Language">Steam 语言码，例如 <c>schinese</c>、<c>english</c>。</param>
/// <param name="Title">该语言的标题。</param>
/// <param name="Description">该语言的描述；空字符串表示只改标题。</param>
public sealed record ModLocalizedText(string Language, string Title, string Description);

/// <summary>
/// Steam 创意工坊支持的语言码表。
/// <para>
/// 语言码直接沿用 Steam 自己的命名（<c>schinese</c> / <c>tchinese</c> / <c>koreana</c> …），
/// 因为 <c>SetItemUpdateLanguage</c> 只认这套值。
/// </para>
/// <para>
/// 显示名固定用英文、不进 i18n：30 种语言 × 2 套界面 = 60 个没人会核对的键，
/// 而英文语言名对各种语言的用户都足够辨认。
/// </para>
/// </summary>
public static class SteamWorkshopLanguages
{
    /// <summary>默认语言码。</summary>
    public const string Default = "english";

    /// <summary>简体中文语言码。</summary>
    public const string SimplifiedChinese = "schinese";

    // ReSharper disable once UseCollectionExpression
    private static readonly (string Code, string DisplayName)[] Languages =
    [
        ("schinese", "Simplified Chinese"),
        ("tchinese", "Traditional Chinese"),
        ("japanese", "Japanese"),
        ("koreana", "Korean"),
        ("thai", "Thai"),
        ("indonesian", "Indonesian"),
        ("malay", "Malay"),
        ("bulgarian", "Bulgarian"),
        ("czech", "Czech"),
        ("danish", "Danish"),
        ("german", "German"),
        ("english", "English"),
        ("spanish", "Spanish (Spain)"),
        ("latam", "Spanish (Latin America)"),
        ("greek", "Greek"),
        ("french", "French"),
        ("italian", "Italian"),
        ("hungarian", "Hungarian"),
        ("dutch", "Dutch"),
        ("norwegian", "Norwegian"),
        ("polish", "Polish"),
        ("portuguese", "Portuguese (Portugal)"),
        ("brazilian", "Portuguese (Brazil)"),
        ("romanian", "Romanian"),
        ("russian", "Russian"),
        ("finnish", "Finnish"),
        ("swedish", "Swedish"),
        ("turkish", "Turkish"),
        ("ukrainian", "Ukrainian"),
        ("vietnamese", "Vietnamese"),
    ];

    /// <summary>全部语言，顺序与 Steam 面板一致。</summary>
    public static IReadOnlyList<SteamWorkshopLanguage> All { get; } =
        Languages.Select(static pair => new SteamWorkshopLanguage(pair.Code, pair.DisplayName)).ToArray();

    /// <summary>该语言码是否在表内。</summary>
    public static bool IsKnown(string? code) =>
        !string.IsNullOrWhiteSpace(code)
        && Languages.Any(pair => string.Equals(pair.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>归一化语言码；未知或空返回 <see cref="Default"/>。</summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Default;
        }

        foreach (var pair in Languages)
        {
            if (string.Equals(pair.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Code;
            }
        }

        return Default;
    }

    /// <summary>
    /// 挑一个还没被占用的语言码，顺序与 <see cref="All"/> 一致。
    /// 全部语言都已占用时返回 null。
    /// </summary>
    /// <param name="used">已被占用的语言码（大小写不敏感，未知码先归一化再比）。</param>
    public static string? PickUnused(IEnumerable<string>? used)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        if (used is not null)
        {
            foreach (var code in used)
            {
                taken.Add(Normalize(code));
            }
        }

        foreach (var pair in Languages)
        {
            if (!taken.Contains(pair.Code))
            {
                return pair.Code;
            }
        }

        return null;
    }

    /// <summary>归一化后是否与已用语言重复。</summary>
    /// <param name="used">已被占用的语言码。</param>
    /// <param name="candidate">待检查的语言码。</param>
    public static bool IsDuplicate(IEnumerable<string>? used, string? candidate)
    {
        if (used is null)
        {
            return false;
        }

        var normalized = Normalize(candidate);
        foreach (var code in used)
        {
            if (string.Equals(Normalize(code), normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>取显示名；未知语言码原样返回，避免把用户填的值吞掉。</summary>
    public static string DisplayName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        foreach (var pair in Languages)
        {
            if (string.Equals(pair.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return pair.DisplayName;
            }
        }

        return code;
    }
}

/// <summary>一个 Steam 语言码及其显示名。</summary>
/// <param name="Code">Steam 语言码。</param>
/// <param name="DisplayName">英文显示名。</param>
public sealed record SteamWorkshopLanguage(string Code, string DisplayName);
