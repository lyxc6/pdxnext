namespace PdxLauncher.Core.Online;

/// <summary>
/// 某个 Steam app 最近一次拿到的工坊筛选目录快照（分组、兼容性版本、所需 DLC）。
/// <para>
/// Steam 没有筛选目录的推送，也没有单独的轻量接口；目录只能随列表页 SSR 一起拿到
/// （<c>declared_tags_v6</c> / <c>workshop_required_dlc_v5</c>）。把最近一次结果落盘后，
/// 重启、离线或页缓存命中时都能先显示已知的最新版本，再按 24h TTL / 本地版本变化补刷新。
/// </para>
/// </summary>
/// <param name="AppId">Steam appid。</param>
/// <param name="GameVersion">抓取时本地游戏的版本（<c>GameInstallation.Version</c>，可能为空）。</param>
/// <param name="FetchedAtUtc">抓取时刻（UTC）。</param>
/// <param name="Groups">筛选分组。</param>
public sealed record WorkshopFilterCatalogSnapshot(
    int AppId,
    string GameVersion,
    DateTimeOffset FetchedAtUtc,
    IReadOnlyList<SteamWorkshopFilterGroup> Groups);
