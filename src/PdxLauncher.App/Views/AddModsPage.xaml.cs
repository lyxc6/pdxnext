namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 「添加模组」子页：列出 mod 库里还没进目标播放集的 mod，让用户多选。
/// </summary>
/// <remarks>
/// 页面自己不持导航职责：返回时抛 <see cref="NavigationRequested"/>（回到播放集页），
/// 确认时抛 <see cref="ModsSelected"/>（把选择结果交回播放集页，由它追加并自动保存）。
/// </remarks>
public sealed partial class AddModsPage : Page
{
    private readonly AddModsViewModel _viewModel;
    private CancellationTokenSource _lifetime = new();

    private AddModsNavigationRequest? _request;

    /// <summary>创建页面。</summary>
    public AddModsPage()
    {
        InitializeComponent();

        _viewModel = new AddModsViewModel(
            AppServices.PlaySets,
            new ModLibraryService(AppServices.PlaySets, AppServices.Mods, AppServices.Logger),
            AppServices.Logger);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>ViewModel。</summary>
    public AddModsViewModel ViewModel => _viewModel;

    /// <summary>XAML 的文案入口：<c>{x:Bind L.SomeKey, Mode=OneWay}</c>。</summary>
    public Localizer L => Localizer.Instance;

    /// <summary>需要返回上一层（播放集页）时触发。</summary>
    public event EventHandler<string>? NavigationRequested;

    /// <summary>用户确认添加时触发，参数是勾选的 mod。</summary>
    public event EventHandler<AddModsNavigationResult>? ModsSelected;

    /// <summary>导航进入时取参数：目标播放集。</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _request = e.Parameter as AddModsNavigationRequest;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_lifetime.IsCancellationRequested)
        {
            _lifetime.Dispose();
            _lifetime = new CancellationTokenSource();
        }

        if (_request is null)
        {
            // 缺少参数就不猜：回到播放集页，让用户从工具条重新进入。
            AppServices.Logger.Warn("添加模组页缺少目标播放集参数");
            OnBackClick(sender, e);
            return;
        }

        _viewModel.SetPlaySet(_request.PlaySetName);
        await LoadAsync().ConfigureAwait(true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _lifetime.Cancel();
    }

    /// <summary>读取候选 mod。</summary>
    private async Task LoadAsync()
    {
        if (_request is null)
        {
            return;
        }

        try
        {
            await _viewModel
                .LoadAsync(AppServices.CurrentGame, _request.PlaySetId, _lifetime.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已经离开：本次加载作废。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("添加模组页加载失败", ex);
        }
    }

    /// <summary>搜索框实时过滤：x:Bind 没有 UpdateSourceTrigger，所以由这里写回 ViewModel。</summary>
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            _viewModel.SearchText = box.Text;
        }
    }

    /// <summary>返回播放集页（不添加任何 mod）。</summary>
    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        NavigationRequested?.Invoke(this, _request?.ReturnTag ?? NavigationService.PlaySetsTag);
    }

    /// <summary>取消：等价于返回。</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        OnBackClick(sender, e);
    }

    /// <summary>确认添加：把勾选的 mod 交回播放集页。</summary>
    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_request is null)
        {
            return;
        }

        var entries = _viewModel.GetSelectedEntries();
        if (entries.Count == 0)
        {
            return;
        }

        ModsSelected?.Invoke(this, new AddModsNavigationResult(_request.PlaySetId, entries));
    }
}
