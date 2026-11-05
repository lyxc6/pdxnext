namespace PdxLauncher.Core.Saves;

/// <summary>
/// 存档页的两段式数据源：第一段只枚举文件，第二段才解析单个文件。
/// <para>
/// 与 <see cref="ISaveGameLibrary"/> 分开，是为了让「快速枚举」不依赖「全量扫描」的实现，
/// 视图模型既能先拿到列表，又不必知道容器格式。实现必须保证：
/// <list type="bullet">
///   <item><see cref="ScanFilesAsync"/> 不打开文件内容（只 Enumerate / 取属性），可取消；</item>
///   <item><see cref="ParseFile"/> 复用现有 <see cref="SavContainerReader"/>，不复制解析逻辑；</item>
///   <item>单文件解析失败只降级该条结果，不抛出。</item>
/// </list>
/// </para>
/// </summary>
public interface ISaveGameFileIndex
{
    /// <summary>枚举给定游戏的存档文件（不解析内容），结果稳定排序。</summary>
    /// <param name="gameIds">游戏 id 集合；重复、空白与未知 id 会被忽略。</param>
    /// <param name="cancellationToken">取消标记；枚举过程中会响应。</param>
    Task<SaveFileScanResult> ScanFilesAsync(IEnumerable<string> gameIds, CancellationToken cancellationToken);

    /// <summary>解析单个存档的细节；任何解析失败都返回 Degraded 而不抛异常。</summary>
    /// <param name="entry">第一段产出的粗略行。</param>
    SaveGameMeta ParseFile(SaveFileEntry entry);

    /// <summary>返回某个游戏登记过的存档落点及是否存在（目录探测，不枚举文件）。</summary>
    /// <param name="gameId">游戏 id（大小写不敏感，别名会归一）。</param>
    IReadOnlyList<SaveLocationInfo> GetLocations(string gameId);
}
