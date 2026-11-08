namespace PdxLauncher.Core.Dlc;

using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Steam;

/// <summary>
/// 判定每条 DLC 的<b>内容</b>来源（见 <see cref="DlcContentState"/>）。纯计算，不做 IO：
/// 「磁盘上有没有描述文件」由 <see cref="DlcInfo.FilePathRelativeToDlcPath"/> 是否指向
/// <c>dlc_metadata/</c> 合成条目表达，「Steam 认不认」由 <see cref="AppManifest.InstalledDlcAppIds"/> 表达。
/// <para>
/// 判定优先级（顺序有意义，别调换）：
/// <list type="number">
/// <item><c>steam_id</c> 命中 <c>InstalledDepots</c> 的 <c>dlcappid</c> → <see cref="DlcContentState.SteamInstalled"/>。
/// 清单是 Steam 自己写的，这是唯一权威依据。</item>
/// <item>否则，有磁盘描述文件（路径不以 <c>dlc_metadata/</c> 开头）→ <see cref="DlcContentState.FilesPresent"/>。</item>
/// <item>否则，只有 metadata 合成条目 → <see cref="DlcContentState.Missing"/>。</item>
/// <item><c>steam_id</c> 缺失 → <see cref="DlcContentState.Unknown"/>（连 id 都没有，谈不上判定）。</item>
/// </list>
/// </para>
/// <para>
/// 刻意<b>不</b>统计目录字节数：V3 全部 DLC 约 6.5 GB，递归统计会让切页卡顿，
/// 而「来源」这个问题用字节数也判断不出来（人工拷贝的目录同样可以字节相等）。
/// </para>
/// </summary>
public static class DlcContentInspector
{
    /// <summary>
    /// <c>DlcCatalog</c> 给「只存在于 <c>game\dlc_metadata\*.txt</c>」的 DLC 合成的路径前缀。
    /// 以它开头 = 磁盘上没有这个 DLC 的内容目录。
    /// </summary>
    public const string MetadataPathPrefix = "dlc_metadata/";

    /// <summary>
    /// 计算每条 DLC 的内容状态，key 是 <see cref="DlcInfo.FilePathRelativeToDlcPath"/>
    /// （与 DLC 页回填拥有的口径一致）。
    /// </summary>
    /// <param name="dlcs">扫描出来的 DLC 列表。</param>
    /// <param name="manifest">游戏的 Steam 清单；读不到时传 null（此时不会判定出 <see cref="DlcContentState.SteamInstalled"/>）。</param>
    public static IReadOnlyDictionary<string, DlcContentState> Inspect(
        IReadOnlyList<DlcInfo>? dlcs,
        AppManifest? manifest)
    {
        var result = new Dictionary<string, DlcContentState>(StringComparer.Ordinal);
        if (dlcs is null || dlcs.Count == 0)
        {
            return result;
        }

        var steamInstalled = manifest is null ? null : manifest.InstalledDlcAppIds;
        foreach (var dlc in dlcs)
        {
            var key = dlc.FilePathRelativeToDlcPath;
            if (string.IsNullOrEmpty(key) || result.ContainsKey(key))
            {
                continue;
            }

            result[key] = Classify(dlc, steamInstalled);
        }

        return result;
    }

    /// <summary>判定单条 DLC（自检与单元断言直接调它）。</summary>
    /// <param name="dlc">DLC 元数据。</param>
    /// <param name="steamInstalledDlcAppIds"><c>InstalledDepots</c> 里登记为 DLC 的 appid；
    /// 清单读不到时传 null，此时不可能返回 <see cref="DlcContentState.SteamInstalled"/>。</param>
    public static DlcContentState Classify(DlcInfo dlc, IReadOnlySet<int>? steamInstalledDlcAppIds)
    {
        ArgumentNullException.ThrowIfNull(dlc);
        return Classify(dlc.SteamId, dlc.FilePathRelativeToDlcPath, steamInstalledDlcAppIds);
    }

    /// <summary>
    /// 判定单条 DLC 的内容状态（只要两个字段，界面层不用为了判定伪造一个 <see cref="DlcInfo"/>）。
    /// </summary>
    /// <param name="steamId">DLC 的 Steam AppID。</param>
    /// <param name="filePathRelativeToDlcPath">相对 <c>dlcPath</c> 的路径；<c>dlc_metadata/</c> 开头表示只有元数据。</param>
    /// <param name="steamInstalledDlcAppIds"><c>InstalledDepots</c> 里登记为 DLC 的 appid；null 表示清单不可用。</param>
    public static DlcContentState Classify(
        string? steamId,
        string? filePathRelativeToDlcPath,
        IReadOnlySet<int>? steamInstalledDlcAppIds)
    {
        var appId = ParseAppId(steamId);
        if (appId is null)
        {
            return DlcContentState.Unknown;
        }

        if (steamInstalledDlcAppIds is not null && steamInstalledDlcAppIds.Contains(appId.Value))
        {
            return DlcContentState.SteamInstalled;
        }

        var path = filePathRelativeToDlcPath ?? string.Empty;

        // CS2 这类由 Steam 授权清单构建的行（`steam:<appid>`）在磁盘上没有描述文件可依据：
        // 清单没登记它时只能说「不知道」，不能因为「磁盘上找不到 .dlc」就断言内容缺失。
        if (path.StartsWith(SteamDlcListBuilder.IdPrefix, StringComparison.Ordinal))
        {
            return DlcContentState.Unknown;
        }

        return path.Length > 0 && !path.StartsWith(MetadataPathPrefix, StringComparison.Ordinal)
            ? DlcContentState.FilesPresent
            : DlcContentState.Missing;
    }

    /// <summary>
    /// 磁盘上是否存在这条 DLC 的描述文件（<c>.dlc</c> / <c>.dlc.json</c>）。
    /// 只有 <c>dlc_metadata</c> 里的元数据条目没有描述文件。
    /// </summary>
    public static bool HasDescriptorFile(DlcInfo dlc)
    {
        ArgumentNullException.ThrowIfNull(dlc);
        var path = dlc.FilePathRelativeToDlcPath;
        return !string.IsNullOrEmpty(path)
            && !path.StartsWith(MetadataPathPrefix, StringComparison.Ordinal);
    }

    /// <summary>把 <see cref="DlcInfo.SteamId"/> 解析成正的 appid；空或非法返回 null。</summary>
    public static int? ParseAppId(string? steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return null;
        }

        return int.TryParse(
            steamId.Trim(),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var appId) && appId > 0
            ? appId
            : null;
    }
}
