namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Saves;

/// <summary>
/// 存档行「详情」段的取字段规则（存档名 / 国家或城市名）。
/// <para>
/// 纯函数、不碰 UI 类型，可被 checks 工程链接后直接做行为断言。规则本身看不见摸不着，
/// 错了只会让同一行里出现两遍同样的字：CS2 的 <c>SaveGameMetadata</c> 没有 country 字段时，
/// Core 会把 <c>cityName</c> 同时写进 <c>SaveName</c> 与 <c>PlayerCountry</c>，
/// 行里再补一句「国家：阿伦德尔」就是同一条信息说了两次，而且那个值其实是城市名。
/// </para>
/// </summary>
public static class SaveRowText
{
    /// <summary>存档名非空才显示「存档名」段。</summary>
    public static bool HasSaveName(string? saveName) => !string.IsNullOrWhiteSpace(saveName);

    /// <summary>
    /// 国家/城市段只在有独立信息时显示：与存档名相同（CS2 的常见情形）或为空都不重复。
    /// </summary>
    public static bool ShouldShowPlace(string? playerCountry, string? saveName) =>
        !string.IsNullOrWhiteSpace(playerCountry)
        && !string.Equals(playerCountry!.Trim(), saveName?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Colossal Order 容器的 <c>PlayerCountry</c> 语义是城市名（CS2 <c>cityName</c>），
    /// 标签不能跟着 CK3/Stellaris 写「国家」。
    /// </summary>
    public static bool IsCityPlace(SaveContainer container) => container == SaveContainer.Colossal;
}
