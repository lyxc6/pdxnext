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

    /// <summary>打补丁；失败时 <see cref="GamePatchReport.Detail"/> 说明原因，不抛异常。</summary>
    GamePatchReport Apply(GameInstallation game);

    /// <summary>移除补丁、恢复原文件；失败时 <see cref="GamePatchReport.Detail"/> 说明原因。</summary>
    GamePatchReport Revert(GameInstallation game);
}
