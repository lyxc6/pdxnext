namespace PdxLauncher.Core.ModStudio;

using PdxLauncher.Core.Games;

/// <summary>
/// 上传面板可选的游戏标签。
/// <para>
/// 原版启动器从 Paradox games API 动态取标签；这里先用按游戏维护的静态清单，
/// 接口不可用时也能稳定显示，后续可替换为账号接口返回的数据。
/// </para>
/// </summary>
public static class ModTagCatalog
{
    private static readonly string[] CommonTags =
    [
        "Balance",
        "Events",
        "Expansion",
        "Fixes",
        "Gameplay",
        "Graphics",
        "Historical",
        "Map",
        "Sound",
        "Total Conversion",
        "Translation",
        "Utilities",
        "Warfare",
    ];

    private static readonly string[] Victoria3Tags =
    [
        "Alternative History",
        "Balance",
        "Cultures And Religions",
        "Diplomacy",
        "Economy And Buildings",
        "Events",
        "Expansion",
        "Fixes",
        "Flags",
        "Gameplay",
        "Graphics",
        "Historical",
        "Interest Groups",
        "Journal Entries",
        "Map",
        "New Nations",
        "Pops",
        "Sound",
        "Technologies",
        "Total Conversion",
        "Trade",
        "Translation",
        "Utilities",
        "Warfare",
    ];

    private static readonly string[] CrusaderKings3Tags =
    [
        "Alternative History",
        "Balance",
        "Characters",
        "Culture",
        "Events",
        "Expansion",
        "Fixes",
        "Gameplay",
        "Graphics",
        "Historical",
        "Interface",
        "Map",
        "Religion",
        "Sound",
        "Total Conversion",
        "Translation",
        "Utilities",
        "Warfare",
    ];

    private static readonly string[] StellarisTags =
    [
        "Balance",
        "Events",
        "Expansion",
        "Fixes",
        "Gameplay",
        "Graphics",
        "Historical",
        "Interface",
        "Map",
        "Megastructures",
        "Ships",
        "Sound",
        "Species",
        "Technologies",
        "Total Conversion",
        "Translation",
        "Utilities",
        "Warfare",
    ];

    private static readonly string[] EuropaUniversalis4Tags =
    [
        "Balance",
        "Events",
        "Expansion",
        "Fixes",
        "Gameplay",
        "Graphics",
        "Historical",
        "Interface",
        "Map",
        "Missions",
        "Sound",
        "Total Conversion",
        "Trade",
        "Translation",
        "Utilities",
        "Warfare",
    ];

    private static readonly string[] HeartsOfIron4Tags =
    [
        "Balance",
        "Events",
        "Expansion",
        "Fixes",
        "Gameplay",
        "Graphics",
        "Historical",
        "Interface",
        "Map",
        "Sound",
        "Technologies",
        "Total Conversion",
        "Translation",
        "Units",
        "Utilities",
        "Warfare",
    ];

    private static readonly string[] ImperatorRomeTags =
    [
        "Balance",
        "Events",
        "Expansion",
        "Fixes",
        "Gameplay",
        "Graphics",
        "Historical",
        "Interface",
        "Map",
        "Religion",
        "Sound",
        "Total Conversion",
        "Translation",
        "Utilities",
        "Warfare",
    ];

    /// <summary>取游戏的上传标签清单；未登记游戏返回通用清单。</summary>
    public static IReadOnlyList<string> For(string? gameId)
    {
        var canonical = KnownGames.CanonicalizeGameId(gameId);
        var tags = canonical switch
        {
            "victoria3" => Victoria3Tags,
            "ck3" => CrusaderKings3Tags,
            "stellaris" => StellarisTags,
            "eu4" or "eu5" => EuropaUniversalis4Tags,
            "hoi4" => HeartsOfIron4Tags,
            "imperator_rome" => ImperatorRomeTags,
            _ => CommonTags,
        };

        return Normalize(tags);
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> tags)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var tag in tags)
        {
            var trimmed = tag.Trim();
            if (trimmed.Length == 0 || !seen.Add(trimmed))
            {
                continue;
            }

            result.Add(trimmed);
        }

        return result;
    }
}
