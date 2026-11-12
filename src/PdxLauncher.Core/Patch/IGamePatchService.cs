namespace PdxLauncher.Core.Patch;

using PdxLauncher.Core.Contracts;

/// <summary>
/// DLC 解锁补丁（loader 替换）的检查 / 应用 / 还原。
/// <para>
/// 只动 <c>steam_api*.dll</c> 与 <c>cream_api.ini</c> 两个文件，不碰游戏内容、不联网。
/// 应用前把原文件备份成同目录的 <c>*_o.dll</c>，还原时把它移回原位——这样即使整合包
/// 里带的 <c>steam_api64_o.dll</c> 与目标游戏版本不匹配，也不会把游戏弄坏。
/// </para>
/// </summary>
public interface IGamePatchService
{
    /// <summary>外部覆盖素材目录（界面「打开素材目录」用）。</summary>
    string AssetsDirectory { get; }

    /// <summary>当前是否在使用外部覆盖素材。</summary>
    bool UsesExternalAssets { get; }

    /// <summary>只读：检查当前安装的补丁状态。</summary>
    GamePatchReport Inspect(GameInstallation game);

    /// <summary>
    /// 打补丁；失败时 <see cref="GamePatchReport.Detail"/> 说明原因，不抛异常。
    /// <para>
    /// 目标 api 文件不在已知原版登记表里时，第一次调用返回
    /// <see cref="GamePatchStatus.UnverifiedVersion"/> 且不写盘；界面提示后由用户确认，
    /// 再以 <paramref name="allowUnverifiedVersion"/> = true 重试。
    /// </para>
    /// </summary>
    GamePatchReport Apply(GameInstallation game, bool allowUnverifiedVersion = false);

    /// <summary>移除补丁、恢复原文件；失败时 <see cref="GamePatchReport.Detail"/> 说明原因。</summary>
    GamePatchReport Revert(GameInstallation game);

    /// <summary>
    /// 本游戏的补丁 DLC 清单（<c>dlc-catalog.json</c> 里按游戏分节的那一段）。
    /// <para>
    /// 返回 null 表示**读不到清单**（素材缺失或解析失败），此时调用方不能对任何一条 DLC 下结论；
    /// 返回空列表表示清单读到了，但里面没有本游戏。注意这是按游戏的短表（Vic3 23 条），
    /// 不是 loader ini 那张 11 款游戏共用的全表。
    /// </para>
    /// </summary>
    /// <param name="gameId">游戏 id（大小写不敏感，内部会归一）；未知游戏返回空列表。</param>
    IReadOnlyList<DlcEntry>? DlcCatalogFor(string? gameId);
}
