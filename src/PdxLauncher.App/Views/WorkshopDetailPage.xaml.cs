using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;

namespace PdxLauncher.App.Views;

/// <summary>
/// 创意工坊 mod 详情子页。行数据通过导航参数（<see cref="WorkshopRowViewModel"/>）传进来，
/// 详情正文、截图与外链由 <see cref="WorkshopDetailViewModel"/> 负责加载。
/// 页面本身不持有导航服务：返回 / 去别处一律抛 <see cref="NavigationRequested"/>，由 MainWindow 处理。
/// </summary>
public sealed partial class WorkshopDetailPage : Page
{
    private readonly WorkshopDetailViewModel _viewModel;
    private WorkshopRowViewModel? _row;
    private CancellationTokenSource? _lifetime;

    /// <summary>初始化详情页，并接好依赖。</summary>
    public WorkshopDetailPage()
    {
        InitializeComponent();

        _viewModel = new WorkshopDetailViewModel(
            new WorkshopDetailSource(
                AppServices.SteamWorkshop,
                AppServices.PdxMods,
                AppServices.Account,
                AppServices.WorkshopInfo,
                AppServices.Logger),
            new WorkshopImageCache(
                AppServices.FileSystem,
                AppServices.Assets,
                AppServices.WorkshopInfo),
            AppServices.Logger);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>详情页视图模型。</summary>
    public WorkshopDetailViewModel ViewModel => _viewModel;

    /// <summary>本地化文案。</summary>
    public Localizer L => Localizer.Instance;

    /// <summary>需要导航到别处（例如返回创意工坊列表）时触发。</summary>
    public event EventHandler<string>? NavigationRequested;

    /// <inheritdoc />
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // 参数就是列表里的那一行；缺失时页面只显示空壳并报错，不做静默兜底。
        _row = e.Parameter as WorkshopRowViewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_row is null)
        {
            _viewModel.HasError = true;
            _viewModel.ErrorText = L.T(Keys.WorkshopDetailFailed, L.T(Keys.WorkshopDetailErrorUnknown));
            return;
        }

        await RunAsync(_row);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _lifetime?.Cancel();
    }

    private async void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Row is null)
        {
            return;
        }

        await RunAsync(_viewModel.Row);
    }

    private async Task RunAsync(WorkshopRowViewModel row)
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = new CancellationTokenSource();

        try
        {
            await _viewModel.LoadAsync(row, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // 页面已经离开（或换了行），本次加载作废。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("创意工坊详情页加载异常: " + row.Id, ex);
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        NavigationRequested?.Invoke(this, NavigationService.WorkshopTag);
    }

    private void OnOpenBrowserClick(object sender, RoutedEventArgs e)
    {
        ShellLauncher.TryOpenUrl(_viewModel.WebUrl, "创意工坊详情");
    }

    private void OnOpenSteamClick(object sender, RoutedEventArgs e)
    {
        ShellLauncher.TryOpenSteam(_viewModel.SteamUrl, "创意工坊详情");
    }

    private void OnLinkClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is WorkshopLinkViewModel link)
        {
            ShellLauncher.TryOpenUrl(link.Url, "创意工坊详情");
        }
    }
}
