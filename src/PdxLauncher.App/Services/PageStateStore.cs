namespace PdxLauncher.App.Services;

/// <summary>
/// 页面级状态暂存（仅本次运行有效）：Mod 库页与 Mod 创建页改成「离开即销毁」后，
/// 靠这里在返回时把搜索词、排序、面板、滚动位置和表单草稿放回去。
/// <para>
/// 只在 UI 线程读写，不加锁；进程退出即丢，符合「不跨重启」的约定。
/// </para>
/// </summary>
public static class PageStateStore
{
    /// <summary>Mod 库页最近一次离开时的状态。</summary>
    public static ModLibraryState? ModLibrary { get; set; }

    /// <summary>Mod 创建页最近一次离开时的状态。</summary>
    public static ModStudioState? ModStudio { get; set; }
}
