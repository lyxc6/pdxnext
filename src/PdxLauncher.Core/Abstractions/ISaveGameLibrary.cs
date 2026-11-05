using PdxLauncher.Core.Saves;

namespace PdxLauncher.Core.Abstractions;

/// <summary>
/// 存档库：一次扫描把磁盘上的存档收成不可变快照，之后按游戏查询。
/// <para>
/// 实现只读磁盘，绝不写 <c>Documents\Paradox Interactive</c> 或游戏安装目录。
/// 扫描中的单文件解析失败由 <see cref="SavContainerReader"/> 降级，不允许让整次扫描失败；
/// 取消是正常路径，<see cref="OperationCanceledException"/> 直接向上抛。
/// </para>
/// </summary>
public interface ISaveGameLibrary
{
    /// <summary>
    /// 扫描指定游戏的存档目录。
    /// </summary>
    /// <param name="gameIds">游戏 id（大小写不敏感；<c>cities_v2</c> 会折成 <c>cities_skylines</c>）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>按游戏 id、文件名、完整路径稳定排序的不可变快照。</returns>
    Task<IReadOnlyList<SaveGameMeta>> ScanAsync(IEnumerable<string> gameIds, CancellationToken cancellationToken);

    /// <summary>最近一次成功扫描得到的全部存档；尚未扫描时为空集合。</summary>
    IReadOnlyList<SaveGameMeta> GetAll();

    /// <summary>按游戏 id 过滤最近一次扫描结果；不存在时返回空集合。</summary>
    IReadOnlyList<SaveGameMeta> GetByGame(string gameId);
}
