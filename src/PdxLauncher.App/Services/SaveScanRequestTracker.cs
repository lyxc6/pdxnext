namespace PdxLauncher.App.Services;

/// <summary>
/// 存档扫描的代次守卫：每次开始扫描（或切游戏）递增一代，
/// 只有最新一代的结果允许写进 ViewModel。
/// <para>
/// 旧扫描被取消后仍可能执行到 <c>finally</c>；没有这道守卫时，
/// 旧请求会把新请求的 <c>IsBusy</c> 关掉，页面就会停在「扫描中」或显示上一款游戏的存档。
/// 实现只用 BCL，可被 checks 工程链接后直接测试。
/// </para>
/// </summary>
public sealed class SaveScanRequestTracker
{
    private int _generation;

    /// <summary>开始一次新请求，返回它的代次。</summary>
    public int Begin() => Interlocked.Increment(ref _generation);

    /// <summary>该代次是否仍是最新一次请求。</summary>
    public bool IsCurrent(int generation) => Volatile.Read(ref _generation) == generation;

    /// <summary>
    /// 只有最新一代请求能提交状态。判定与写入放在同一个方法里，
    /// 调用点就没有「取反写错」的余地：旧代次返回 <c>false</c>，且<b>不执行</b> <paramref name="commit"/>。
    /// </summary>
    /// <remarks>
    /// 这条路径由 <c>SavesPageChecks</c> 用「旧代次丢弃 / 新代次应用」的行为断言钉住：
    /// 把判定极性写成 <c>if (IsCurrent(...)) return;</c> 会立刻变红。
    /// </remarks>
    /// <returns>是否由本次请求提交了状态。</returns>
    public bool TryCommit(int generation, Action commit)
    {
        ArgumentNullException.ThrowIfNull(commit);

        if (!IsCurrent(generation))
        {
            return false;
        }

        commit();
        return true;
    }
}
