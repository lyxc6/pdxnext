namespace PdxLauncher.Core.Patch;

/// <summary>补丁状态。</summary>
public enum GamePatchStatus
{
    /// <summary>缺少 Steam appId（独立安装）或素材缺失，无法打补丁。</summary>
    Unsupported,

    /// <summary>安装目录里没找到 steam_api.dll / steam_api64.dll。</summary>
    NotDetected,

    /// <summary>原版：api 文件是 Steam 原版，没有任何补丁痕迹。</summary>
    Vanilla,

    /// <summary>已由本启动器打上补丁（loader + <c>_o</c> 备份 + 带标记的 ini 齐全）。</summary>
    Patched,

    /// <summary>被别的工具打过补丁（loader 不匹配，或缺少 <c>_o</c> / ini 标记）。</summary>
    PatchedOther,

    /// <summary>Steam 更新把 api 文件覆盖回原版，但 <c>_o</c> 备份还在。</summary>
    SteamOverwrote,
}

/// <summary>一次状态检查 / 打补丁 / 还原动作的结果。</summary>
/// <param name="Status">当前状态（动作失败时仍是动作结束后的实际状态）。</param>
/// <param name="SteamAppId">目标游戏的 Steam appId；缺少时为 null。</param>
/// <param name="ApiDirectory">实际处理的目录（首选 x64 所在目录）。</param>
/// <param name="ApiFileName">实际处理的文件名（<c>steam_api64.dll</c> 优先）。</param>
/// <param name="Detail">失败或需要额外说明时的技术细节；正常状态为 null。</param>
/// <param name="CanApply">是否允许「一键打补丁」。</param>
/// <param name="CanRevert">是否允许「移除补丁」。</param>
/// <param name="PatchedFiles">当前处于补丁状态的文件绝对路径。</param>
public sealed record GamePatchReport(
    GamePatchStatus Status,
    int? SteamAppId,
    string? ApiDirectory,
    string? ApiFileName,
    string? Detail,
    bool CanApply,
    bool CanRevert,
    IReadOnlyList<string> PatchedFiles)
{
    /// <summary>是否已经有补丁痕迹（不论是不是本启动器打的）。</summary>
    public bool IsPatched =>
        Status is GamePatchStatus.Patched or GamePatchStatus.PatchedOther or GamePatchStatus.SteamOverwrote;
}
