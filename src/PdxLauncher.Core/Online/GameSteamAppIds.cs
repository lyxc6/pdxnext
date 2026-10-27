namespace PdxLauncher.Core.Online;

/// <summary>
/// gameId 到 Steam appId 的兜底映射。
/// <para>
/// <see cref="Contracts.GameInstallation.SteamAppId"/> 只在从 Steam 库扫描到 appmanifest 时才有值，
/// 独立安装（或 appmanifest 暂时读不到）时为 null。有了这张表，创意工坊页照样能探测到
/// Steam 创意工坊，而不是把 EU4、CS1、监狱建筑师这些明明有工坊的游戏判成「没有工坊」。
/// </para>
/// </summary>
public static class GameSteamAppIds
{
    /// <summary>已知游戏与其 Steam appId。</summary>
    private static readonly Dictionary<string, int> Known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ck3"] = 1158310,
            ["stellaris"] = 281990,
            ["hoi4"] = 394360,
            ["eu4"] = 236850,
            ["victoria3"] = 529340,
            ["cities_skylines_2"] = 949230,
            ["cities_skylines"] = 255710,
            ["eu5"] = 3450310,
            ["prison_architect_pc_console"] = 233450,
            ["imperator_rome"] = 859580,
            ["ck2"] = 203770,
            ["victoria2"] = 42960,
        };

    /// <summary>
    /// 按 gameId 查 Steam appId（大小写不敏感）。
    /// </summary>
    /// <param name="gameId">启动器内部的游戏 id，例如 <c>eu4</c>。</param>
    /// <param name="appId">查到的 appId；查不到时为 0。</param>
    /// <returns>映射表里有这个游戏时为 true。</returns>
    public static bool TryGet(string? gameId, out int appId)
    {
        if (!string.IsNullOrWhiteSpace(gameId) && Known.TryGetValue(gameId, out var found))
        {
            appId = found;
            return true;
        }

        appId = 0;
        return false;
    }
}
