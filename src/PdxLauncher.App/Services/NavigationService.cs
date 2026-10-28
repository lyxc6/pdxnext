namespace PdxLauncher.App.Services;

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

    /// <summary>创意工坊页的导航 Tag。</summary>
    public const string WorkshopTag = "workshop";

    /// <summary>创意工坊 mod 详情子页的导航 Tag（不是左栏菜单项；靠参数指定是哪个 mod）。</summary>
    public const string WorkshopDetailTag = "workshopdetail";

    /// <summary>DLC 页的导航 Tag。</summary>
    public const string DlcTag = "dlc";

    /// <summary>账号页的导航 Tag。</summary>
    public const string AccountTag = "account";

    /// <summary>设置页的导航 Tag。</summary>
    public const string SettingsTag = "settings";

    private static readonly Dictionary<string, string> TypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [LibraryTag] = "PdxLauncher.App.Views.GameLibraryPage",
        [HomeTag] = "PdxLauncher.App.Views.HomePage",
        [PlaySetsTag] = "PdxLauncher.App.Views.ModsPage",
        [ModLibraryTag] = "PdxLauncher.App.Views.ModLibraryPage",
        [WorkshopTag] = "PdxLauncher.App.Views.WorkshopPage",
        [WorkshopDetailTag] = "PdxLauncher.App.Views.WorkshopDetailPage",
        [DlcTag] = "PdxLauncher.App.Views.DlcPage",
        [AccountTag] = "PdxLauncher.App.Views.AccountPage",
        [SettingsTag] = "PdxLauncher.App.Views.SettingsPage",
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

    /// <summary>Tag → 页面类型；未知 Tag 或类型未编译进来时返回 null。</summary>
    public Type? ResolvePageType(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || !TypeNames.TryGetValue(tag, out var typeName))
        {
            return null;
        }

        return typeof(NavigationService).Assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
    }

    /// <summary>
    /// 按 Tag 导航；成功返回 true。<paramref name="parameter"/> 为 null 时，重复导航到当前页会被跳过。
    /// <para>
    /// 带参数时不跳过：详情子页用同一个 Tag 打开不同的 mod，必须允许「同 Tag 再导航」。
    /// </para>
    /// </summary>
    /// <param name="tag">目标页的导航 Tag。</param>
    /// <param name="parameter">传给页面 <c>OnNavigatedTo</c> 的参数；普通页面不传。</param>
    public bool Navigate(string? tag, object? parameter = null)
    {
        if (Frame is null)
        {
            _logger.Warn("导航失败：Frame 尚未注入 (" + tag + ")");
            return false;
        }

        if (parameter is null && string.Equals(CurrentTag, tag, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var pageType = ResolvePageType(tag);
        if (pageType is null)
        {
            _logger.Warn("导航失败：找不到页面类型 (" + tag + ")");
            return false;
        }

        try
        {
            var navigated = Frame.Navigate(pageType, parameter);
            if (navigated)
            {
                CurrentTag = tag;
            }
            else
            {
                _logger.Warn("导航失败：Frame.Navigate 返回 false (" + tag + ")");
            }

            return navigated;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or TypeLoadException)
        {
            _logger.Error("导航异常 (" + tag + ")", ex);
            return false;
        }
    }

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
