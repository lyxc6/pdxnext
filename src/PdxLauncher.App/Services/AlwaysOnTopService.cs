namespace PdxLauncher.App.Services;

using Microsoft.UI.Windowing;

using PdxLauncher.App.Localization;

/// <summary>
/// 窗口置顶的唯一入口：走 Windows App SDK 官方的 OverlappedPresenter.IsAlwaysOnTop。
/// 不要在外部用 SetWindowPos(HWND_TOPMOST) 钉窗口——上一轮"点别的程序切不走窗口"
/// 就是外部脚本那样钉住后没有解除留下的。
/// </summary>
public static class AlwaysOnTopService
{
    private static AppWindow? _appWindow;

    /// <summary>置顶状态已生效且已写入 settings.json 之后触发，参数为新状态。</summary>
    public static event EventHandler<bool>? Changed;

    /// <summary>当前窗口是否支持置顶（只有 Overlapped 呈现方式支持）。</summary>
    public static bool IsSupported => _appWindow?.Presenter is OverlappedPresenter;

    /// <summary>窗口创建后调用一次，登记要操作的 AppWindow。</summary>
    public static void Attach(AppWindow appWindow) => _appWindow = appWindow;

    /// <summary>把持久化偏好应用到窗口（启动时调用），不修改设置。</summary>
    public static bool Apply(bool alwaysOnTop) => TryApply(alwaysOnTop, out _);

    /// <summary>
    /// 切换置顶：应用 → 写 settings.json → 通知。应用或保存失败时不改设置，返回 false 并给出原因。
    /// </summary>
    public static bool Set(bool alwaysOnTop, out string? error)
    {
        if (!AppServices.IsInitialized)
        {
            error = Localizer.Instance.T(Keys.ShellAlwaysOnTopNotReady);
            return false;
        }

        if (!TryApply(alwaysOnTop, out error))
        {
            return false;
        }

        try
        {
            AppServices.Settings.AlwaysOnTop = alwaysOnTop;
            AppServices.SaveSettings();
        }
        catch (Exception ex)
        {
            error = Localizer.Instance.T(Keys.ShellAlwaysOnTopSaveFailed, ex.Message);
            AppServices.Logger.Error("保存窗口置顶设置失败", ex);
            return false;
        }

        Changed?.Invoke(null, alwaysOnTop);
        return true;
    }

    private static bool TryApply(bool alwaysOnTop, out string? error)
    {
        if (_appWindow?.Presenter is not OverlappedPresenter presenter)
        {
            error = Localizer.Instance.T(Keys.ShellAlwaysOnTopUnsupported);
            return false;
        }

        try
        {
            presenter.IsAlwaysOnTop = alwaysOnTop;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = Localizer.Instance.T(Keys.ShellAlwaysOnTopApplyFailed, ex.Message);
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("切换窗口置顶失败", ex);
            }

            return false;
        }
    }
}
