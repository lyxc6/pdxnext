namespace PdxLauncher.Core.Saves;

/// <summary>
/// 一个游戏的一种存档落点：根目录、递归深度、扩展名白名单、容器形态。
/// <para><see cref="MaxDepth"/> 的 0 表示只看根目录，1 表示允许一层子目录（CS2 的 SteamID），
/// 2 表示允许两层（Stellaris 的 <c>save games\&lt;子目录&gt;</c>）。</para>
/// </summary>
/// <param name="GameId">归一后的游戏 id。</param>
/// <param name="Directory">存档根目录（已存在的绝对路径）。</param>
/// <param name="MaxDepth">相对根目录允许向下递归的层数。</param>
/// <param name="Extensions">扩展名白名单（含点，大小写不敏感）。</param>
/// <param name="Container">该落点的容器形态。</param>
public sealed record SaveLocation(
    string GameId,
    string Directory,
    int MaxDepth,
    IReadOnlyList<string> Extensions,
    SaveContainer Container);
