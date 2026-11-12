namespace PdxLauncher.Core.Online;

/// <summary>
/// 「哪些游戏用 Steam 创意工坊」的内置事实表。
/// <para>
/// 创意工坊页以前靠联网探测来判定来源（先打 <c>store.steampowered.com/api/appdetails</c>，
/// 失败再抓 <c>steamcommunity.com</c> 的工坊页）。这条路有两个毛病：
/// </para>
/// <list type="number">
/// <item><description>
/// 慢且会卡住页面：两个请求各自的连接超时是 30 秒，网络不通时页面会长时间停在「正在判断…」。
/// </description></item>
/// <item><description>
/// 结果本身不可靠：appdetails 的 <c>categories</c> 接口对一部分老游戏不返回 <c>Steam Workshop</c> 分类，
/// 例如 CK2（203770）与 BattleTech（637090）明明有创意工坊却被判成没有。
/// </description></item>
/// </list>
/// <para>
/// 所以这里把答案写死：键是启动器内部的 gameId（与 <see cref="Games.KnownGames"/> / <see cref="GameSteamAppIds"/>
/// 同一套 id，含官方启动器里的别名），命中即直接定来源、一个网络请求都不发。
/// 未登记的游戏（例如以后新增的作品）返回 <c>null</c>，由调用方退回探测。
/// </para>
/// <para>
/// 判断错了只改这一张表：<c>true</c> = 有 Steam 创意工坊，<c>false</c> = 没有（走 Paradox Mods）。
/// 不要往 <see cref="Games.KnownGames"/> 里塞这个信息，那边是「支持等级」语义，混在一起容易改错。
/// </para>
/// </summary>
public static class SteamWorkshopGames
{
    /// <summary>
    /// gameId → 是否有 Steam 创意工坊。比较一律忽略大小写。
    /// 每行注释里写着对应的 Steam appId，便于以后核对。
    /// </summary>
    private static readonly Dictionary<string, bool> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---- 有 Steam 创意工坊 ----
        ["ck3"] = true,                      // 1158310 Crusader Kings III
        ["victoria3"] = true,                // 529340 Victoria 3
        ["stellaris"] = true,                // 281990 Stellaris
        ["eu4"] = true,                      // 236850 Europa Universalis IV
        ["eu5"] = true,                      // 3450310 Europa Universalis V
        ["hoi4"] = true,                     // 394360 Hearts of Iron IV
        ["imperator_rome"] = true,           // 859580 Imperator: Rome
        ["cities_skylines"] = true,          // 255710 Cities: Skylines
        ["ck2"] = true,                      // 203770 Crusader Kings II（appdetails 分类接口不返回 Workshop，实际有工坊）
        ["prison_architect_pc_console"] = true, // 233450 Prison Architect
        ["aow4"] = true,                     // 1669000 Age of Wonders 4
        ["aow_planetfall"] = true,           // 524220 Age of Wonders: Planetfall
        ["surviving_mars"] = true,           // 464920 Surviving Mars
        ["surviving_the_aftermath"] = true,  // 684450 Surviving the Aftermath
        ["battletech"] = true,               // 637090 BattleTech（同上，分类接口不返回 Workshop，实际有工坊）
        ["foundry"] = true,                  // 983870 Foundry
        ["across_the_obelisk"] = true,       // 1385380 Across the Obelisk
        ["star_trek_infinite"] = true,       // 2473900 Star Trek: Infinite
        ["millenia"] = true,                 // Millennia
        ["empire_of_sin"] = true,            // 604230 Empire of Sin
        ["the_lamplighters_league"] = true,  // 1167750 The Lamplighters League
        ["stardeus"] = true,                 // Stardeus

        // ---- 没有 Steam 创意工坊（内容在 Paradox Mods 或游戏本体里）----
        ["cities_skylines_2"] = false,       // 949230 Cities: Skylines II：mod 走 Paradox Mods
        ["victoria2"] = false,               // 42960 Victoria II
        ["eu3"] = false,                     // 25800 Europa Universalis III
        ["hoi3"] = false,                    // 25890 Hearts of Iron III
        ["moto_gp"] = false,                 // MotoGP
        ["pillars_of_eternity"] = false,     // Pillars of Eternity
        ["tyranny"] = false,                 // Tyranny
        ["magicka2"] = false,                // Magicka 2
        ["steel_division"] = false,          // Steel Division

        // ---- 官方启动器里的 gameId 别名：与正名同一答案 ----
        ["infinite"] = true,                 // Star Trek: Infinite 的别名
        ["prison_architect"] = true,         // Prison Architect 的别名
        ["surviving_aftermath"] = true,      // Surviving the Aftermath 的别名
        ["surviving_mars_relaunched"] = true, // Surviving Mars: Relaunched 的别名
    };

    /// <summary>
    /// 查某个 gameId 该用哪个创意工坊。
    /// </summary>
    /// <param name="gameId">启动器内部的游戏 id，可以带别名，大小写不限。</param>
    /// <returns>
    /// <c>true</c> = 有 Steam 创意工坊；<c>false</c> = 没有；<c>null</c> = 未登记，调用方自己探测。
    /// </returns>
    public static bool? TryGet(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return null;
        }

        // 官方启动器的别名（CS1 = cities_v2）与正名同答案。
        var canonical = Games.KnownGames.CanonicalizeGameId(gameId);
        return Known.TryGetValue(canonical, out var hasWorkshop) ? hasWorkshop : null;
    }

    /// <summary>
    /// 这个游戏能不能在启动器里打开 Steam 创意工坊（按内置表）。
    /// <para>
    /// 与「能不能管理游戏内容」是两件事：仅启动的游戏（Cities: Skylines 1 / Prison Architect）
    /// 也有工坊，浏览与订阅只碰 Steam、不碰游戏目录，所以照样放行；
    /// 未登记的游戏返回 false，由页面自己去探测。
    /// </para>
    /// </summary>
    /// <param name="gameId">启动器内部的游戏 id。</param>
    public static bool HasWorkshop(string? gameId) => TryGet(gameId) == true;

    /// <summary>全部已登记的 gameId（便于日志与自检，不含顺序语义）。</summary>
    public static IReadOnlyCollection<string> All => Known.Keys;

    /// <summary>登记的条目数量（诊断用）。</summary>
    public static int Count => Known.Count;
}
