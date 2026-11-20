namespace PdxLauncher.App.Services;

using System.Diagnostics;
using System.Globalization;
using System.Text;

/// <summary>
/// 启动阶段计时：以进程启动时间为零点，记录「进程启动 → 服务就绪 → 窗口创建 → 首屏渲染 → 首页可交互」
/// 五个里程碑的累计耗时，供「性能与日志」页面与 <c>docs/MEMORY-BASELINE.md</c> 的 2.5 秒目标对照。
/// <para>
/// 埋点都发生在诊断页出现之前，所以这里只记稳定的键名（<see cref="AppStart"/> 等），
/// 中文文案由页面层在显示时按当前语言映射；这样切换语言不需要重跑启动。
/// 计时失败只意味着少一条读数，任何情况都不允许抛异常打断启动。
/// </para>
/// <para>
/// 除五个用户可见里程碑外，还有一组「内部里程碑」（主页首帧拆解、整窗背景解码）。
/// 它们不参与「性能与日志」页面的展示（<see cref="Snapshot"/> 会过滤掉），只通过
/// <see cref="DescribeAll"/> 打一行日志，用于首帧性能的前后对比与回归。
/// </para>
/// </summary>
public static class StartupTimeline
{
    /// <summary>进程入口（Program.Main 第一行）。</summary>
    public const string AppStart = "app-start";

    /// <summary>依赖注入完成，日志/数据目录/数据库/网络服务都已可用。</summary>
    public const string ServicesReady = "services-ready";

    /// <summary>主窗口构造并激活。</summary>
    public const string WindowCreated = "window-created";

    /// <summary>主窗口 Loaded，导航框架就绪。</summary>
    public const string ShellReady = "shell-ready";

    /// <summary>首页已导航并完成首次数据加载。</summary>
    public const string HomeLoaded = "home-loaded";

    /// <summary>（内部）主页 XAML 树构造完成，还没进可视化树。</summary>
    public const string HomePageCreated = "home-page-created";

    /// <summary>（内部）主页 Loaded 事件进入。</summary>
    public const string HomePageLoaded = "home-page-loaded";

    /// <summary>（内部）主页数据（继续存档 + 播放集）读取完成。</summary>
    public const string HomePageDataReady = "home-page-data-ready";

    /// <summary>（内部）主页主题素材（按钮外观 / 字标）应用完成。</summary>
    public const string HomePageThemeReady = "home-page-theme-ready";

    /// <summary>（内部）主题按钮外观（底图 / 配色 / 字体）算好，还没解字标。</summary>
    public const string HomePageThemeVisuals = "home-page-theme-visuals";

    /// <summary>（内部）主页首个渲染帧被提交给合成器。</summary>
    public const string FirstFrame = "first-frame";

    /// <summary>（内部）整窗背景开始解码。</summary>
    public const string BackdropDecodeStart = "backdrop-decode-start";

    /// <summary>（内部）按上一次会话的快照提前开始解码整窗背景（还没确认游戏）。</summary>
    public const string BackdropPreloadStart = "backdrop-preload-start";

    /// <summary>（内部）提前解码完成。</summary>
    public const string BackdropPreloadReady = "backdrop-preload-ready";
    /// <summary>（内部）整窗背景解码完成（还没交给画笔）。</summary>
    public const string BackdropDecoded = "backdrop-decoded";

    /// <summary>（内部）背景已经赋给画笔（<c>ImageBrush.ImageSource</c>），还没应用显示方式。</summary>
    public const string BackdropBrushAssigned = "backdrop-brush-assigned";

    /// <summary>（内部）背景显示方式（Stretch / 对齐 / 遮罩）应用完成。</summary>
    public const string BackdropModeApplied = "backdrop-mode-applied";

    /// <summary>（内部）游戏发现完成（只记第一次）。</summary>
    public const string DiscoveryReady = "discovery-ready";

    /// <summary>（内部）整窗背景已交给画笔并完成显示方式应用。</summary>
    public const string BackdropPainted = "backdrop-painted";

    private const int MaxMarks = 32;

