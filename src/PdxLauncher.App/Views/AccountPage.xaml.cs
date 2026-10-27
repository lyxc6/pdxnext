namespace PdxLauncher.App.Views;

using System.ComponentModel;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;

/// <summary>
/// 账号页：Paradox 账号登录 / 退出（左栏导航里的独立页面）。
/// 页面只负责生命周期与控件事件；会话状态与文案都在 <see cref="AccountViewModel"/> 里。
/// 登录反馈走页内 InfoBar，不再弹 ContentDialog。
/// </summary>
public sealed partial class AccountPage : Page
{
    private readonly AccountViewModel _viewModel;

    /// <summary>页面生命周期令牌；NavigationCacheMode=Required 会在返回页面时重建。</summary>
    private CancellationTokenSource _lifetime = new();

    public AccountPage()
    {
        InitializeComponent();

        _viewModel = new AccountViewModel();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public AccountViewModel ViewModel => _viewModel;

    /// <summary>XAML 里 <c>{x:Bind L.SomeKey, Mode=OneWay}</c> 的入口。</summary>
    public Localizer L => Localizer.Instance;

    // ------------------------------------------------------------ 生命周期

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_lifetime.IsCancellationRequested)
        {
            _lifetime = new CancellationTokenSource();
        }

        // 启动阶段的会话恢复是异步的：恢复完成后由这里把界面刷成已登录。
        AppServices.Account.SessionChanged -= OnSessionChanged;
        AppServices.Account.SessionChanged += OnSessionChanged;

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        RefreshAccountPanels();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.Account.SessionChanged -= OnSessionChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _lifetime.Cancel();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (!DispatcherQueue.TryEnqueue(RefreshAccountPanels))
        {
            AppServices.Logger.Warn("AccountPage 无法派发会话变化事件");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AccountViewModel.StatusLevel) or nameof(AccountViewModel.StatusMessage))
        {
            UpdateStatusBar();
        }
    }

    // ------------------------------------------------------------ 控件事件

    /// <summary>身份卡右侧的「登录」：展开表单并把焦点交给邮箱框。</summary>
    private void OnAccountPrimaryClick(object sender, RoutedEventArgs e)
    {
        expAccountSignIn.IsExpanded = true;
        EmailBox.Focus(FocusState.Programmatic);
    }

    private async void OnAccountSignInClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var password = PasswordBoxControl.Password;
            var error = await _viewModel.SignInAsync(password, _lifetime.Token).ConfigureAwait(true);

            // 无论成功失败都把密码框清空，别让明文留在控件上。
            PasswordBoxControl.Password = string.Empty;

            if (error is not null && !_viewModel.HasStatusMessage)
            {
                // ViewModel 没写反馈（理论上不会发生）时兜底，避免用户看不到失败原因。
                _viewModel.ShowStatus(error);
            }

            RefreshAccountPanels();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("AccountPage 登录失败", ex);
            _viewModel.ShowStatus(ex.Message);
            RefreshAccountPanels();
        }
    }

    private async void OnAccountSignOutClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = L.AccountDialogSignOutTitle,
                Content = L.AccountDialogSignOutBody,
                PrimaryButtonText = L.AccountSignOut,
                CloseButtonText = L.CommonCancel,
                DefaultButton = ContentDialogButton.Close,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            _viewModel.SignOut();
            RefreshAccountPanels();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("AccountPage 登出失败", ex);
        }
    }

    // ------------------------------------------------------------ 状态刷新

    /// <summary>会话可能变了：通知 ViewModel 重算派生属性，并同步 InfoBar 的级别。</summary>
    private void RefreshAccountPanels()
    {
        _viewModel.RaiseAccountProps();
        UpdateStatusBar();
    }

    private void UpdateStatusBar()
    {
        infoAccountStatus.Severity = _viewModel.StatusLevel switch
        {
            AccountStatusLevel.Success => InfoBarSeverity.Success,
            AccountStatusLevel.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational,
        };
    }
}
