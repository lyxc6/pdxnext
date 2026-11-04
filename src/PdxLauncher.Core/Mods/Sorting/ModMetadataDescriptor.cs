namespace PdxLauncher.Core.Mods.Sorting;

/// <summary>
/// V3 / EU5 的 <c>.metadata/metadata.json</c> 里与排序相关的部分：自身 id 与它声明的依赖 id。
/// </summary>
/// <param name="Id">元数据 id；别的 mod 在 relationships 里用这个 id 声明依赖。缺省为 null。</param>
/// <param name="DependencyIds">依赖的 mod id，按声明顺序、已按忽略大小写去重。</param>
/// <remarks>
/// 这两个值都只用于「谁必须先加载」：V3 的 mod 没有 <c>descriptor.mod</c>，游戏库
/// <c>mods_dependencies</c> 又常年为空，元数据是本地 mod 唯一能表达依赖的地方。
/// </remarks>
public sealed record ModMetadataDescriptor(string? Id, IReadOnlyList<string> DependencyIds)
{
    /// <summary>空描述：没有 metadata.json 或内容不可解析时用它，调用方不必判空。</summary>
    public static ModMetadataDescriptor Empty { get; } = new(null, Array.Empty<string>());
}
