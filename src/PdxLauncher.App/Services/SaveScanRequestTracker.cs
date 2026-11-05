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
}
