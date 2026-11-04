namespace PdxLauncher.Core.Steam;

using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Patch;

/// <summary>Steam 授权探测的总体结果。</summary>
public enum SteamOwnershipState
{
    /// <summary>没法探测：缺 appid、游戏目录没有 api DLL、32 位游戏，或不是 Windows。</summary>
    Unavailable,

    /// <summary>Steam 客户端没有运行。</summary>
    SteamNotRunning,

    /// <summary><c>SteamAPI_Init</c> 失败（未登录、appid 已被占用、接口被别的页面占着）。</summary>
    InitFailed,

    /// <summary>探测成功，<see cref="SteamOwnershipReport.Dlcs"/> 可用。</summary>
    Succeeded,
}

/// <summary>Steam 眼里一个 DLC 的授权情况。</summary>
/// <param name="AppId">DLC 的 Steam appid。</param>
/// <param name="Name">Steam 里的 DLC 名称（可能为空，空的会被跳过）。</param>
/// <param name="IsOwned">是否已拥有（<c>BIsSubscribedApp</c> 或 <c>BIsDlcInstalled</c>，另加可信 DisabledDLC）。</param>
/// <param name="IsInstalled">Steam 报的可用位（<c>BGetDLCDataByIndex</c> 的 <c>pbAvailable</c>）。</param>
public sealed record SteamDlcOwnership(string AppId, string Name, bool IsOwned, bool IsInstalled);

/// <summary>拿磁盘上的 DLC 去跟 Steam 授权对账时用的最小输入。</summary>
public sealed record DlcOwnershipCandidate(string SteamId, string DisplayName);

/// <summary>
/// 用原版备份（<c>steam_api64_o.dll</c>）探测到的「账号真实拥有」视图。
/// <para>
/// 与主报告分开：主报告回答「游戏现在能用哪些 DLC」，这里回答「账号本身拥有哪些 DLC」。
/// loader 在岗时两者会差很多（loader 的 ini 表常混入其他游戏），混在一起就会被误读成账号授权。
/// </para>
/// </summary>
/// <param name="State">探测状态。</param>
/// <param name="ApiFilePath">实际探测的备份 DLL 全路径（证据句里给用户核对）。</param>
/// <param name="SteamId64">探测时 Steam 返回的账号；0 表示没拿到。</param>
/// <param name="Dlcs">Steam 目录列出的 DLC。</param>
/// <param name="OwnedAppIds">已授权的 appid 集合（同样经过可信 DisabledDLC 升级）。</param>
/// <param name="ErrorKey">不可用原因键（见 <see cref="SteamProbeError"/>）；成功时为 null。</param>
/// <param name="Detail">技术细节。</param>
public sealed record DlcAccountOwnership(
    SteamOwnershipState State,
    string? ApiFilePath,
    ulong SteamId64,
    IReadOnlyList<SteamDlcOwnership> Dlcs,
    IReadOnlySet<string> OwnedAppIds,
    string? ErrorKey,
    string? Detail)
{
    /// <summary>这份账号视图是否可信可用。</summary>
    public bool HasOwnership => State == SteamOwnershipState.Succeeded;
}

/// <summary>
/// 一次授权探测的完整结果。口径对齐官方启动器的 <c>SteamService</c>：
/// 拥有 = <c>BIsSubscribedApp</c> || 可信 <c>DisabledDLC</c> || <c>BIsDlcInstalled</c>。
/// </summary>
/// <param name="State">总体状态。</param>
/// <param name="PatchStatus">打补丁状态（<see cref="GamePatchStatus"/>）。</param>
/// <param name="SteamAppId">探测时喂给 steam_api 的 appid。</param>
/// <param name="ApiDirectory">探测的目录。</param>
/// <param name="ApiFileName">探测的文件名（<c>steam_api64.dll</c> 才是真正生效的那份）。</param>
/// <param name="ApiFilePath">探测的 DLL 全路径（证据句里给用户核对用）。</param>
/// <param name="SteamId64">Steam 返回的账号；0 表示没拿到。</param>
/// <param name="ManifestLastOwner">appmanifest 的 <c>LastOwner</c>。</param>
/// <param name="ManifestOwnerMatches">清单里的 <c>LastOwner</c> 是否等于当前账号（官方信不信 DisabledDLC 的闸门）。</param>
/// <param name="Dlcs">Steam 列出的 DLC（loader 在岗时来自 <c>cream_api.ini</c>，不是 Steam 目录）。</param>
/// <param name="OwnedAppIds">已授权的 appid 集合（大小写不敏感的字符串形式）。</param>
/// <param name="TrustedDisabledDlc">采信的 <c>DisabledDLC</c> 集合。</param>
/// <param name="ErrorKey">失败原因键（见 <see cref="SteamProbeError"/>）；成功时为 null。</param>
/// <param name="Detail">原生错误文本等技术细节。</param>
/// <param name="IsLoaderActive">被探测的 DLL 是否就是 loader（决定 <paramref name="Dlcs"/> 的来源）。</param>
/// <param name="Account">原版备份的账号视图；没有备份、备份疑似 loader 或未探测时为 null。</param>
public sealed record SteamOwnershipReport(
    SteamOwnershipState State,
    GamePatchStatus PatchStatus,
    int? SteamAppId,
    string? ApiDirectory,
    string? ApiFileName,
    string? ApiFilePath,
    ulong SteamId64,
    string? ManifestLastOwner,
    bool ManifestOwnerMatches,
    IReadOnlyList<SteamDlcOwnership> Dlcs,
    IReadOnlySet<string> OwnedAppIds,
    IReadOnlySet<string> TrustedDisabledDlc,
    string? ErrorKey,
    string? Detail,
    bool IsLoaderActive = false,
    DlcAccountOwnership? Account = null)
{
    /// <summary>探测是否可用（决定界面上显示「已拥有 / 未拥有」还是「授权未知」）。</summary>
    public bool HasOwnership => State == SteamOwnershipState.Succeeded;
}

