namespace PdxLauncher.Core.Online;

/// <summary>
/// 内置的创意工坊筛选组目录，只在「首次运行且联网失败」时兜底。
/// <para>
/// 正常联网时以列表页 SSR 为准，并落盘到 <see cref="WorkshopFilterCatalogCache"/>：
/// 游戏更新后（如 1.14）Steam 的标签目录会变，静态表不可能跟着发布，
/// 所以这里只保证全新安装 + 离线时侧栏不是空的。
/// </para>
/// <para>
/// 数据来自 2026-11 对 <c>steamcommunity.com/workshop/browse/?appid=529340</c> 的实测：
/// 标签组取自 <c>declared_tags_v6.readytouse_tags</c>，DLC 组取自
/// <c>workshop_required_dlc_v5</c> 加 <c>StoreItem/app_&lt;appid&gt;</c> 的商品名。
/// 正常联网时以页面数据为准（Steam 会随版本增删标签/DLC），这里的目录只保证
/// 「第一次请求失败/页面改版」时侧栏仍能用。
/// </para>
/// <para>其他游戏没有静态目录：解析不到就隐藏侧栏，避免给错游戏的标签。</para>
/// </summary>
public static class WorkshopFilterCatalog
{
    /// <summary>Victoria 3。</summary>
    public const int Victoria3AppId = 529340;

    private static readonly IReadOnlyList<SteamWorkshopFilterGroup> Victoria3 =
    [
        new(
            "category",
            string.Empty,
            [
                new("Alternative History", "Alternative History"),
                new("Balance", "Balance"),
                new("Cultures and Religions", "Cultures and Religions"),
                new("Diplomacy", "Diplomacy"),
                new("Events", "Events"),
                new("Expansion", "Expansion"),
                new("Economy and Buildings", "Economy and Buildings"),
                new("Fixes", "Fixes"),
                new("Flags", "Flags"),
                new("Gameplay", "Gameplay"),
                new("Graphics", "Graphics"),
                new("Historical", "Historical"),
                new("Interest Groups", "Interest Groups"),
                new("Journal Entries", "Journal Entries"),
                new("Map", "Map"),
                new("New Nations", "New Nations"),
                new("Pops", "Pops"),
                new("Sound", "Sound"),
                new("Technologies", "Technologies"),
                new("Total Conversion", "Total Conversion"),
                new("Trade", "Trade"),
                new("Translation", "Translation"),
                new("Utilities", "Utilities"),
                new("Warfare", "Warfare"),
            ]),
        new(
            "compatibility",
            "Update Compatibility",
            [
                new("Deprecated", "Deprecated"),
                new("1.9", "1.9"),
                new("1.10", "1.10"),
                new("1.11", "1.11"),
                new("1.12", "1.12"),
                new("1.13", "1.13"),
            ]),
        new(
            "dlc",
            "Required DLC",
            [
                new("2071471", "American Buildings Pack"),
                new("4314670", "Century of Strife - Immersion Pack"),
                new("3450170", "Charters of Commerce - Mechanics Pack"),
                new("2591240", "Colossus of the South - Region Pack"),
                new("2411230", "Dawn of Wonder - Art Pack"),
                new("3563050", "Expansion Pass 2"),
                new("2366580", "French Agitators Bonus Pack"),
                new("3562890", "Iberian Twilight - Immersion Pack"),
                new("2348450", "Melodies for the Masses - Music Pack"),
                new("3562730", "National Awakening - Immersion Pack"),
                new("3174360", "Pivot of Empire - Immersion Pack"),
                new("3562810", "Songs of the Homeland - Music Pack"),
                new("2411231", "Sphere of Influence - Expansion"),
                new("4314660", "State and Revolution - Immersion Pack"),
                new("4314650", "The Great Wave - Expansion"),
                new("3562940", "Trade Ships - Bonus Pack"),
                new("2800730", "Trains - Bonus Pack"),
                new("2282100", "Voice of the People - Immersion Pack"),
                new("4314630", "Volume 3"),
                new("4314640", "Warships - Bonus Pack"),
            ],
            IsDlc: true),
    ];

    /// <summary>取某个 appid 的静态筛选组；没有目录时返回空列表。</summary>
    /// <param name="appId">Steam appid。</param>
    public static IReadOnlyList<SteamWorkshopFilterGroup> ForApp(int appId) =>
        appId == Victoria3AppId ? Victoria3 : [];
}
