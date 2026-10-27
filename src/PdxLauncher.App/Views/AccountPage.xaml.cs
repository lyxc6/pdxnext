namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;

/// <summary>
/// 账号页：Paradox 账号登录 / 退出（左栏导航里的独立页面）。
/// 页面只负责生命周期与控件事件；会话状态与文案都在 <see cref="AccountViewModel"/> 里。
/// </summary>
public sealed partial class AccountPage : Page
{
    private readonly AccountViewModel _viewModel;

    /// <summary>页面生命周期令牌；NavigationCacheMode=Required 会在返回页面时重建。</summary>
    private CancellationTokenSource _lifetime = new();

    /// <summary>密码框内容只在页面里存一份，交给 ViewModel 后立刻清掉。</summary>
    private string _accountPassword = string.Empty;

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

        RefreshAccountPanels();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.Account.SessionChanged -= OnSessionChanged;
        _lifetime.Cancel();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (!DispatcherQueue.TryEnqueue(RefreshAccountPanels))
        {
            AppServices.Logger.Warn("AccountPage 无法派发会话变化事件");
        }
    }

    // ------------------------------------------------------------ 控件事件

    private void OnAccountPasswordChanged(object sender, RoutedEventArgs e)
    {
        _accountPassword = (sender as PasswordBox)?.Password ?? string.Empty;
        _viewModel.RaiseAccountProps();
    }

    private async void OnAccountSignInClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var error = await _viewModel.SignInAsync(_accountPassword, _lifetime.Token).ConfigureAwait(true);
            _accountPassword = string.Empty;

            if (sender is Button { Parent: StackPanel { } panel })
            {
                // 密码框在同一组卡片里，清掉输入框内容，避免密码留在控件上。
                foreach (var child in panel.Children)
                {
                    if (child is PasswordBox box)
                    {
                        box.Password = string.Empty;
                    }
                }
            }

            if (error is not null)
            {
                await ShowMessageAsync(L.AccountDialogFailedTitle, error).ConfigureAwait(true);
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
            await ShowMessageAsync(L.AccountDialogFailedTitle, ex.Message).ConfigureAwait(true);
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

    /// <summary>登录前后切换「输密码」与「登出」两组卡片。</summary>
    private void RefreshAccountPanels()
    {
        var signedIn = _viewModel.IsSignedIn;
        if (grpAccountSignIn is { } signIn)
        {
            signIn.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        }

        if (grpAccountSignedIn is { } signedInGroup)
        {
            signedInGroup.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        }

        _viewModel.RaiseAccountProps();
    }

    // ------------------------------------------------------------ 对话框

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
            AppServices.Logger.Error("AccountPage 显示对话框失败", ex);
        }
    }
}
