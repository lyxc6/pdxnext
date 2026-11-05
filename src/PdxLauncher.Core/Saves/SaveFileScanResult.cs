namespace PdxLauncher.Core.Saves;

/// <summary>
/// 第一段（快速枚举）的结果：粗略行、落点存在性、是否因候选上限被截断。
/// <para>
/// 不包含任何解析后的元数据；调用方拿到后先把 <see cref="Files"/> 铺到列表里，
/// 再按批调用 <see cref="ISaveGameFileIndex.ParseFile"/> 补细节。
/// </para>
/// </summary>
/// <param name="Files">按 GameId → FileName → FilePath 稳定排序的粗略行。</param>
/// <param name="Locations">本次探测到的落点（目录不存在时 Exists=false）。</param>
/// <param name="Truncated">是否达到单次扫描候选上限。</param>
public sealed record SaveFileScanResult(
    IReadOnlyList<SaveFileEntry> Files,
    IReadOnlyList<SaveLocationInfo> Locations,
    bool Truncated);
