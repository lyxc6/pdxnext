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

    /// <summary>
    /// 未验证版本：目标 api 文件不在已知原版登记表里（未知版本 / 未知哈希）。
    /// 这是「打补丁前被拦下等确认」的中间状态：此时没有写盘，UI 提示后允许用户确认再打
    /// （<c>Apply(game, allowUnverifiedVersion: true)</c>）。
    /// </summary>
    UnverifiedVersion,
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
    /// <summary>
    /// 当前「原版」文件（loader 在岗时是 <c>_o</c>，否则是当前 api 文件）在已知版本登记表里的匹配情况。
    /// </summary>
    public PatchVersionState VersionState { get; init; } = PatchVersionState.NotChecked;

    /// <summary>命中登记表时的游戏版本号（例如 <c>1.15.0 (Ivan-Chai)</c>）；未命中为 null。</summary>
    public string? MatchedVersion { get; init; }

    /// <summary>命中登记表时的 Steam buildid；未命中为 null。</summary>
    public string? MatchedBuildId { get; init; }

    /// <summary>
    /// 打补丁时发现已存在 <c>_o</c> 备份、且它既不是已知原版也不等于当前文件：
    /// 为免毁掉唯一的原版，没有覆盖它，把要替换的当前文件另存归档。true 表示备份来源存疑。
    /// </summary>
    public bool BackupSuspect { get; init; }

    /// <summary>备份来源存疑时，被替换掉的当前文件另存的归档路径（<c>steam_api64_o.bak-时间戳</c>）。</summary>
    public string? BackupArchivePath { get; init; }

    /// <summary>是否已经有补丁痕迹（不论是不是本启动器打的）。</summary>
    public bool IsPatched =>
        Status is GamePatchStatus.Patched or GamePatchStatus.PatchedOther or GamePatchStatus.SteamOverwrote;

    /// <summary>
    /// 当前 <c>steam_api64.dll</c> 是否就是 loader。它决定了授权探测拿到的 DLC 列表
    /// 来自 loader 的 <c>cream_api.ini</c> 还是 Steam 目录，界面必须据此标注来源。
    /// <para>
    /// <see cref="GamePatchStatus.SteamOverwrote"/> 不算：那时磁盘上的 api 已被 Steam 覆盖回原版，
    /// 而 <see cref="IsPatched"/> 仍为 true，两者语义不同。
    /// </para>
    /// </summary>
    public bool IsLoaderActive =>
        Status is GamePatchStatus.Patched or GamePatchStatus.PatchedOther;

    /// <summary>
    /// CreamAPI 约定的原版备份全路径（<c>steam_api64_o.dll</c>）；无法从报告推导时为 null。
    /// 授权服务用它做第二份「账号真实拥有」探测。
    /// </summary>
    public string? BackupApiFilePath =>
        ApiDirectory is null || string.IsNullOrWhiteSpace(ApiFileName)
            ? null
            : Path.Combine(
                ApiDirectory,
                Path.GetFileNameWithoutExtension(ApiFileName) + GamePatchService.BackupSuffix + Path.GetExtension(ApiFileName));
}
