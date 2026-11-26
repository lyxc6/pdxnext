namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.ComponentModel;

using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Mcp;

/// <summary>
/// 「MCP 访问密钥」子页：新建和编辑共用一套能力勾选表单。
/// 页面不持导航职责：返回 / 保存 / 删除后都抛 <see cref="NavigationRequested"/>，
/// 由 MainWindow 带着结果回到设置页并留在 MCP 分节。
/// </summary>
public sealed partial class McpKeyEditorPage : Page, INotifyPropertyChanged
{
    private McpKeyEditorViewModel? _viewModel;
    private string? _keyId;
    private string? _createdTokenToReturn;

    public McpKeyEditorPage()
    {
        InitializeComponent();
        ViewModel = new McpKeyEditorViewModel(null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>XAML 里取文案的入口。</summary>
    public Localizer L => Localizer.Instance;

    public string PageTitle => _keyId is null
        ? L.SettingsMcpKeyCreateTitle
        : L.SettingsMcpKeyEditTitle;

    public string SubtitleText => _keyId is null
        ? L.SettingsMcpKeyCreateSubtitle
        : L.SettingsMcpKeyEditSubtitle;

    /// <summary>
    /// XAML 里 <c>ViewModel</c> 的绑定入口。控件在 InitializeComponent 时就按名字抓属性，
    /// 所以构造函数先放一个「新建」VM；导航参数到达后再用同名属性替换。
    /// </summary>
    public McpKeyEditorViewModel ViewModel { get; private set; }

    /// <summary>返回设置页；结果里带一次性密钥与是否已保存。</summary>
    public event EventHandler<McpKeyEditorNavigationResult>? NavigationRequested;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var request = e.Parameter as McpKeyEditorNavigationRequest;
        _keyId = request?.KeyId;

        try
        {
            ViewModel = new McpKeyEditorViewModel(_keyId);
            ViewModel.Initialize(new McpAccessKeyStore(AppServices.Database));
            _viewModel = ViewModel;
            OnPropertyChanged(nameof(ViewModel));
        }
        catch (Exception exception)
        {
            AppServices.Logger.Error("初始化 MCP 密钥编辑页失败", exception);
            _viewModel = ViewModel;
        }

        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(SubtitleText));
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void OnCapabilityChecked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.OnCapabilityChanged();
    }

    private void OnBackClick(object sender, RoutedEventArgs e) =>
        RaiseNavigation(
            saved: _createdTokenToReturn is not null,
            createdToken: _createdTokenToReturn,
            keyId: _keyId);

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        // 已经显示过一次明文（新建完成）时，再点主按钮就是「完成」，直接回去，
        // 避免在同一个密钥上重建一次、把刚生成的 token 作废。
        if (_viewModel.HasCreatedToken)
        {
            OnBackClick(sender, e);
            return;
        }

        var created = _viewModel.Save();
        if (_viewModel.HasError)
        {
            return;
        }

        if (created is not null)
        {
            // 关键：新建成功后先停在页内，让用户看到并复制唯一一次明文；
            // 点「完成」时再带回去，供设置页生成客户端配置。
            _keyId = created.Key.Id;
            _createdTokenToReturn = created.RawToken;
            RefreshFooter();
            return;
        }

        RaiseNavigation(saved: true, createdToken: null, keyId: _keyId);
    }

    private void OnRotateClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _viewModel.Rotate() is null)
        {
            return;
        }

        // 轮换不离开页面：明文就在页内展示一次，用户复制后点「完成」再返回。
        _createdTokenToReturn = _viewModel.CreatedToken;
        RefreshFooter();
        TokenCopiedText.Text = string.Empty;
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var confirmed = await ConfirmOverlay.ConfirmAsync(
            L.SettingsMcpKeyDeleteTitle,
            L.T(Keys.SettingsMcpKeyDeleteContent, _viewModel.DisplayName),
            L.SettingsMcpKeyDelete,
            L.CommonCancel,
            initialFocus: InitialFocus.Cancel);
        if (!confirmed)
        {
            return;
        }

        _viewModel.Delete();
        if (_viewModel.HasError)
        {
            return;
        }

        RaiseNavigation(saved: true, createdToken: null, keyId: null);
    }

    private void OnCopyTokenClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || !_viewModel.HasCreatedToken)
        {
            return;
        }

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(_viewModel.CreatedToken);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        TokenCopiedText.Text = L.SettingsMcpKeyCopied;
    }

    /// <summary>到期时间沿用已有日期选择器；这里只负责把可选值交给 ViewModel。</summary>
    private async void OnSetExpiryClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var initial = _viewModel.ExpiresUtc?.ToLocalTime().Date ?? DateTimeOffset.UtcNow.AddDays(30).Date;
        var dialog = new ContentDialog
        {
            Title = L.SettingsMcpKeyExpiryDialogTitle,
            PrimaryButtonText = L.CommonSave,
            CloseButtonText = L.CommonCancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        var picker = new CalendarDatePicker
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsTodayHighlighted = true,
            Date = initial,
        };
        dialog.Content = picker;

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || picker.Date is not { } date)
        {
            return;
        }

        // 选中的是「当天结束」而不是零时：用户选 12-31 期望那天仍可用。
        var local = date.Date.AddDays(1).AddTicks(-1);
        _viewModel.SetExpiry(new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime());
    }

    private void OnClearExpiryClick(object sender, RoutedEventArgs e) => _viewModel?.ClearExpiry();

    /// <summary>新建成功 / 轮换后把底部主按钮切成「完成」，取消变「返回」。</summary>
    private void RefreshFooter()
    {
        if (_viewModel is null || SaveButton is null || CancelButton is null)
        {
            return;
        }

        var created = _viewModel.HasCreatedToken;
        SaveButton.Content = created ? L.SettingsMcpKeyDone : L.SettingsMcpKeySave;
        SaveButton.IsEnabled = created || _viewModel.CanSaveName;
        CancelButton.Content = created ? L.SettingsMcpKeyBack : L.SettingsMcpKeyCancel;
    }

    private void RaiseNavigation(bool saved, string? createdToken, string? keyId) =>
        NavigationRequested?.Invoke(
            this,
            new McpKeyEditorNavigationResult(saved, keyId, createdToken, _viewModel?.ErrorMessage ?? string.Empty));
}