/// <summary>一次原生探测的结果。</summary>
/// <param name="Initialized"><c>SteamAPI_Init</c> 是否成功。</param>
/// <param name="SteamRunning">Steam 客户端是否在运行（<c>SteamAPI_IsSteamRunning</c>）。</param>
/// <param name="SteamId64">当前账号的 SteamID64；0 表示没取到。</param>
/// <param name="Dlcs">Steam 列出的 DLC（已过滤掉空名）。</param>
/// <param name="Error">失败原因键；成功时为 null。</param>
/// <param name="ErrorDetail">原生异常文本。</param>
public sealed record SteamDllProbeResult(
    bool Initialized,
    bool SteamRunning,
    ulong SteamId64,
    IReadOnlyList<SteamDlcOwnership> Dlcs,
    string? Error,
    string? ErrorDetail = null)
{
    /// <summary>探测是否完全成功。</summary>
    public bool Succeeded => Error is null && Initialized && SteamRunning;

    /// <summary>构造一个失败结果。</summary>
    public static SteamDllProbeResult Fail(string error, string? detail = null) =>
        new(false, false, 0, Array.Empty<SteamDlcOwnership>(), error, detail);
}

/// <summary>原生探针（便于自检注入假件）。</summary>
public interface ISteamDllProbe
{
    /// <summary>加载 <paramref name="apiFilePath"/> 并询问 Steam 授权。</summary>
    SteamDllProbeResult Probe(string apiFilePath, int appId);
}

/// <summary>
/// 判定「游戏目录里这些 DLC 在 Steam 眼里是不是已拥有」。
/// <para>
/// 探测目标是游戏目录里真正被加载的那份 <c>steam_api64.dll</c>：打过补丁时它是 loader，
/// 于是它报告的授权就是游戏实际看到的授权——这正是「补丁有没有生效」的直接证据。
/// </para>
/// </summary>
public interface ISteamOwnershipService
{
    /// <summary>只读探测；失败时返回带 <see cref="SteamOwnershipReport.ErrorKey"/> 的报告，不抛异常。</summary>
    SteamOwnershipReport Inspect(GameInstallation game, CancellationToken cancellationToken);

    /// <summary>丢弃缓存（设置页打完/还原补丁后调用）。</summary>
    void Invalidate();
}

/// <summary>探测失败原因的键。</summary>
public static class SteamProbeError
{
    /// <summary>不知道这款游戏的 Steam appid。</summary>
    public const string NoAppId = "noAppId";

    /// <summary>游戏目录里没有 steam_api DLL（也没检测到补丁目标）。</summary>
    public const string NoDll = "noDll";

    /// <summary>32 位游戏的 <c>steam_api.dll</c> 不能在 64 位进程里加载。</summary>
    public const string X86 = "x86";

    /// <summary>不是 Windows。</summary>
    public const string NotWindows = "notWindows";

    /// <summary>Steam 接口正被别的页面占用。</summary>
    public const string Busy = "busy";

    /// <summary>Steam 客户端没有运行。</summary>
    public const string SteamNotRunning = "steamNotRunning";

    /// <summary><c>SteamAPI_Init</c> 返回 false。</summary>
    public const string InitFailed = "initFailed";

    /// <summary>原版备份 <c>steam_api64_o.dll</c> 与当前 loader 字节相同或吐出同一份表，疑似也不是原版。</summary>
    public const string BackupNotOriginal = "backupNotOriginal";

    /// <summary>原生调用抛异常。</summary>
    public const string Native = "native";

    /// <summary>
    /// Steam 宿主子进程没能启动、超时或被提前杀掉（结果行没回来）。
    /// 与 <see cref="Native"/> 区分开：这不是 Steamworks 自己的错，重试通常有效。
    /// </summary>
    public const string HostFailed = "hostFailed";
}
