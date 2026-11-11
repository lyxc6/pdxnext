namespace PdxLauncher.App.Services;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 页面导航。NavigationView 的 Tag 是字符串，这里把 Tag 映射成页面类型再交给 Frame。
/// 页面用无参构造，依赖从 <see cref="AppServices"/> 取；需要入参的页面（如工坊详情子页）
/// 通过 <see cref="Navigate(string?, object?)"/> 的 parameter 交给 <c>OnNavigatedTo</c>。
/// 类型解析失败只记 Warn，不崩。
/// </summary>
public sealed class NavigationService
{
    /// <summary>游戏库页的导航 Tag。</summary>
    public const string LibraryTag = "library";

    /// <summary>当前游戏主页的导航 Tag。</summary>
    public const string HomeTag = "home";

    /// <summary>播放集页的导航 Tag。</summary>
    public const string PlaySetsTag = "playsets";

    /// <summary>Mod 库页的导航 Tag。</summary>
    public const string ModLibraryTag = "modlibrary";

    /// <summary>「Mod 创建」页的导航 Tag。</summary>
    public const string ModStudioTag = "modstudio";

    /// <summary>创意工坊页的导航 Tag。</summary>
    public const string WorkshopTag = "workshop";

    /// <summary>创意工坊 mod 详情子页的导航 Tag（不是左栏菜单项；靠参数指定是哪个 mod）。</summary>
    public const string WorkshopDetailTag = "workshopdetail";

    /// <summary>「添加模组」子页的导航 Tag（不是左栏菜单项；靠参数指定目标播放集）。</summary>
    public const string AddModsTag = "addmods";

    /// <summary>DLC 页的导航 Tag。</summary>
    public const string DlcTag = "dlc";

    /// <summary>存档页的导航 Tag。</summary>
    public const string SavesTag = "saves";

    /// <summary>「备份」子页的导航 Tag（不是左栏菜单项）。</summary>
    public const string SaveBackupsTag = "savebackups";

    /// <summary>成就页的导航 Tag。</summary>
    public const string AchievementsTag = "achievements";

    /// <summary>账号页的导航 Tag。</summary>
    public const string AccountTag = "account";

    /// <summary>设置页的导航 Tag。</summary>
    public const string SettingsTag = "settings";

    /// <summary>「性能与日志」页的导航 Tag。</summary>
    public const string DiagnosticsTag = "diagnostics";

    /// <summary>「崩溃分析」页的导航 Tag。</summary>
    public const string CrashAnalysisTag = "crashanalysis";

