namespace PdxLauncher.App.Services;

using System.Diagnostics;

/// <summary>
/// 启动阶段计时：以进程启动时间为零点，记录「进程启动 → 服务就绪 → 窗口创建 → 首屏渲染 → 首页可交互」
/// 五个里程碑的累计耗时，供「性能与日志」页面与 <c>docs/MEMORY-BASELINE.md</c> 的 2.5 秒目标对照。
/// <para>
/// 埋点都发生在诊断页出现之前，所以这里只记稳定的键名（<see cref="AppStart"/> 等），
/// 中文文案由页面层在显示时按当前语言映射；这样切换语言不需要重跑启动。
/// 计时失败只意味着少一条读数，任何情况都不允许抛异常打断启动。
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

    private const int MaxMarks = 32;

    private static readonly object Gate = new();

    private static readonly List<StartupMark> Marks = new(MaxMarks);

    private static readonly Lazy<DateTimeOffset> Start = new(CaptureStart, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>当前已有里程碑的快照（拷贝，可安全枚举）。</summary>
    public static IReadOnlyList<StartupMark> Snapshot()
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
    public static void Mark(string milestone)
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

                Marks.Add(new StartupMark(milestone, elapsed, now));
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
public sealed record StartupMark(string Milestone, double ElapsedMilliseconds, DateTimeOffset At);
