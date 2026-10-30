namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// <see cref="GameSteamAppIds"/> 与 <see cref="WorkshopSourceChooser.ResolveAppId"/> 自检：
/// 独立安装拿不到 appmanifest（SteamAppId 为 null）时，必须还能靠映射表找到 appId，
/// 否则 EU4 / CS1 / 监狱建筑师会被误判成「没有 Steam 创意工坊」。
/// 不联网。
/// </summary>
public static class GameSteamAppIdsChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckKnownGames();
        CheckUnknownGames();
        CheckResolveAppId();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckKnownGames()
    {
        var expected = new (string GameId, int AppId)[]
        {
            ("ck3", 1158310),
            ("stellaris", 281990),
            ("hoi4", 394360),
            ("eu4", 236850),
            ("victoria3", 529340),
            ("cities_skylines_2", 949230),
            ("cities_skylines", 255710),
            ("eu5", 3450310),
            ("prison_architect_pc_console", 233450),
        };

        foreach (var (gameId, appId) in expected)
        {
            ChecksContext.Check(gameId + " 在映射表里", GameSteamAppIds.TryGet(gameId, out var actual));
            ChecksContext.Equal(gameId + " 的 appId", appId, actual);
        }

        ChecksContext.Check("大小写不敏感（EU4）", GameSteamAppIds.TryGet("EU4", out var upper));
        ChecksContext.Equal("EU4 的 appId", 236850, upper);
        ChecksContext.Check("大小写不敏感（Stellaris）", GameSteamAppIds.TryGet("Stellaris", out var mixed));
        ChecksContext.Equal("Stellaris 的 appId", 281990, mixed);

        // CS1 的官方启动器 id 是 cities_v2，归一后必须查到 255710，
        // 否则独立安装（没有 appmanifest）会退成「无法打补丁」。
        ChecksContext.Check("官方 id cities_v2 命中映射表", GameSteamAppIds.TryGet("cities_v2", out var cs1Alias));
        ChecksContext.Equal("cities_v2 的 appId", 255710, cs1Alias);
        ChecksContext.Equal("CS1 独立安装也能解析 appId", (int?)255710, WorkshopSourceChooser.ResolveAppId(null, "cities_v2"));
    }

    private static void CheckUnknownGames()
    {
        ChecksContext.Check("未知 gameId 不命中", !GameSteamAppIds.TryGet("stellaris_2", out var unknown));
        ChecksContext.Equal("未知 gameId 的 appId 为 0", 0, unknown);
        ChecksContext.Check("null 不命中", !GameSteamAppIds.TryGet(null, out _));
        ChecksContext.Check("空串不命中", !GameSteamAppIds.TryGet(string.Empty, out _));
        ChecksContext.Check("空白不命中", !GameSteamAppIds.TryGet("   ", out _));
    }

    private static void CheckResolveAppId()
    {
        ChecksContext.Equal("安装信息里的 appId 优先", (int?)4242, WorkshopSourceChooser.ResolveAppId(4242, "eu4"));
        ChecksContext.Equal("独立安装回退映射表", (int?)236850, WorkshopSourceChooser.ResolveAppId(null, "eu4"));
        ChecksContext.Equal("appId 为 0 时也回退映射表", (int?)1158310, WorkshopSourceChooser.ResolveAppId(0, "ck3"));
        ChecksContext.Equal("未知游戏且无 appId 时为 null", null, WorkshopSourceChooser.ResolveAppId(null, "unknown_game"));
        ChecksContext.Equal("两者都缺时为 null", null, WorkshopSourceChooser.ResolveAppId(null, null));
    }
}
