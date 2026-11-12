namespace PdxLauncher.App.Services;

using System.Threading;

/// <summary>
/// 取数请求的「代次」计数器：每次开始取数就推进一代，异步结果回来时只有最新一代可以落地。
/// <para>
/// 为什么需要它：页面用 <see cref="ReloadCoordinator"/> 取消了旧请求，但取消是协作式的 ——
/// 旧请求的 <c>finally</c> 仍会在新请求开始之后执行。如果它在 <c>finally</c> 里直接写
/// <c>IsBusy = false</c>，新请求转圈就会被提前关掉。把「我是不是最新一代」交给这个类判断，
/// 旧请求就只能安静退出。
/// </para>
/// <para>纯 BCL、无 UI 依赖，自检工程可以直接链接进来测。</para>
/// </summary>
public sealed class AchievementRequestTracker
{
    private int _generation;

    /// <summary>开始一次新请求，返回它的代次。</summary>
    public int Begin() => Interlocked.Increment(ref _generation);

    /// <summary>这个代次是否仍是最新的（不是最新的一律丢弃）。</summary>
    public bool IsCurrent(int generation) => Volatile.Read(ref _generation) == generation;
}
