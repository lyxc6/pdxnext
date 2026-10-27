namespace PdxLauncher.Core.Games;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 已知 Paradox 游戏的元数据表，只做两件事：
/// <list type="bullet">
/// <item><description>展示名：<c>launcher-settings.json</c> 缺少 displayName 时的兜底；</description></item>
/// <item><description>支持等级：由 <see cref="GetSupport"/> 给出（完整适配 / 仅启动 / 未适配）。</description></item>
/// </list>
/// 这张表不参与"是否收录"的判断：只要目录里有可解析的 launcher-settings.json，
/// <see cref="GameCatalog"/> 就会收录；未登记的 gameId 支持等级为 <see cref="GameSupport.Unsupported"/>。
/// </summary>
public static class KnownGames
{
    /// <summary>gameId → 展示名（仅用于缺 displayName 时的兜底与日志）。比较一律忽略大小写。</summary>
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ck3"] = "Crusader Kings III",
        ["victoria3"] = "Victoria 3",
        ["stellaris"] = "Stellaris",
        ["eu4"] = "Europa Universalis IV",
        ["eu5"] = "Europa Universalis V",
        ["hoi4"] = "Hearts of Iron IV",
        ["imperator_rome"] = "Imperator: Rome",
        ["cities_skylines_2"] = "Cities: Skylines II",
        ["cities_skylines"] = "Cities: Skylines",
        ["millenia"] = "Millennia",
        ["aow4"] = "Age of Wonders 4",
        ["aow_planetfall"] = "Age of Wonders: Planetfall",
        ["prison_architect_pc_console"] = "Prison Architect",
        ["surviving_mars"] = "Surviving Mars",
        ["surviving_the_aftermath"] = "Surviving the Aftermath",
        ["battletech"] = "BattleTech",
        ["stardeus"] = "Stardeus",
        ["across_the_obelisk"] = "Across the Obelisk",
        ["star_trek_infinite"] = "Star Trek: Infinite",
        ["the_lamplighters_league"] = "The Lamplighters League",
        ["empire_of_sin"] = "Empire of Sin",
        ["foundry"] = "Foundry",
        ["moto_gp"] = "MotoGP",

        // 官方启动器里的 gameId 别名：同一款游戏在 Steam 版、独立版、商店链接里的 id 并不统一，
        // 这里只补展示名兜底（支持等级仍由 FullSupport / LaunchOnly 决定）。
        ["infinite"] = "Star Trek: Infinite",
        ["prison_architect"] = "Prison Architect",
        ["surviving_aftermath"] = "Surviving the Aftermath",
        ["surviving_mars_relaunched"] = "Surviving Mars: Relaunched",
        ["pillars_of_eternity"] = "Pillars of Eternity",
        ["tyranny"] = "Tyranny",
        ["magicka2"] = "Magicka 2",
        ["steel_division"] = "Steel Division",
        ["ck2"] = "Crusader Kings II",
        ["eu3"] = "Europa Universalis III",
        ["hoi3"] = "Hearts of Iron III",
        ["victoria2"] = "Victoria II",
    };

    /// <summary>完整适配的 gameId：播放集 / mod / DLC / 加载文件全部由启动器接管。</summary>
    private static readonly HashSet<string> FullSupport = new(StringComparer.OrdinalIgnoreCase)
    {
        "ck3",
        "victoria3",
        "stellaris",
        "eu4",
        "eu5",
        "hoi4",
        "imperator_rome",
        "cities_skylines_2",
        "millenia",
        "aow4",
    };

    /// <summary>
    /// 仅启动适配的 gameId：启动 / 主题 / 版本可用，内容管理交给游戏本体。
    /// Prison Architect：本体不读 content_load.json、没有 launcher-v2.sqlite、
    /// mod 走 <c>gameDataDir\mods\&lt;name&gt;\manifest.txt</c>、DLC 只由 Steam 授权控制。
    /// </summary>
    private static readonly HashSet<string> LaunchOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "prison_architect_pc_console",
    };

    /// <summary>是否是展示名表里登记过的 Paradox 游戏 gameId（不决定是否收录）。</summary>
    public static bool IsKnown(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        return DisplayNames.ContainsKey(gameId);
    }

    /// <summary>已知展示名；不认识时返回 null。</summary>
    public static string? GetDisplayName(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return null;
        }

        return DisplayNames.TryGetValue(gameId, out var name) ? name : null;
    }

    /// <summary>
    /// 游戏的支持等级；gameId 为空或不认识时返回 <see cref="GameSupport.Unsupported"/>。
    /// </summary>
    public static GameSupport GetSupport(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return GameSupport.Unsupported;
        }

        if (LaunchOnly.Contains(gameId))
        {
            return GameSupport.LaunchOnly;
        }

        return FullSupport.Contains(gameId) ? GameSupport.Full : GameSupport.Unsupported;
    }

    /// <summary>
    /// 是否是本启动器完全支持的 gameId（能读写加载文件、能读本地主题）。
    /// 等价于 <c>GetSupport(gameId) == GameSupport.Full</c>。
    /// </summary>
    public static bool IsFullySupported(string? gameId) => GetSupport(gameId) == GameSupport.Full;

    /// <summary>全部已知 gameId（便于日志/诊断）。</summary>
    public static IReadOnlyCollection<string> All => DisplayNames.Keys;
}
