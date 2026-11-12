namespace PdxLauncher.App.Views;

using System.ComponentModel;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;

using Windows.System;

/// <summary>
/// 账号页：Paradox 账号登录 / 退出（左栏导航里的独立页面）。
/// 页面只负责生命周期与控件事件；会话状态与文案都在 <see cref="AccountViewModel"/> 里。
/// 登录反馈走页内 InfoBar，不再弹 ContentDialog。
/// </summary>
public sealed partial class AccountPage : Page
{
    private readonly AccountViewModel _viewModel;

    /// <summary>页面生命周期令牌；页面不缓存（NavigationCacheMode=Disabled），每次进入都会重建。</summary>
    private CancellationTokenSource _lifetime = new();

    /// <summary>成功提示自动收起的计时器；错误提示必须由用户自己关掉。</summary>
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    public AccountPage()
    {
        InitializeComponent();

        _viewModel = new AccountViewModel();

        _statusTimer.Tick += OnStatusTimerTick;

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
        _statusTimer.Stop();
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

    private async void OnAccountSignInClick(object sender, RoutedEventArgs e) => await SignInAsync();

    /// <summary>邮箱框 / 密码框里按回车等于点「登录」。</summary>
    /// <remarks>
    /// 直接绑 Button 当默认按钮在 WinUI 里不可靠（TextBox 会自己吃掉回车），
    /// 所以显式处理：回车被消费掉，不往文本框里插换行，也不触发别的默认行为。
    /// </remarks>
    private void OnCredentialKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        if (_viewModel.CanSignIn)
        {
            _ = SignInAsync();
        }
    }

    private async Task SignInAsync()
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
            var confirmed = await ConfirmOverlay.ConfirmAsync(
                L.AccountDialogSignOutTitle,
                L.AccountDialogSignOutBody,
                L.AccountSignOut,
                L.CommonCancel);
            if (!confirmed)
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

        // 成功提示看一眼就够了，留着只会把下面的表单往下挤；错误必须留着等用户读。
        _statusTimer.Stop();
        if (_viewModel.StatusLevel == AccountStatusLevel.Success && _viewModel.HasStatusMessage)
        {
            _statusTimer.Start();
        }
    }

    private void OnStatusTimerTick(object? sender, object e)
    {
        _statusTimer.Stop();
        infoAccountStatus.IsOpen = false;
    }
}
