namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Storage;

/// <summary>
/// 页面级状态暂存：Mod 库页、Mod 创建页与「添加模组」页改成「离开即销毁」后，
/// 靠这里在返回时把搜索词、排序、面板、滚动位置和表单草稿放回去。
/// <para>
/// 先在内存里存一份（本次运行内一定可用），再写进自有库的
/// <see cref="JsonDocumentKeys.UiPageState"/> 键，重启后也能恢复上次的位置。
/// 只在 UI 线程读写，不加锁；写失败只记 Warn，页面行为退化为「回到顶部」。
/// </para>
/// </summary>
public static class PageStateStore
{
    private static IJsonDocumentStore? _documents;
    private static IAppLogger? _logger;

    /// <summary>Mod 库页最近一次离开时的状态。</summary>
    public static ModLibraryState? ModLibrary { get; set; }

    /// <summary>Mod 创建页最近一次离开时的状态。</summary>
    public static ModStudioState? ModStudio { get; set; }

    /// <summary>「添加模组」页最近一次离开时的状态。</summary>
    public static AddModsState? AddMods { get; set; }

    /// <summary>
    /// 启动时接上文档库并把上次的页面状态读回内存；读失败/没有记录都按「首次进入」处理。
    /// 必须在页面创建之前调用。
    /// </summary>
    public static void Attach(IJsonDocumentStore documents, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(logger);

        _documents = documents;
        _logger = logger;

        try
        {
            if (!documents.TryRead(JsonDocumentKeys.UiPageState, out var json))
            {
                return;
            }

            var snapshot = PageStateSnapshot.Deserialize(json);
            if (snapshot is null)
            {
                return;
            }

            ModLibrary = snapshot.ModLibrary;
            ModStudio = snapshot.ModStudio;
            AddMods = snapshot.AddMods;
        }
        catch (Exception exception)
        {
            logger.Warn("页面状态读取失败：" + exception.Message);
        }
    }

    /// <summary>
    /// 把当前内存里的三份状态写回文档库（离开页面、关窗口时调用）。
    /// 没 Attach 或写失败都只记 Warn，不影响页面本身。
    /// </summary>
    public static void Persist()
    {
        var documents = _documents;
        if (documents is null)
        {
            return;
        }

        try
        {
            var json = PageStateSnapshot.Serialize(new PageStateSnapshot(ModLibrary, ModStudio, AddMods));
            if (!documents.Write(JsonDocumentKeys.UiPageState, json))
            {
                _logger?.Warn("页面状态保存失败。");
            }
        }
        catch (Exception exception)
        {
            _logger?.Warn("页面状态保存失败：" + exception.Message);
        }
    }
}
