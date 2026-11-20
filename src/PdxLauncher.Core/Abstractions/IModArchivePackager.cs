namespace PdxLauncher.Core.Abstractions;

/// <summary>
/// 把 mod 目录打包成 <c>.zip</c> 归档。
/// <para>
/// ContentLoadV1（<see cref="PdxLauncher.Core.LoadFiles.ModLoadRules.WarnNoArchive"/>）的加载文件
/// 只写 archivePath，目录型 mod 必须先把整棵目录打成 zip 才能被游戏加载。
/// 现有代码里只有只读的 <c>ZipFile.OpenRead</c>，没有写侧能力，故单独抽象出来。
/// </para>
/// </summary>
public interface IModArchivePackager
{
    /// <summary>
    /// 把 <paramref name="sourceDirectory"/> 整棵目录（含以 <c>.</c> 开头的隐藏目录/文件）打包到
    /// <paramref name="archivePath"/>。zip 内条目使用相对 <paramref name="sourceDirectory"/> 的 POSIX 路径，
    /// 不额外包一层目录名；写入采用 tmp + 原子改名，失败不留半截归档。
    /// </summary>
    Task CreateFromDirectory(string sourceDirectory, string archivePath, CancellationToken cancellationToken);
}
