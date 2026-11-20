namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 「Mod 编辑」页骨架（第 2 部分）：只负责分节切换、左栏显隐联动与游戏信息同步。
/// 四个分区的实际功能（导出 / 检索 / 建模板 / 清单）在第 3 部分接入；
/// 全程只用本地数据，不构造 URL、不发网络请求。
/// </summary>
public sealed partial class ModEditorPage : Page
{
    private readonly ModEditorViewModel _viewModel = new();

    /// <summary>左栏展开宽度（DIP），收起时写 0。</summary>
    private const double LeftPaneWidth = 216;

    /// <summary>四个分区顺序，与 XAML 左栏 Tag / 右栏 StackPanel 一致。</summary>
    private static readonly string[] SectionAutomationIds =
    [
        "secModEditorExport",
        "secModEditorSearch",
        "secModEditorTemplate",
        "secModEditorLibrary",
    ];

    private bool _restoringSection;

    /// <summary>x:Bind 在 InitializeComponent 期间就会改 SelectedIndex；那时先不切分区。</summary>
    private bool _isLoaded;

    public ModEditorPage()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ModEditorViewModel ViewModel => _viewModel;

    /// <summary>XAML 里取界面文案的入口（切语言时由 Localizer 的属性变更驱动）。</summary>
    public Localizer L => Localizer.Instance;

    // ------------------------------------------------------------ 生命周期

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;

        AppServices.CurrentGameChanged -= OnCurrentGameChanged;
        AppServices.CurrentGameChanged += OnCurrentGameChanged;
        AppServices.ModEditorPaneVisibilityChanged -= OnModEditorPaneVisibilityChanged;
        AppServices.ModEditorPaneVisibilityChanged += OnModEditorPaneVisibilityChanged;

        _viewModel.SetGame(AppServices.IsInitialized ? AppServices.CurrentGame : null);
        ApplyLeftPaneVisibility();
        RestoreSection();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;

        AppServices.CurrentGameChanged -= OnCurrentGameChanged;
        AppServices.ModEditorPaneVisibilityChanged -= OnModEditorPaneVisibilityChanged;
    }

    /// <summary>当前游戏变化：只同步页头显示，功能数据第 3 部分再按游戏刷新。</summary>
    private void OnCurrentGameChanged(object? sender, GameInstallation? game)
    {
        if (!DispatcherQueue.TryEnqueue(() => _viewModel.SetGame(game)))
        {
            AppServices.Logger.Warn("ModEditorPage 无法派发游戏切换事件");
        }
    }

    /// <summary>设置页改了左栏显隐：把绑定值拉回最新并重算列宽。</summary>
    private void OnModEditorPaneVisibilityChanged(object? sender, EventArgs e)
    {
        if (!DispatcherQueue.TryEnqueue(ApplyFromSettings))
        {
            AppServices.Logger.Warn("ModEditorPage 无法派发左栏显隐变化");
        }
    }

    private void ApplyFromSettings()
    {
        _viewModel.RefreshLeftPaneVisibility();
        ApplyLeftPaneVisibility();
    }

    /// <summary>左栏显隐落到 Column0 宽度：显示 216、隐藏 0。</summary>
    private void ApplyLeftPaneVisibility()
    {
        leftPaneColumn.Width = new GridLength(_viewModel.IsLeftPaneVisible ? LeftPaneWidth : 0);
    }

    // ------------------------------------------------------------ 分节切换

    private void OnSectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        var index = CurrentSectionIndex();
        ShowSection(index);
    }

    private int CurrentSectionIndex()
    {
        if (navModEditorSections.SelectedItem is NavigationViewItem item
            && int.TryParse(item.Tag?.ToString(), out var index)
            && index >= 0
            && index < SectionAutomationIds.Length)
        {
            return index;
        }

        return 0;
    }

    /// <summary>只显示选中的右栏分区；同步给隐藏左栏时用的 ComboBox。</summary>
    private void ShowSection(int index)
    {
        foreach (var element in RightPaneSections())
        {
            if (element is null)
            {
                continue;
            }

            var sectionIndex = Array.IndexOf(SectionAutomationIds, element.Name);
            element.Visibility = sectionIndex == index ? Visibility.Visible : Visibility.Collapsed;
        }

        if (sectionPicker.SelectedIndex != index)
        {
            _restoringSection = true;
            sectionPicker.SelectedIndex = index;
            _restoringSection = false;
        }

        _viewModel.SelectedSectionIndex = index;
    }

    /// <summary>隐藏左栏时 ComboBox 改选也要切分区。</summary>
    private void OnPickerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || _restoringSection || sectionPicker.SelectedIndex < 0)
        {
            return;
        }

        ShowSection(sectionPicker.SelectedIndex);
    }

    private FrameworkElement?[] RightPaneSections() =>
    [
        secModEditorExport,
        secModEditorSearch,
        secModEditorTemplate,
        secModEditorLibrary,
    ];

    private void RestoreSection()
    {
        _restoringSection = true;
        ShowSection(0);
        _restoringSection = false;
    }
}
