namespace PdxLauncher.App.Services;

using System.Diagnostics;
using System.Globalization;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// Debug 构建的内存埋点：抓一份「托管 / 工作集 / 私有 + GC 次数 + 缓存条目数」快照，
/// 拼成一行中文写进日志，供 <c>docs/MEMORY-BASELINE.md</c> 的验收对照。
/// <para>
/// 本类自身不带编译条件，由调用点用 <c>#if DEBUG</c> 控制；埋点失败只 Warn，绝不影响主流程。
/// 这个文件会被 <c>tests/PdxLauncher.Checks</c> 原样链接，所以不得引用任何 WinUI 或 App 层类型：
/// 缓存条目数走可选委托传入，由 App 调用点负责读 <c>AppServices</c>。
/// </para>
/// </summary>
public static class MemoryDiagnostics
{
    private const double BytesPerMegabyte = 1024d * 1024d;

    /// <summary>一次内存采样；<c>Workshop*</c> / <c>ModThumbnails</c> 在未提供计数委托时记 0。</summary>
    public sealed record MemorySnapshot(
        long ManagedBytes,
        long WorkingSetBytes,
        long PrivateBytes,
        int Gen0,
        int Gen1,
        int Gen2,
        int WorkshopItems,
        int WorkshopPages,
        int ModThumbnails);

    /// <summary>
    /// 抓取当前进程的内存快照。进程信息取不到、计数委托抛异常时对应项退化为 0，
    /// 保证埋点本身不向调用方抛任何异常。
    /// </summary>
    public static MemorySnapshot Capture(Func<(int Items, int Pages, int Thumbnails)>? counters = null)
    {
        long managedBytes = 0;
        long workingSetBytes = 0;
        long privateBytes = 0;

        try
        {
            managedBytes = GC.GetTotalMemory(forceFullCollection: false);
            using var process = Process.GetCurrentProcess();
            workingSetBytes = process.WorkingSet64;
            privateBytes = process.PrivateMemorySize64;
        }
        catch (Exception)
        {
            // 埋点不允许把异常带进主流程：拿不到的项按 0 记，日志里其余数字仍然可读。
        }

        var (items, pages, thumbnails) = (0, 0, 0);
        if (counters is not null)
        {
            try
            {
                (items, pages, thumbnails) = counters();
            }
            catch (Exception)
            {
                // AppServices 未初始化或缓存已释放时委托会抛，同样按 0 处理。
            }
        }

        return new MemorySnapshot(
            managedBytes,
            workingSetBytes,
            privateBytes,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            items,
            pages,
            thumbnails);
    }

    /// <summary>把快照拼成单行中文；MB 按 1 MB = 1024² B 换算并保留一位小数（InvariantCulture）。</summary>
    public static string Describe(MemorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return "内存：托管 " + FormatMegabytes(snapshot.ManagedBytes)
            + " / 工作集 " + FormatMegabytes(snapshot.WorkingSetBytes)
            + " / 私有 " + FormatMegabytes(snapshot.PrivateBytes)
            + "；GC 0/1/2=" + snapshot.Gen0.ToString(CultureInfo.InvariantCulture)
            + "/" + snapshot.Gen1.ToString(CultureInfo.InvariantCulture)
            + "/" + snapshot.Gen2.ToString(CultureInfo.InvariantCulture)
            + "；工坊条目 " + snapshot.WorkshopItems.ToString(CultureInfo.InvariantCulture)
            + " / 分页 " + snapshot.WorkshopPages.ToString(CultureInfo.InvariantCulture)
            + " / mod 缩略图 " + snapshot.ModThumbnails.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 抓快照并写一行 Info（<c>[tag] 内存：…</c>）。埋点自身失败时退回 Warn，不向调用方抛。
    /// 是否只在 Debug 构建里调用由调用点决定。
    /// </summary>
    public static void Log(IAppLogger logger, string tag, Func<(int Items, int Pages, int Thumbnails)>? counters = null)
    {
        try
        {
            var snapshot = Capture(counters);
            logger.Info("[" + tag + "] " + Describe(snapshot));
        }
        catch (Exception ex)
        {
            // 埋点失败只报警，不打断启动 / 关闭流程；连 Warn 都失败时见 TryWarn。
            TryWarn(logger, "[" + tag + "] 内存埋点失败: " + ex.Message);
        }
    }

    private static void TryWarn(IAppLogger? logger, string message)
    {
        try
        {
            logger?.Warn(message);
        }
        catch (Exception)
        {
            // 日志落点自身失败时没有第二个落点可写，只能丢弃。
        }
    }

    private static string FormatMegabytes(long bytes)
    {
        return (bytes / BytesPerMegabyte).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    }
}