    private static readonly Dictionary<string, string> TypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [LibraryTag] = "PdxLauncher.App.Views.GameLibraryPage",
        [HomeTag] = "PdxLauncher.App.Views.HomePage",
        [PlaySetsTag] = "PdxLauncher.App.Views.ModsPage",
        [ModLibraryTag] = "PdxLauncher.App.Views.ModLibraryPage",
        [ModStudioTag] = "PdxLauncher.App.Views.ModStudioPage",
        [WorkshopTag] = "PdxLauncher.App.Views.WorkshopPage",
        [WorkshopDetailTag] = "PdxLauncher.App.Views.WorkshopDetailPage",
        [AddModsTag] = "PdxLauncher.App.Views.AddModsPage",
        [DlcTag] = "PdxLauncher.App.Views.DlcPage",
        [SavesTag] = "PdxLauncher.App.Views.SavesPage",
        [SaveBackupsTag] = "PdxLauncher.App.Views.SaveBackupsPage",
        [AchievementsTag] = "PdxLauncher.App.Views.AchievementsPage",
        [AccountTag] = "PdxLauncher.App.Views.AccountPage",
        [SettingsTag] = "PdxLauncher.App.Views.SettingsPage",
        [DiagnosticsTag] = "PdxLauncher.App.Views.DiagnosticsPage",
        [CrashAnalysisTag] = "PdxLauncher.App.Views.CrashAnalysisPage",
    };

    private readonly IAppLogger _logger;

    public NavigationService(IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>宿主 Frame；由 MainWindow 在构造后注入。</summary>
    public Frame? Frame { get; set; }

    /// <summary>当前页的导航 Tag；未导航时为 null。</summary>
    public string? CurrentTag { get; private set; }

    /// <summary>正在执行 Frame.Navigate；重入请求先排队，等这一次结束再跑。</summary>
    private bool _navigating;

    /// <summary>重入期间攒下的最新一次请求（旧的直接丢掉，只保留最后一次）。</summary>
    private PendingNavigation? _pending;

    /// <summary>已经排了一帧重试，避免同一次失败排出一串重复导航。</summary>
    private bool _retryScheduled;

    /// <summary>请求代号；下一帧重试时只认最新代号，用户中途改道就不补旧目标。</summary>
    private int _requestVersion;

    /// <summary>Tag → 页面类型；未知 Tag 或类型未编译进来时返回 null。</summary>
    public Type? ResolvePageType(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || !TypeNames.TryGetValue(tag, out var typeName))
        {
            return null;
        }

        return typeof(NavigationService).Assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
    }

    /// <summary>一次导航最多尝试 3 次（首次 + 2 次重试）；再多说明 Frame 真的坏了。</summary>
    private const int MaxRetryAttempts = 2;

    /// <summary>
    /// 按 Tag 导航；<c>true</c> 表示请求已受理（可能排在下一帧重试，最终失败会写日志）。
    /// <paramref name="parameter"/> 为 null 时，重复导航到当前页会被跳过。
    /// <para>
    /// 带参数时不跳过：详情子页用同一个 Tag 打开不同的 mod，必须允许「同 Tag 再导航」。
    /// </para>
    /// <para>
    /// 已知问题 D22：从游戏库点卡片后跳主页时偶发 <c>Frame.Navigate</c> 返回 false，
    /// 原因是同一帧内还有一次导航没走完。这里用「重入排队 + 下一帧重试」兜住：
    /// 重入期间只保留最新请求，避免旧目标在用户已经改道后又抢回页面。
    /// </para>
    /// </summary>
    /// <param name="tag">目标页的导航 Tag。</param>
    /// <param name="parameter">传给页面 <c>OnNavigatedTo</c> 的参数；普通页面不传。</param>
    public bool Navigate(string? tag, object? parameter = null) =>
        NavigateCore(tag, parameter, attempt: 0);

    private bool NavigateCore(string? tag, object? parameter, int attempt)
    {
        if (Frame is null)
        {
            _logger.Warn("导航失败：Frame 尚未注入 (" + tag + ")");
            return false;
        }

        var version = unchecked(++_requestVersion);

        if (parameter is null && string.Equals(CurrentTag, tag, StringComparison.OrdinalIgnoreCase))
        {
            // 用户点回当前页：也算一次「最新动作」，让挂起的旧重试失效，别再抢回别的页面。
            return true;
        }

        var pageType = ResolvePageType(tag);
        if (pageType is null)
        {
            _logger.Warn("导航失败：找不到页面类型 (" + tag + ")");
            return false;
        }

        // Frame.Navigate 内部会同步触发布局 / SelectionChanged，NavigationView 可能回手再调一次
        // Navigate。重入时不硬闯，只记下最新目标，等外层这一趟结束再跑。
        if (_navigating)
        {
            _pending = new PendingNavigation(tag, parameter, version, attempt);
            return true;
        }

        _navigating = true;
        try
        {
            var navigated = Frame.Navigate(pageType, parameter);
            if (navigated)
            {
                CurrentTag = tag;
                return true;
            }

            if (attempt >= MaxRetryAttempts)
            {
                _logger.Error(
                    "导航失败：Frame.Navigate 连续返回 false，已放弃 (" + tag + "，attempt=" + attempt + ")");
                return false;
            }

            _logger.Warn("导航失败：Frame.Navigate 返回 false (" + tag + ")，安排下一帧重试");
            return ScheduleRetry(tag, parameter, version, attempt + 1);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or TypeLoadException)
        {
            _logger.Error("导航异常 (" + tag + ")", ex);
            return false;
        }
        finally
        {
            _navigating = false;
            DrainPending();
        }
    }

    /// <summary>重试排队：只排一次；真正执行时如果用户又点了别的页面，就放弃这次旧目标。</summary>
    private bool ScheduleRetry(string? tag, object? parameter, int version, int attempt)
    {
        if (_retryScheduled)
        {
            _pending = new PendingNavigation(tag, parameter, version, attempt);
            return true;
        }

        var dispatcher = Frame?.DispatcherQueue;
        if (dispatcher is null)
        {
            return false;
        }

        _retryScheduled = true;
        var queued = dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _retryScheduled = false;

            // 重入期间攒下的最新请求优先；它可能来自比本次重试更新的用户动作。
            if (_pending is { } pending)
            {
                _pending = null;
                if (pending.Version == _requestVersion)
                {
                    NavigateCore(pending.Tag, pending.Parameter, pending.Attempt);
                }

                return;
            }

            if (version != _requestVersion)
            {
                return;
            }

            NavigateCore(tag, parameter, attempt);
        });

        if (!queued)
        {
            _retryScheduled = false;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 外层导航结束后处理排队里的最新一次。
    /// <para>
    /// 这里绝不直接调用 <see cref="NavigateCore"/>：失败导航的 finally 若同步重入，会在
    /// 「两次导航都失败」时形成 DrainPending → Navigate → 失败 → ScheduleRetry →
    /// DrainPending 的无限递归。统一丢到下一帧，递归就自然断开。
    /// 已经有重试在排队时也不重复排，下一帧回调会消费 <see cref="_pending"/>。
    /// </para>
    /// </summary>
    private void DrainPending()
    {
        if (_retryScheduled || _pending is not { } pending)
        {
            return;
        }

        _pending = null;
        if (pending.Version != _requestVersion)
        {
            return;
        }

        var dispatcher = Frame?.DispatcherQueue;
        if (dispatcher is null)
        {
            return;
        }

        if (!dispatcher.TryEnqueue(
                DispatcherQueuePriority.Low,
                () => NavigateCore(pending.Tag, pending.Parameter, pending.Attempt)))
        {
            _logger.Warn("导航失败：无法派发排队中的导航 (" + pending.Tag + ")");
        }
    }

    private readonly record struct PendingNavigation(string? Tag, object? Parameter, int Version, int Attempt);

    /// <summary>回到游戏库页。</summary>
    public bool GoBackToLibrary() => Navigate(LibraryTag);

    /// <summary>页面类型 → 导航 Tag；用于 Frame.Navigated 之后同步左侧选中项。</summary>
    public static string? TagForPageType(Type? pageType)
    {
        if (pageType?.FullName is not { } fullName)
        {
            return null;
        }

        foreach (var pair in TypeNames)
        {
            if (string.Equals(pair.Value, fullName, StringComparison.Ordinal))
            {
                return pair.Key;
            }
        }

        return null;
    }
}
