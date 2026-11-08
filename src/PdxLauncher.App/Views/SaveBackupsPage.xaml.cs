namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Saves;

/// <summary>
/// 「备份」子页：管理当前游戏的存档备份（列表 / 恢复 / 覆盖恢复 / 删除 / 清理 / 用量 / 上限）。
/// </summary>
/// <remarks>
/// 页面自己不持导航职责：返回时抛 <see cref="NavigationRequested"/>，由 MainWindow 回到存档页。
/// 删除备份、覆盖恢复、批量清理都是破坏性操作，确认框初始焦点固定在「取消」；
/// 普通恢复不覆盖现有存档，初始焦点保持「确认」。
/// </remarks>
public sealed partial class SaveBackupsPage : Page
{
    private const double MinQuotaGb = 1;
    private const double MaxQuotaGb = 100;
    private const double BytesPerGb = 1024d * 1024d * 1024d;

    private readonly SaveBackupsViewModel _viewModel;

    private CancellationTokenSource _lifetime = new();
    private SaveBackupsNavigationRequest? _request;

    /// <summary>初始化配额输入框时 ValueChanged 会触发，必须挡住写回。</summary>
    private bool _quotaReady;

    public SaveBackupsPage()
    {
        InitializeComponent();

        _viewModel = new SaveBackupsViewModel(AppServices.SaveBackups, AppServices.Logger);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public SaveBackupsViewModel ViewModel => _viewModel;

    /// <summary>XAML 里 <c>{x:Bind L.Xxx, Mode=OneWay}</c> 的绑定入口。</summary>
    public Localizer L => Localizer.Instance;

    /// <summary>返回上一层（存档页）时触发。</summary>
    public event EventHandler<string>? NavigationRequested;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _request = e.Parameter as SaveBackupsNavigationRequest;
        SyncQuotaBox();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lifetime.IsCancellationRequested)
            {
                _lifetime = new CancellationTokenSource();
            }

            SyncQuotaBox();
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SaveBackupsPage 初始化失败", ex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _lifetime.Cancel();
    }

    private void OnBackClick(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, _request?.ReturnTag ?? NavigationService.SavesTag);

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = ReloadAsync();

    /// <summary>在资源管理器里打开备份根目录；目录不存在时先创建，避免 explorer 报错。</summary>
    private void OnOpenBackupFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var root = AppServices.SaveBackups.BackupRootDirectory;
            AppServices.FileSystem.EnsureDirectory(root);
            if (!ShellLauncher.TryStart("explorer.exe", "\"" + root + "\"", "SaveBackupsPage 打开备份目录"))
            {
                _viewModel.DismissNotice();
                AppServices.Logger.Warn("无法打开备份目录：" + root);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("打开备份目录失败", ex);
        }
    }

    /// <summary>
    /// 空间上限以 GB 为单位调整：写回 <see cref="AppSettings"/>、立即更新服务策略，
    /// 再刷新用量文案。非法/超出范围的值由 NumberBox 与钳位双重兜底。
    /// </summary>
    private async void OnQuotaValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_quotaReady || double.IsNaN(args.NewValue))
        {
            return;
        }

        try
        {
            var gb = Math.Clamp(Math.Round(args.NewValue), MinQuotaGb, MaxQuotaGb);
            var bytes = (long)(gb * BytesPerGb);
            AppServices.Settings.SaveBackupMaxTotalBytes = bytes;
            AppServices.SaveSettingsDeferred();
            AppServices.SaveBackups.ApplyPolicy(
                new SaveBackupPolicy(
                    AppServices.Settings.SaveBackupMaxTotalBytes,
                    AppServices.SaveBackups.Policy.MaxEntriesPerGame,
                    AutoPruneOldestWhenFull: true));
            _viewModel.NotifyQuotaChanged();
            await _viewModel.RefreshUsageAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("更新备份空间上限失败", ex);
        }
    }

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

        await InvokeAsync(() => _viewModel.RestoreAsync(row, overwrite: false, _lifetime.Token));
    }

    /// <summary>覆盖恢复：先把同名存档移入回收站再写回原路径，不可撤销，默认焦点在「取消」。</summary>
    private async void OnRestoreOverwriteClick(object sender, RoutedEventArgs e)
    {
        if (BackupRowFrom(sender) is not { } row)
        {
            return;
        }

        var confirmed = await ConfirmOverlay.ConfirmAsync(
            L.SavesConfirmRestoreOverwriteTitle,
            L.T(Keys.SavesConfirmRestoreOverwriteContent, row.FileName),
            L.SavesActionRestoreOverwrite,
            L.CommonCancel,
            initialFocus: InitialFocus.Cancel);
        if (!confirmed)
        {
            return;
        }

        await InvokeAsync(() => _viewModel.RestoreAsync(row, overwrite: true, _lifetime.Token));
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

    /// <summary>清理旧备份：批量删除，确认框默认焦点在「取消」。</summary>
    private async void OnPruneBackupsClick(object sender, RoutedEventArgs e)
    {
        var confirmed = await ConfirmOverlay.ConfirmAsync(
            L.SavesConfirmPruneBackupsTitle,
            L.T(Keys.SavesConfirmPruneBackupsContent, SaveBackupsViewModel.PruneKeepNewest),
            L.SavesActionPruneBackups,
            L.CommonCancel,
            initialFocus: InitialFocus.Cancel);
        if (!confirmed)
        {
            return;
        }

        await InvokeAsync(() => _viewModel.PruneAsync(_lifetime.Token));
    }

    /// <summary>备份行容器（UIA 里的 ListItem）写 id/name，避免读屏读成类型全名。</summary>
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

    private void OnNoticeClose(InfoBar sender, object args) => _viewModel.DismissNotice();

    private void SyncQuotaBox()
    {
        _quotaReady = false;
        var gb = AppServices.Settings.SaveBackupMaxTotalBytes / BytesPerGb;
        QuotaBox.Value = Math.Clamp(gb, MinQuotaGb, MaxQuotaGb);
        _quotaReady = true;
    }

    private static SaveBackupRowViewModel? BackupRowFrom(object sender) =>
        (sender as FrameworkElement)?.DataContext as SaveBackupRowViewModel;

    private async Task InvokeAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SaveBackupsPage 操作失败", ex);
        }
    }

    private Task ReloadAsync() => _viewModel.LoadAsync(_request?.Game, _lifetime.Token);
}
