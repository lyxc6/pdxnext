namespace PdxLauncher.Core.Online;

/// <summary>创意工坊页当前显示哪个平台的内容。</summary>
public enum WorkshopSource
{
    /// <summary>还没决定（没有游戏，或探测中）。</summary>
    None,

    /// <summary>Steam 创意工坊（有 Steam 工坊的游戏）。</summary>
    Steam,

    /// <summary>Paradox Mods（CS2 这类没有 Steam 工坊的游戏，或独立版）。</summary>
    Pdx,
}

/// <summary>
/// 「这个游戏该显示哪个工坊」的纯判定：抽出来是为了能在自检里直接测四种组合，
/// 不必拉起视图模型。
/// </summary>
public static class WorkshopSourceChooser
{
    /// <summary>
    /// 解析某款游戏该用哪个 Steam appId 去探测工坊。
    /// <para>
    /// 优先用安装信息里的 appId；独立安装时那里是 null，回退到 <see cref="GameSteamAppIds"/> 的
    /// 映射表，避免「拿不到 appmanifest 就当没有 Steam 工坊」。
    /// </para>
    /// </summary>
    /// <param name="steamAppId">安装信息里的 Steam appId，可能为 null。</param>
    /// <param name="gameId">启动器内部的游戏 id。</param>
    /// <returns>可用的 appId；确实没有对应 Steam 版本时为 null。</returns>
    public static int? ResolveAppId(int? steamAppId, string? gameId)
    {
        if (steamAppId is > 0)
        {
            return steamAppId;
        }

        return GameSteamAppIds.TryGet(gameId, out var mapped) ? mapped : null;
    }

    /// <summary>
    /// <paramref name="steamAppId"/> 为 null（独立版）→ PDX；
    /// 探测结果为 true → Steam；false → PDX；还没探测（null）→ PDX。
    /// </summary>
    /// <param name="steamAppId">游戏的 Steam appId；独立版为 null。</param>
    /// <param name="hasWorkshop">Steam 工坊探测结果；未探测为 null。</param>
    /// <returns>该游戏对应的创意工坊来源。</returns>
    public static WorkshopSource Resolve(int? steamAppId, bool? hasWorkshop)
    {
        if (steamAppId is null)
        {
            return WorkshopSource.Pdx;
        }

        return hasWorkshop == true ? WorkshopSource.Steam : WorkshopSource.Pdx;
    }

    /// <summary>
    /// 带内置表的判定：<see cref="SteamWorkshopGames"/> 里登记过就按表定来源（不看探测结果、也不发请求），
    /// 只有未登记的游戏才退回 <see cref="Resolve(int?, bool?)"/> 的两参逻辑。
    /// </summary>
    /// <param name="steamAppId">游戏的 Steam appId；独立版为 null。</param>
    /// <param name="hasWorkshop">Steam 工坊探测结果；未探测为 null。</param>
    /// <param name="gameId">启动器内部的游戏 id。</param>
    /// <returns>该游戏对应的创意工坊来源。</returns>
    public static WorkshopSource Resolve(int? steamAppId, bool? hasWorkshop, string? gameId)
    {
        if (SteamWorkshopGames.TryGet(gameId) is { } known)
        {
            return known ? WorkshopSource.Steam : WorkshopSource.Pdx;
        }

        return Resolve(steamAppId, hasWorkshop);
    }
}
