namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 存档页：浏览当前游戏的存档，并提供备份 / 恢复 / 删除。
/// 扫描与所有写操作都委托给 <see cref="SavesViewModel"/>（内部走线程池 + <c>ISaveGameBackupService</c>），
/// 页面只负责生命周期、刷新、确认框与按钮事件；两个破坏性确认（删除存档、删除备份）
/// 都把初始焦点放在「取消」，Enter 继承焦点，Escape 恒为取消。
/// </summary>
public sealed partial class SavesPage : Page
{
    private readonly SavesViewModel _viewModel;

    /// <summary>页面生命周期令牌；页面不缓存（NavigationCacheMode=Disabled），每次进入都会重建。</summary>
    private CancellationTokenSource _lifetime = new();

    /// <summary>重载协调器：连点刷新 / 连续切游戏时取消旧扫描，只应用最新那次的结果。</summary>
    private readonly ReloadCoordinator _reload = new();

    /// <summary>把第二段解析后的读屏名同步到 ListItem；容器复用/卸载时必须退订。</summary>
    private readonly SaveRowAutomationWatcher _rowAutomation = new();

    /// <summary>容器到行的映射，用于复用同一容器换行时先解除旧订阅。</summary>
    private readonly Dictionary<ListViewItem, SaveGameRowViewModel> _automationRows = [];

    public SavesPage()
    {
        InitializeComponent();

        _viewModel = new SavesViewModel(AppServices.SaveGameFiles, AppServices.SaveBackups, AppServices.Logger);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public SavesViewModel ViewModel => _viewModel;

    /// <summary>XAML 里 <c>{x:Bind L.Xxx, Mode=OneWay}</c> 的绑定入口。</summary>
    public Localizer L => Localizer.Instance;

    // ------------------------------------------------------------ 生命周期

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lifetime.IsCancellationRequested)
            {
                _lifetime = new CancellationTokenSource();
            }

            AppServices.CurrentGameChanged -= OnCurrentGameChanged;
            AppServices.CurrentGameChanged += OnCurrentGameChanged;

            await ReloadAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SavesPage 初始化失败", ex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.CurrentGameChanged -= OnCurrentGameChanged;

