namespace PdxLauncher.Core.Online;

/// <summary>
/// Steam 创意工坊标签名 → 界面文案键。
/// <para>
/// 标签值（发给 <c>requiredtags[]</c> / <c>excludedtags[]</c> 的机器名）不能改，这里只把
/// 展示名映射到 i18n 键，由界面层用当前语言取文案。未收录的标签原样显示英文，
/// Steam 以后新增标签时不会变成空白。
/// </para>
/// <para>
/// 文案键写成字符串常量（Core 不能引用 App 生成的 <c>Keys</c>），由自检
/// <c>WorkshopTagTextChecks</c> 保证键名与 <c>.resw</c> 一致，并覆盖内置标签目录。
/// </para>
/// </summary>
public static class WorkshopTagKeys
{
    /// <summary>架空历史。</summary>
    public const string KeyAlternativeHistory = "WorkshopTagAlternativeHistory";

    /// <summary>平衡。</summary>
    public const string KeyBalance = "WorkshopTagBalance";

    /// <summary>角色。</summary>
    public const string KeyCharacters = "WorkshopTagCharacters";

    /// <summary>文化。</summary>
    public const string KeyCulture = "WorkshopTagCulture";

    /// <summary>文化与宗教。</summary>
    public const string KeyCulturesAndReligions = "WorkshopTagCulturesAndReligions";

    /// <summary>已弃用。</summary>
    public const string KeyDeprecated = "WorkshopTagDeprecated";

    /// <summary>外交。</summary>
    public const string KeyDiplomacy = "WorkshopTagDiplomacy";

    /// <summary>经济与建筑。</summary>
    public const string KeyEconomyAndBuildings = "WorkshopTagEconomyAndBuildings";

    /// <summary>事件。</summary>
    public const string KeyEvents = "WorkshopTagEvents";

    /// <summary>扩展。</summary>
    public const string KeyExpansion = "WorkshopTagExpansion";

    /// <summary>修复。</summary>
    public const string KeyFixes = "WorkshopTagFixes";

    /// <summary>旗帜。</summary>
    public const string KeyFlags = "WorkshopTagFlags";

    /// <summary>玩法。</summary>
    public const string KeyGameplay = "WorkshopTagGameplay";

    /// <summary>画面。</summary>
    public const string KeyGraphics = "WorkshopTagGraphics";

    /// <summary>历史。</summary>
    public const string KeyHistorical = "WorkshopTagHistorical";

    /// <summary>利益集团。</summary>
    public const string KeyInterestGroups = "WorkshopTagInterestGroups";

    /// <summary>界面。</summary>
    public const string KeyInterface = "WorkshopTagInterface";

    /// <summary>日志条目。</summary>
    public const string KeyJournalEntries = "WorkshopTagJournalEntries";

    /// <summary>地图。</summary>
    public const string KeyMap = "WorkshopTagMap";

    /// <summary>巨型结构。</summary>
    public const string KeyMegastructures = "WorkshopTagMegastructures";

    /// <summary>任务。</summary>
    public const string KeyMissions = "WorkshopTagMissions";

    /// <summary>新国家。</summary>
    public const string KeyNewNations = "WorkshopTagNewNations";

    /// <summary>人口。</summary>
    public const string KeyPops = "WorkshopTagPops";

    /// <summary>宗教。</summary>
    public const string KeyReligion = "WorkshopTagReligion";

    /// <summary>舰船。</summary>
    public const string KeyShips = "WorkshopTagShips";

    /// <summary>音效。</summary>
    public const string KeySound = "WorkshopTagSound";

    /// <summary>物种。</summary>
    public const string KeySpecies = "WorkshopTagSpecies";

    /// <summary>科技。</summary>
    public const string KeyTechnologies = "WorkshopTagTechnologies";

    /// <summary>全面转换。</summary>
    public const string KeyTotalConversion = "WorkshopTagTotalConversion";

    /// <summary>贸易。</summary>
    public const string KeyTrade = "WorkshopTagTrade";

    /// <summary>翻译。</summary>
    public const string KeyTranslation = "WorkshopTagTranslation";

    /// <summary>单位。</summary>
    public const string KeyUnits = "WorkshopTagUnits";

    /// <summary>实用工具。</summary>
    public const string KeyUtilities = "WorkshopTagUtilities";

    /// <summary>战争。</summary>
    public const string KeyWarfare = "WorkshopTagWarfare";

    /// <summary>标签名 → 文案键（忽略大小写，兼容 «Cultures And Religions» 这类写法）。</summary>
    private static readonly Dictionary<string, string> ByTag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alternative History"] = KeyAlternativeHistory,
        ["Balance"] = KeyBalance,
        ["Characters"] = KeyCharacters,
        ["Culture"] = KeyCulture,
        ["Cultures and Religions"] = KeyCulturesAndReligions,
        ["Deprecated"] = KeyDeprecated,
        ["Diplomacy"] = KeyDiplomacy,
        ["Economy and Buildings"] = KeyEconomyAndBuildings,
        ["Events"] = KeyEvents,
        ["Expansion"] = KeyExpansion,
        ["Fixes"] = KeyFixes,
        ["Flags"] = KeyFlags,
        ["Gameplay"] = KeyGameplay,
        ["Graphics"] = KeyGraphics,
        ["Historical"] = KeyHistorical,
        ["Interest Groups"] = KeyInterestGroups,
        ["Interface"] = KeyInterface,
        ["Journal Entries"] = KeyJournalEntries,
        ["Map"] = KeyMap,
        ["Megastructures"] = KeyMegastructures,
        ["Missions"] = KeyMissions,
        ["New Nations"] = KeyNewNations,
        ["Pops"] = KeyPops,
        ["Religion"] = KeyReligion,
        ["Ships"] = KeyShips,
        ["Sound"] = KeySound,
        ["Species"] = KeySpecies,
        ["Technologies"] = KeyTechnologies,
        ["Total Conversion"] = KeyTotalConversion,
        ["Trade"] = KeyTrade,
        ["Translation"] = KeyTranslation,
        ["Units"] = KeyUnits,
        ["Utilities"] = KeyUtilities,
        ["Warfare"] = KeyWarfare,
    };

    /// <summary>全部文案键（去重，供自检核对两份 resw）。</summary>
    public static IReadOnlyCollection<string> AllKeys { get; } =
        ByTag.Values.Distinct(StringComparer.Ordinal).ToList();

    /// <summary>取标签名对应的文案键；未收录返回 null。</summary>
    /// <param name="tag">Steam 标签的机器名或展示名。</param>
    public static string? KeyFor(string? tag) =>
        !string.IsNullOrWhiteSpace(tag) && ByTag.TryGetValue(tag.Trim(), out var key) ? key : null;
}
