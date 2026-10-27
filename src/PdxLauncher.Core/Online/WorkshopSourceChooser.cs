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
}