        _rowAutomation.Clear();
        _automationRows.Clear();
        _lifetime.Cancel();
    }

    private void OnCurrentGameChanged(object? sender, GameInstallation? game)
    {
        // 用户切游戏时事件就在 UI 线程上：直接开始扫描，少一帧上一款游戏的残留。
        // 后台刷新（主题重建 GameInstallation）仍走派发，避免跨线程碰 ViewModel。
        if (DispatcherQueue.HasThreadAccess)
        {
            _ = ReloadAsync();
            return;
        }

        if (!DispatcherQueue.TryEnqueue(() => _ = ReloadAsync()))
        {
            AppServices.Logger.Warn("SavesPage 无法派发游戏切换事件");
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        // 扫描由协调器取消上一次；错误在 onError 里记日志，不弹框。
        _ = ReloadAsync();
    }

    /// <summary>
    /// 行容器（UIA 里的 ListItem）的自动化 id 与名字。WinUI 的 Style Setter.Value 不支持 {Binding}，
    /// 所以不能在 ItemContainerStyle 里绑；容器每次（含复用）绑定内容时在这里写。
    /// id 是 <c>save_N</c>（ASCII，不随语言变），名字是「文件名，日期，详情」——
    /// 不写的话读屏只会读到 <c>PdxLauncher.App.ViewModels.SaveGameRowViewModel</c>，整列无法区分。
    /// 第二段补齐细节后行会发 PropertyChanged，由 <see cref="SaveRowAutomationWatcher"/> 重写名字。
    /// </summary>
    private void OnSaveContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not ListViewItem container)
        {
            return;
        }

        if (args.InRecycleQueue)
        {
            if (_automationRows.Remove(container, out var recycled))
            {
                _rowAutomation.Untrack(recycled);
            }

            return;
        }

        if (args.Item is not SaveGameRowViewModel row)
        {
            return;
        }

        if (_automationRows.TryGetValue(container, out var previous) && !ReferenceEquals(previous, row))
        {
            _rowAutomation.Untrack(previous);
        }

        _automationRows[container] = row;
        AutomationProperties.SetAutomationId(container, row.AutomationId);
        _rowAutomation.Track(row, name => AutomationProperties.SetName(container, name));
    }

    /// <summary>
    /// 备份行容器（备份段的 ListItem）同样需要写 id/name：不写的话读屏只会读类型全名。
    /// 备份行的名字不随后台补齐变化（创建时间一开始就有），所以只写一次即可。
    /// </summary>
    private void OnBackupContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.ItemContainer is not ListViewItem container)
        {
            return;
        }

        if (args.Item is SaveBackupRowViewModel row)
        {
            AutomationProperties.SetAutomationId(container, row.AutomationId);
            AutomationProperties.SetName(container, row.AutomationName);
        }
    }

    // ------------------------------------------------------------ 备份 / 恢复 / 删除

    private async void OnBackupSaveClick(object sender, RoutedEventArgs e)
    {
        if (RowFrom(sender) is { } row)
        {
            await InvokeAsync(() => _viewModel.BackupAsync(row, _lifetime.Token));
        }
    }

    /// <summary>删除真实存档：破坏性操作，初始焦点在「取消」；服务只把文件移入 trash。</summary>
    private async void OnDeleteSaveClick(object sender, RoutedEventArgs e)
    {
        if (RowFrom(sender) is not { } row)
        {
            return;
        }

        var confirmed = await ConfirmOverlay.ConfirmAsync(
            L.SavesConfirmDeleteSaveTitle,
            L.T(Keys.SavesConfirmDeleteSaveContent, row.FileName),
            L.CommonDelete,
            L.CommonCancel,
            initialFocus: InitialFocus.Cancel);
        if (!confirmed)
        {
            return;
        }

        await InvokeAsync(() => _viewModel.DeleteSaveAsync(row, _lifetime.Token));
    }

    /// <summary>恢复备份：非破坏性（默认 overwrite:false，新建副本），初始焦点保持「确认」。</summary>
    private async void OnRestoreBackupClick(object sender, RoutedEventArgs e)
    {
        if (BackupRowFrom(sender) is not { } row)
        {
            return;
        }

        var confirmed = await ConfirmOverlay.ConfirmAsync(
            L.SavesConfirmRestoreTitle,
            L.T(Keys.SavesConfirmRestoreContent, row.FileName),
            L.SavesActionRestore,
            L.CommonCancel);
        if (!confirmed)
        {
            return;
        }

        await InvokeAsync(() => _viewModel.RestoreAsync(row, _lifetime.Token));
    }

    /// <summary>删除备份：破坏性操作，初始焦点在「取消」。</summary>
    private async void OnDeleteBackupClick(object sender, RoutedEventArgs e)
    {
        if (BackupRowFrom(sender) is not { } row)
        {
            return;
        }

        var confirmed = await ConfirmOverlay.ConfirmAsync(
            L.SavesConfirmDeleteBackupTitle,
            L.T(Keys.SavesConfirmDeleteBackupContent, row.FileName),
            L.CommonDelete,
            L.CommonCancel,
            initialFocus: InitialFocus.Cancel);
        if (!confirmed)
        {
            return;
        }

        await InvokeAsync(() => _viewModel.DeleteBackupAsync(row, _lifetime.Token));
    }

    private void OnNoticeClose(InfoBar sender, object args) => _viewModel.DismissNotice();

    private static SaveGameRowViewModel? RowFrom(object sender) =>
        (sender as FrameworkElement)?.DataContext as SaveGameRowViewModel;

    private static SaveBackupRowViewModel? BackupRowFrom(object sender) =>
        (sender as FrameworkElement)?.DataContext as SaveBackupRowViewModel;

    /// <summary>点击事件里统一收尾：页面关闭导致的取消静默，其余异常记日志（页面已有 InfoBar 展示服务消息）。</summary>
    private async Task InvokeAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭/切游戏导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SavesPage 操作失败", ex);
        }
    }

    private Task ReloadAsync() => _reload.RunAsync(
        token => _viewModel.LoadAsync(AppServices.CurrentGame, token),
        _lifetime.Token,
        ex => AppServices.Logger.Error("SavesPage 加载失败", ex));
}