    private static readonly object Gate = new();

    private static readonly List<StartupMark> Marks = new(MaxMarks);

    private static readonly Lazy<DateTimeOffset> Start = new(CaptureStart, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>当前已有里程碑的快照（拷贝，可安全枚举）。只含用户可见的五个里程碑。</summary>
    public static IReadOnlyList<StartupMark> Snapshot()
    {
        lock (Gate)
        {
            return Marks.Where(static mark => !mark.Internal).ToArray();
        }
    }

    /// <summary>全部里程碑（含内部埋点）的快照，供首帧日志使用。</summary>
    public static IReadOnlyList<StartupMark> SnapshotAll()
    {
        lock (Gate)
        {
            return Marks.ToArray();
        }
    }

    /// <summary>
    /// 记一个里程碑。同名只记第一次；超过 32 条忽略；任何异常都被吞掉。
    /// 累计耗时保证单调不减（系统时间回拨时沿用上一条），页面不会出现负的阶段差。
    /// </summary>
    public static void Mark(string milestone) => MarkCore(milestone, internalOnly: false);

    /// <summary>
    /// 记一个内部里程碑：只进 <see cref="SnapshotAll"/>，不出现在「性能与日志」页面。
    /// 与 <see cref="Mark"/> 共用一个列表，因此同样受 32 条上限与「同名只记一次」约束。
    /// </summary>
    public static void MarkInternal(string milestone) => MarkCore(milestone, internalOnly: true);

    /// <summary>
    /// 把全部里程碑格式化成一行 <c>key=ms | key=ms</c>（不含毫秒小数，便于对比中位数）。
    /// 空列表返回空串；任何异常都返回已拼好的部分。
    /// </summary>
    public static string DescribeAll()
    {
        try
        {
            var marks = SnapshotAll();
            var builder = new StringBuilder(marks.Count * 24);
            foreach (var mark in marks)
            {
                if (builder.Length > 0)
                {
                    builder.Append(" | ");
                }

                builder
                    .Append(mark.Milestone)
                    .Append('=')
                    .Append(Math.Round(mark.ElapsedMilliseconds).ToString("0", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static void MarkCore(string milestone, bool internalOnly)
    {
        if (string.IsNullOrWhiteSpace(milestone))
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                if (Marks.Count >= MaxMarks)
                {
                    return;
                }

                foreach (var existing in Marks)
                {
                    if (string.Equals(existing.Milestone, milestone, StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                var now = DateTimeOffset.Now;
                double elapsed = (now - Start.Value).TotalMilliseconds;
                if (!double.IsFinite(elapsed) || elapsed < 0)
                {
                    elapsed = 0;
                }

                if (Marks.Count > 0 && elapsed < Marks[Marks.Count - 1].ElapsedMilliseconds)
                {
                    elapsed = Marks[Marks.Count - 1].ElapsedMilliseconds;
                }

                Marks.Add(new StartupMark(milestone, elapsed, now, internalOnly));
            }
        }
        catch (Exception)
        {
            // 计时埋点绝不能影响启动：拿不到时间只是少一条读数。
        }
    }

    /// <summary>清空全部里程碑，仅供自检使用。</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Marks.Clear();
        }
    }

    private static DateTimeOffset CaptureStart()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return new DateTimeOffset(process.StartTime);
        }
        catch (Exception)
        {
            // 取不到进程启动时间（权限不足等）时退化为首次计时的当前时间。
            return DateTimeOffset.Now;
        }
    }
}

/// <summary>一个启动里程碑：键名、相对进程启动的累计毫秒、记录时的本地时间。</summary>
/// <param name="Milestone">稳定的键名（<see cref="StartupTimeline"/> 里的常量）。</param>
/// <param name="ElapsedMilliseconds">相对进程启动的累计毫秒。</param>
/// <param name="At">记录时的本地时间。</param>
/// <param name="Internal">true 表示这是只进日志的内部埋点，不在诊断页展示。</param>
public sealed record StartupMark(string Milestone, double ElapsedMilliseconds, DateTimeOffset At, bool Internal = false);
