namespace PdxLauncher.Core.Saves;

/// <summary>
/// 一份存档的不可变元数据快照。
/// <para>
/// 所有字段都只来自「文件名 + 文件时间 + 容器头部的有限字节」，从不读完整的
/// <c>gamestate</c>（Victoria 3 / EU5 / Stellaris 的 gamestate 可达数十 MB）。
/// 解析失败时 <see cref="ParseState"/> 为 <see cref="SaveParseState.Degraded"/>，
/// <see cref="SaveName"/> 仍回退到文件名，UI 不会出现空行。
/// </para>
/// </summary>
/// <param name="FilePath">存档完整路径。</param>
/// <param name="FileName">含扩展名的文件名（中文原样保留）。</param>
/// <param name="GameId">归一后的游戏 id。</param>
/// <param name="Container">容器形态。</param>
/// <param name="SaveName">展示名；解析失败时为去掉扩展名的文件名。</param>
/// <param name="GameDate">游戏内日期原文（如 <c>1066.9.29</c>/<c>1850.06.18</c>）；读不到为 null。</param>
/// <param name="GameVersion">游戏版本原文（如 <c>1.13.11</c>）；读不到为 null。</param>
/// <param name="PlayerName">玩家名；读不到为 null。</param>
/// <param name="IsIronman">是否铁人档。</param>
/// <param name="PlayerCountry">玩家国家/城市名；读不到为 null。</param>
/// <param name="FileSize">文件字节数。</param>
/// <param name="LastWriteUtc">最后写入时间（UTC）。</param>
/// <param name="ParseState">解析完成度。</param>
/// <param name="DegradeReason">降级原因；正常时为 null。</param>
public sealed record SaveGameMeta(
    string FilePath,
    string FileName,
    string GameId,
    SaveContainer Container,
    string? SaveName,
    string? GameDate,
    string? GameVersion,
    string? PlayerName,
    bool IsIronman,
    string? PlayerCountry,
    long FileSize,
    DateTimeOffset LastWriteUtc,
    SaveParseState ParseState,
    string? DegradeReason);
