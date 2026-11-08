namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 「哪些游戏用 Steam 创意工坊」内置表自检：<see cref="SteamWorkshopGames"/> 与
/// <see cref="WorkshopSourceChooser"/> 的三参判定。
/// <para>
/// 起因：来源判定以前全靠联网探测，慢且在断网时把页面卡在「正在判断…」；
/// 而 appdetails 的分类接口对 CK2、BattleTech 这类老游戏根本不返回 Workshop 分类，
/// 探测结果本身就是错的。这里把「表优先、表里没有才探测」的口径钉死。
/// </para>
/// <para>纯内存用例，不联网。</para>
/// </summary>
public static class SteamWorkshopGamesChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckHasWorkshop();
        CheckNoWorkshop();
        CheckUnregistered();
        CheckAliases();
        CheckAppIdTableCovered();
        CheckChooser();
        CheckLaunchOnlyGames();
        return ChecksContext.Failures - before;
    }

    /// <summary>
    /// 仅启动的游戏也要能打开创意工坊：CS1 / PA 的工坊只碰 Steam、不碰游戏目录，
    /// 所以「有没有工坊」与「能不能管理内容」是两套判定，别被 SupportsContentManagement 一起关掉。
    /// </summary>
    private static void CheckLaunchOnlyGames()
    {
        ChecksContext.Equal("CS1 是仅启动但仍然有工坊", true, SteamWorkshopGames.HasWorkshop("cities_skylines"));
        ChecksContext.Equal("PA 是仅启动但仍然有工坊", true, SteamWorkshopGames.HasWorkshop("prison_architect_pc_console"));
        ChecksContext.Equal("未登记游戏 HasWorkshop 为 false", false, SteamWorkshopGames.HasWorkshop("unknown_game"));
        ChecksContext.Equal("null gameId → HasWorkshop false", false, SteamWorkshopGames.HasWorkshop(null));
        ChecksContext.Equal("CS2 走 Paradox Mods → HasWorkshop false", false, SteamWorkshopGames.HasWorkshop("cities_skylines_2"));
    }

    /// <summary>有创意工坊的游戏，包括被 appdetails 分类接口误判成「没有」的那几个。</summary>
    private static void CheckHasWorkshop()
    {
        ChecksContext.Equal("CK3 有 Steam 工坊", true, SteamWorkshopGames.TryGet("ck3"));
        ChecksContext.Equal("Stellaris 有 Steam 工坊", true, SteamWorkshopGames.TryGet("stellaris"));
        ChecksContext.Equal("EU4 有 Steam 工坊", true, SteamWorkshopGames.TryGet("eu4"));
        ChecksContext.Equal("Victoria 3 有 Steam 工坊", true, SteamWorkshopGames.TryGet("victoria3"));
        ChecksContext.Equal("Cities: Skylines 有 Steam 工坊", true, SteamWorkshopGames.TryGet("cities_skylines"));
        ChecksContext.Equal("EU5 有 Steam 工坊", true, SteamWorkshopGames.TryGet("eu5"));

        // 这两个是「不能信探测」的回归样例：appdetails 不返回 Workshop 分类，但工坊实际存在。
        ChecksContext.Equal("CK2 有 Steam 工坊（分类接口不返回也要认）", true, SteamWorkshopGames.TryGet("ck2"));
        ChecksContext.Equal("BattleTech 有 Steam 工坊（分类接口不返回也要认）", true, SteamWorkshopGames.TryGet("battletech"));
    }

    /// <summary>内容不在 Steam 工坊里的游戏。</summary>
    private static void CheckNoWorkshop()
    {
        ChecksContext.Equal("CS2 没有 Steam 工坊（走 Paradox Mods）", false, SteamWorkshopGames.TryGet("cities_skylines_2"));
        ChecksContext.Equal("Victoria II 没有 Steam 工坊", false, SteamWorkshopGames.TryGet("victoria2"));
        ChecksContext.Equal("EU3 没有 Steam 工坊", false, SteamWorkshopGames.TryGet("eu3"));
        ChecksContext.Equal("HOI3 没有 Steam 工坊", false, SteamWorkshopGames.TryGet("hoi3"));
    }

    /// <summary>未登记 = null，由调用方退回探测。</summary>
    private static void CheckUnregistered()
    {
        ChecksContext.Equal("null gameId 未登记", null, SteamWorkshopGames.TryGet(null));
        ChecksContext.Equal("空串未登记", null, SteamWorkshopGames.TryGet(string.Empty));
        ChecksContext.Equal("空白未登记", null, SteamWorkshopGames.TryGet("   "));
        ChecksContext.Equal("未知游戏未登记", null, SteamWorkshopGames.TryGet("unknown_game"));
    }

    /// <summary>官方启动器里的 id 别名必须和正名同答案。</summary>
    private static void CheckAliases()
    {
        ChecksContext.Equal("别名 infinite = star_trek_infinite",
            SteamWorkshopGames.TryGet("star_trek_infinite"),
            SteamWorkshopGames.TryGet("infinite"));
        ChecksContext.Equal("别名 prison_architect = prison_architect_pc_console",
            SteamWorkshopGames.TryGet("prison_architect_pc_console"),
            SteamWorkshopGames.TryGet("prison_architect"));
        ChecksContext.Equal("别名 surviving_aftermath = surviving_the_aftermath",
            SteamWorkshopGames.TryGet("surviving_the_aftermath"),
            SteamWorkshopGames.TryGet("surviving_aftermath"));
        ChecksContext.Equal("别名 surviving_mars_relaunched = surviving_mars",
            SteamWorkshopGames.TryGet("surviving_mars"),
            SteamWorkshopGames.TryGet("surviving_mars_relaunched"));

        // Cities: Skylines 1 的官方启动器 id 是 cities_v2，归一后与正名同答案。
        ChecksContext.Equal("别名 cities_v2 = cities_skylines",
            SteamWorkshopGames.TryGet("cities_skylines"),
            SteamWorkshopGames.TryGet("cities_v2"));
        ChecksContext.Equal("别名 cities_v2 也有 Steam 工坊", true, SteamWorkshopGames.TryGet("cities_v2"));

        // 大小写不敏感（安装目录/启动器设置里的 gameId 大小写并不统一）。
        ChecksContext.Equal("大写 CK3 也认", true, SteamWorkshopGames.TryGet("CK3"));
        ChecksContext.Equal("混合大小写 CS2 也认", false, SteamWorkshopGames.TryGet("Cities_Skylines_2"));
    }

    /// <summary>
    /// <see cref="GameSteamAppIds"/> 里登记的游戏都必须在这张表里有答案，
    /// 否则「能查到 appId 却不知道有没有工坊」会退回慢探测，两表就漂移了。
    /// </summary>
    private static void CheckAppIdTableCovered()
    {
        string[] gameIds =
        [
            "ck3", "stellaris", "hoi4", "eu4", "victoria3", "cities_skylines_2",
            "cities_skylines", "eu5", "prison_architect_pc_console", "imperator_rome", "ck2", "victoria2",
        ];

        foreach (var gameId in gameIds)
        {
            ChecksContext.Check(
                "appId 表里的游戏都有工坊答案：" + gameId,
                SteamWorkshopGames.TryGet(gameId) is not null);
        }
    }

    /// <summary>三参判定：表命中就无视探测结果；表里没有才按 appId + 探测结果走。</summary>
    private static void CheckChooser()
    {
        ChecksContext.Equal("CK3 不探测也是 Steam",
            WorkshopSource.Steam,
            WorkshopSourceChooser.Resolve(1158310, null, "ck3"));

        ChecksContext.Equal("CS2 即使探测说 true 也按表走 Paradox Mods",
            WorkshopSource.Pdx,
            WorkshopSourceChooser.Resolve(949230, true, "cities_skylines_2"));

        ChecksContext.Equal("未登记游戏退回探测结果（true → Steam）",
            WorkshopSource.Steam,
            WorkshopSourceChooser.Resolve(1158310, true, "unknown_game"));

        ChecksContext.Equal("未登记游戏退回探测结果（null → Paradox Mods）",
            WorkshopSource.Pdx,
            WorkshopSourceChooser.Resolve(1158310, null, "unknown_game"));

        ChecksContext.Equal("未登记且没有 appId → Paradox Mods",
            WorkshopSource.Pdx,
            WorkshopSourceChooser.Resolve(null, null, "unknown_game"));
    }
}
