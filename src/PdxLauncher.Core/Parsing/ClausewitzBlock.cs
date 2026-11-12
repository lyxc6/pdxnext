namespace PdxLauncher.Core.Parsing;

/// <summary>
/// Clausewitz 文件里的一个顶层块（<c>dlc019 = { name = "..." steam_id = "..." }</c>）。
/// <para>
/// <see cref="Key"/> 是块名（原样保留大小写，例如 <c>dlc019</c>，对应 dlc 文件名）；
/// <see cref="Values"/> 是块内标量键值对（键小写，值去掉包裹引号）。
/// </para>
/// <para>用于 <c>game/dlc_metadata/*.txt</c> 这类「一条 DLC 一个块」的文件，由
/// <see cref="ClausewitzParser.ParseBlocks"/> 返回。</para>
/// </summary>
/// <param name="Key">块名（大小写敏感）。</param>
/// <param name="Values">块内标量键值对（键为小写）。</param>
public sealed record ClausewitzBlock(string Key, IReadOnlyDictionary<string, string> Values);
