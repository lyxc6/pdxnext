namespace PdxLauncher.Core.Games;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 已知 Paradox 游戏的元数据表，只做三件事：
/// <list type="bullet">
/// <item><description>身份归一：<see cref="CanonicalizeGameId"/> 把官方启动器里的别名折成正名；</description></item>
/// <item><description>展示名：<c>launcher-settings.json</c> 缺少 displayName 时的兜底；</description></item>
/// <item><description>支持等级：由 <see cref="GetSupport"/> 给出（完整适配 / 仅启动 / 未适配）。</description></item>
/// </list>
/// 这张表不参与"是否收录"的判断：只要目录里有可解析的 launcher-settings.json，
/// <see cref="GameCatalog"/> 就会收录；未登记的 gameId 支持等级为 <see cref="GameSupport.Unsupported"/>。
/// </summary>
public static class KnownGames
{
    /// <summary>
    /// 官方启动器写进 <c>launcher-settings.json</c> 的别名 → 本启动器内部正名。
    /// <para>
    /// 目前只有一条：Cities: Skylines 1 的 <c>gameId</c> 是 <c>cities_v2</c>（官方启动器 v2 的内部编号），
    /// 而 Paradox 元数据 API、<see cref="Online.GameSteamAppIds"/>、<see cref="Online.SteamWorkshopGames"/>
    /// 以及本仓库所有表都只认 <c>cities_skylines</c>。不折正名，CS1 会被判成「未适配」。
    /// </para>
    /// <para>
    /// 归一只发生在发现阶段（<see cref="GameCatalog"/>）与各查询入口，<c>LauncherSettings.GameId</c>
    /// 保留原始值不动；公告接口需要反向特例，见 <see cref="GetNewsFeedGameId"/>。
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> IdentityAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["cities_v2"] = "cities_skylines",
        };

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
        ["cities_v2"] = "Cities: Skylines",
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
    /// <para>
    /// Cities: Skylines 1：本体同样不读 content_load.json（全量扫描 229 个 dll/exe 搜
    /// <c>content_load</c> / <c>enabledMods</c> / <c>disabledDLC</c> / <c>dlc_signature</c> 全部 0 命中），
    /// 安装目录里也没有任何 <c>.dlc</c> 描述文件；mod 由游戏内 Colossal Mods Domain 枚举
    /// <c>Addons\Mods</c> 与 Steam 工坊内容目录（<c>ISteamUGC</c>）自行加载，
    /// 开关状态存在游戏自己的 <c>modsSettings</c> / <c>.cgs</c> 里；DLC 归属纯由 Steam 授权决定。
    /// </para>
    /// </summary>
    private static readonly HashSet<string> LaunchOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "prison_architect_pc_console",
        "cities_skylines",
    };

    /// <summary>
    /// 把官方启动器里的 gameId 别名折成本启动器的正名（大小写不敏感）。
    /// <para>
    /// 只做「同一款游戏的不同写法」这一件事：目前 <c>cities_v2</c> → <c>cities_skylines</c>。
    /// 未登记的 id 原样返回（含大小写），空值返回空串——调用方不必再判空。
    /// </para>
    /// </summary>
    /// <param name="gameId">launcher-settings.json 里的原始 gameId。</param>
    /// <returns>正名；不认识时返回原值。</returns>
    public static string CanonicalizeGameId(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return string.Empty;
        }

        return IdentityAliases.TryGetValue(gameId, out var canonical) ? canonical : gameId;
    }

    /// <summary>
    /// 公告接口（Braze contentcards）该用哪个 gameId。
    /// <para>
    /// 这是归一的反向特例，唯一的一处：Paradox 的公告接口只认官方启动器写下的原始 id，
    /// 实测 <c>contentcards/cities_v2</c> 返回 3 张卡、<c>contentcards/cities_skylines</c> 返回 <c>{}</c>。
    /// 其它任何地方（元数据、主题、appId、工坊、补丁）都必须用正名。
    /// </para>
    /// </summary>
    /// <param name="gameId">安装项的 gameId（通常已是正名）。</param>
    /// <returns>公告接口的 gameId；没有反向特例时返回正名。</returns>
    public static string GetNewsFeedGameId(string? gameId)
    {
        var canonical = CanonicalizeGameId(gameId);
        return string.Equals(canonical, "cities_skylines", StringComparison.OrdinalIgnoreCase)
            ? "cities_v2"
            : canonical;
    }

    /// <summary>是否是展示名表里登记过的 Paradox 游戏 gameId（不决定是否收录）。</summary>
    public static bool IsKnown(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        return DisplayNames.ContainsKey(CanonicalizeGameId(gameId));
    }

    /// <summary>已知展示名；不认识时返回 null。</summary>
    public static string? GetDisplayName(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return null;
        }

        return DisplayNames.TryGetValue(CanonicalizeGameId(gameId), out var name) ? name : null;
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

        var canonical = CanonicalizeGameId(gameId);
        if (LaunchOnly.Contains(canonical))
        {
            return GameSupport.LaunchOnly;
        }

        return FullSupport.Contains(canonical) ? GameSupport.Full : GameSupport.Unsupported;
    }

    /// <summary>
    /// 是否是本启动器完全支持的 gameId（能读写加载文件、能读本地主题）。
    /// 等价于 <c>GetSupport(gameId) == GameSupport.Full</c>。
    /// </summary>
    public static bool IsFullySupported(string? gameId) => GetSupport(gameId) == GameSupport.Full;

    /// <summary>全部已知 gameId（便于日志/诊断）。</summary>
    public static IReadOnlyCollection<string> All => DisplayNames.Keys;
}
