namespace PdxLauncher.Core.Steam;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 把 Steam 授权报告里的 DLC 清单翻译成 <see cref="DlcInfo"/>。
/// <para>CS2（<c>PlaysetConfigV1</c>）没有 <c>.dlc</c> / <c>.dlc.json</c> 描述文件，磁盘扫描必然是空的：
/// DLC 的名单与归属都只在 Steam 里（<c>GetDLCCount</c> + <c>BGetDLCDataByIndex</c>），
/// 因此这类游戏的 DLC 列表改由这里构建，而不是伪造磁盘条目。</para>
/// <para><see cref="DlcInfo.PopsId"/> 一律留空：CS2 的播放集里没有 DLC 条目，也没有 pops_id，
/// 于是 <see cref="Dlc.DlcCatalog.CanBeDisabledInContentLoad"/> 为 false —— 界面上的开关必须隐藏，
/// 避免伪造一个写不进任何文件的启用状态。唯一 key 用 <see cref="DlcInfo.FilePathRelativeToDlcPath"/>，
/// 保证 Steam 返回重名 DLC 时也不会撞车。</para>
/// </summary>
public static class SteamDlcListBuilder
{
    /// <summary>伪造的"相对路径"前缀：只为拿到一个稳定唯一的有效 id（见 <see cref="BuildDlcId"/>）。</summary>
    public const string IdPrefix = "steam:";

    /// <summary>由 Steam appid 生成有效 id（<c>steam:&lt;appid&gt;</c>）。</summary>
    /// <param name="appId">DLC 的 Steam appid。</param>
    /// <returns>用作 <see cref="DlcInfo.FilePathRelativeToDlcPath"/> 的唯一 id。</returns>
    public static string BuildDlcId(string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        return IdPrefix + appId.Trim();
    }

    /// <summary>
    /// 构建 DLC 列表：跳过名字或 appid 为空的条目、按 appid 去重（先到先得），并保持 Steam 返回的顺序。
    /// </summary>
    /// <param name="dlcs">Steam 报告的 DLC（<see cref="SteamOwnershipReport.Dlcs"/>）。</param>
    /// <returns>可直接交给 <c>DlcRowViewModel</c> 的不可变条目。</returns>
    public static IReadOnlyList<DlcInfo> FromOwnership(IEnumerable<SteamDlcOwnership>? dlcs)
    {
        if (dlcs is null)
        {
            return Array.Empty<DlcInfo>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<DlcInfo>();
        foreach (var dlc in dlcs)
        {
            if (dlc is null)
            {
                continue;
            }

            var appId = dlc.AppId?.Trim();
            var name = dlc.Name?.Trim();
            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (!seen.Add(appId))
            {
                continue;
            }

            list.Add(new DlcInfo(
                DisplayName: name,
                PopsId: string.Empty,
                SteamId: appId,
                FilePathRelativeToDlcPath: BuildDlcId(appId),
                ThumbnailPath: string.Empty,
                Description: string.Empty,
                RawBytes: Array.Empty<byte>()));
        }

        return list;
    }
}
