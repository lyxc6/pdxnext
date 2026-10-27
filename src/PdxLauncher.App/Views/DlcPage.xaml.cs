namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;

/// <summary>
/// DLC 页：磁盘 DLC 网格 + 启用开关；保存写 playsets_dlcs 并同步加载文件。
/// </summary>
public sealed partial class DlcPage : Page
{
    private readonly DlcViewModel _viewModel;
    /// <summary>页面生命周期令牌；NavigationCacheMode=Required 会在返回页面时重建。</summary>
    private CancellationTokenSource _lifetime = new();

    private bool _isReloading;

    public DlcPage()
    {
        InitializeComponent();

        _viewModel = new DlcViewModel(new PlaySetEditor(
            AppServices.PlaySets,
            AppServices.LoadFiles,
            AppServices.Logger));

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public DlcViewModel ViewModel => _viewModel;

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

            if (!AppServices.IsInitialized)
            {
                await _viewModel.LoadAsync(null, _lifetime.Token).ConfigureAwait(true);
                return;
            }

            await ReloadAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DlcPage 初始化失败", ex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.CurrentGameChanged -= OnCurrentGameChanged;

        _lifetime.Cancel();
    }

    private void OnCurrentGameChanged(object? sender, GameInstallation? game)
    {
        if (!DispatcherQueue.TryEnqueue(() => _ = ReloadAsync()))
        {
            AppServices.Logger.Warn("DlcPage 无法派发游戏切换事件");
        }
    }

    private async Task ReloadAsync()
    {
        if (_isReloading)
        {
            return;
        }

        _isReloading = true;
        try
        {
            await _viewModel.LoadAsync(AppServices.CurrentGame, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 忽略：切换页面导致的取消。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DlcPage 加载失败", ex);
        }
        finally
        {
            _isReloading = false;
        }
    }

    // ------------------------------------------------------------ 交互

    private void OnEnableAllClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.SetAllEnabled(true);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DlcPage 启用全部失败", ex);
        }
    }

    private void OnDisableAllClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.SetAllEnabled(false);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DlcPage 禁用全部失败", ex);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.SaveAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DlcPage 保存失败", ex);
            await ShowMessageAsync(L.DlcSaveFailedShort, ex.Message).ConfigureAwait(true);
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = title,
                Content = message,
                CloseButtonText = L.CommonClose,
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DlcPage 显示对话框失败", ex);
        }
    }
}
