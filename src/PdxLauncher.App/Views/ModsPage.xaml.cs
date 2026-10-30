namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;
using Windows.System;

/// <summary>
/// 播放集页：原版启动器布局（工具条里切换播放集、条目自动保存）或留空占位。
/// 页面只负责对话框、确认与生命周期；切换即激活、添加 / 移除 / 排序 / 启停后的自动保存等
/// 业务全部在 <see cref="PlaySetEditor"/> 与 <see cref="ModsViewModel"/> 里完成。
/// </summary>
public sealed partial class ModsPage : Page
{
    private readonly ModsViewModel _viewModel;

    /// <summary>页面生命周期令牌；NavigationCacheMode=Required 会在返回页面时重建。</summary>
    private CancellationTokenSource _lifetime = new();

    /// <summary>重载协调器：连点游戏时取消旧重载，只应用最新那次的结果。</summary>
    private readonly ReloadCoordinator _reload = new();

    /// <summary>当前正在编辑位次的行；null 表示没有行处于编辑态。同一时刻只允许一行。</summary>
    private ModRowViewModel? _editingRow;

    /// <summary>
    /// 从「添加模组」子页带回来的选择结果；null 表示这次回来没有要添加的东西。
    /// 必须在页面重载完成之后再写入，否则重载会用数据库里的内容覆盖掉刚加的 mod。
    /// </summary>
    private AddModsNavigationResult? _pendingAddResult;

    public ModsPage()
    {
        InitializeComponent();

        _viewModel = new ModsViewModel(
            new PlaySetEditor(AppServices.PlaySets, AppServices.LoadFiles, AppServices.Logger),
            new ModLibraryService(AppServices.PlaySets, AppServices.Mods, AppServices.Logger),
            new ModPathRepairer(
                AppServices.FileSystem,
                AppServices.PlaySets,
                AppServices.Mods,
                AppServices.Logger),
            AppServices.Logger);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ModsViewModel ViewModel => _viewModel;

    /// <summary>XAML 的文案入口：<c>{x:Bind L.SomeKey, Mode=OneWay}</c>。</summary>
    public Localizer L => Localizer.Instance;

    /// <summary>要求打开「添加模组」子页（页面自己不持导航职责）。</summary>
    public event EventHandler<AddModsNavigationRequest>? AddModsRequested;

    /// <summary>从子页回来时带着导航参数（这里只关心添加结果）。</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is AddModsNavigationResult result)
        {
            _pendingAddResult = result;
        }
    }

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

            _viewModel.RowRevealRequested -= OnRowRevealRequested;
            _viewModel.RowRevealRequested += OnRowRevealRequested;

            if (!AppServices.IsInitialized)
            {
                await _viewModel.LoadAsync(null, _lifetime.Token);
                return;
            }

            await ReloadAsync().ConfigureAwait(true);
            await ApplyPendingAddsAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 初始化失败", ex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.CurrentGameChanged -= OnCurrentGameChanged;
        _viewModel.RowRevealRequested -= OnRowRevealRequested;

        _lifetime.Cancel();
    }

    private void OnCurrentGameChanged(object? sender, GameInstallation? game)
    {
        if (!DispatcherQueue.TryEnqueue(() => _ = ReloadAsync()))
        {
            AppServices.Logger.Warn("ModsPage 无法派发游戏切换事件");
        }
    }

    private Task ReloadAsync() => _reload.RunAsync(
        token => _viewModel.LoadAsync(AppServices.CurrentGame, token),
        _lifetime.Token,
        ex => AppServices.Logger.Error("ModsPage 加载失败", ex));

    // ------------------------------------------------------------ 播放集

    /// <summary>
    /// 工具条里的播放集切换器：只切换当前查看/编辑的播放集并加载它的 mod 行，
    /// 不改变激活状态、不写游戏加载文件；要应用请点「激活播放集」。
    /// </summary>
    private async void OnPlaySetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_reload.IsReloading)
            {
                return;
            }

            // 切换 / 自动保存过程中不接受新的选择，避免两次激活流程互相覆盖；
            // 把下拉框的选择回退到视图模型里的当前播放集。
            if (_viewModel.IsBusy || _viewModel.IsPlaceholderLayout)
            {
                if (sender is ComboBox combo)
                {
                    combo.SelectedItem = _viewModel.SelectedPlaySet;
                }

                return;
            }

            if (e.AddedItems.Count == 0 || e.AddedItems[0] is not PlaySet playSet)
            {
                return;
            }

            if (_viewModel.SelectedPlaySet?.Id == playSet.Id)
            {
                return;
            }

            await _viewModel.SwitchPlaySetAsync(playSet, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 切换播放集失败", ex);
        }
    }

    /// <summary>「激活播放集」：把当前选中集设为激活集，并把它的 mod 配置写入游戏加载文件。</summary>
    private async void OnActivatePlaySetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel.SelectedPlaySet is null)
            {
                await ShowMessageAsync(
                    Localizer.Instance.ModsActivateButton,
                    Localizer.Instance.ModsStatusSelectPlaySetFirst).ConfigureAwait(true);
                return;
            }

            await _viewModel.ActivateSelectedAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 激活播放集失败", ex);
            await ShowMessageAsync(Localizer.Instance.ModsStatusActivateFailedTitle, ex.Message).ConfigureAwait(true);
        }
    }

    private async void OnNewPlaySetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = await PromptForNameAsync(
                Localizer.Instance.ModsToolbarNewPlaySetName,
                Localizer.Instance.ModsDialogNamePlaceholder,
                Localizer.Instance.ModsDialogDefaultNewName).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            await _viewModel.CreatePlaySetAsync(name, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 新建播放集失败", ex);
            await ShowMessageAsync(Localizer.Instance.ModsStatusCreateFailedTitle, ex.Message).ConfigureAwait(true);
        }
    }

    private async void OnRenamePlaySetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel.SelectedPlaySet is null)
            {
                return;
            }

            var name = await PromptForNameAsync(
                Localizer.Instance.ModsToolbarRenameName,
                Localizer.Instance.ModsDialogNewNamePlaceholder,
                _viewModel.SelectedPlaySet.Name).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            await _viewModel.RenameSelectedAsync(name, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 重命名播放集失败", ex);
            await ShowMessageAsync(Localizer.Instance.ModsStatusRenameFailedTitle, ex.Message).ConfigureAwait(true);
        }
    }

    private async void OnDeletePlaySetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var playSet = _viewModel.SelectedPlaySet;
            if (playSet is null)
            {
                return;
            }

            var deletingActive = _viewModel.IsSelectedPlaySetActive;
            var fallback = deletingActive ? _viewModel.DeleteFallbackPlaySet : null;
            if (deletingActive && fallback is null)
            {
                await ShowMessageAsync(
                    Localizer.Instance.ModsStatusCannotDeleteTitle,
                    Localizer.Instance.ModsDialogCannotDeleteBody).ConfigureAwait(true);
                return;
            }

            var content = Localizer.Instance.T(Keys.ModsDialogDeleteBody, playSet.Name);
            if (fallback is not null)
            {
                content += Localizer.Instance.T(Keys.ModsDialogDeleteBodyFallback, fallback.Name);
            }
            else if (!deletingActive)
            {
                content += Localizer.Instance.ModsDialogDeleteBodyInactive;
            }

            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Localizer.Instance.ModsToolbarDeleteName,
                Content = content,
                PrimaryButtonText = Localizer.Instance.CommonDelete,
                CloseButtonText = Localizer.Instance.CommonCancel,
                DefaultButton = ContentDialogButton.Close,
            };

            var result = await confirm.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            await _viewModel.DeleteSelectedAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 删除播放集失败", ex);
            await ShowMessageAsync(Localizer.Instance.ModsStatusDeleteFailedTitle, ex.Message).ConfigureAwait(true);
        }
    }

    // ------------------------------------------------------------ mod 操作

    /// <summary>「添加模组」：打开子页，从 mod 库候选里多选，加入当前播放集（自动保存）。</summary>
    private async void OnAddModsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var playSet = _viewModel.SelectedPlaySet;
            if (playSet is null)
            {
                await ShowMessageAsync(
                    Localizer.Instance.ModsToolbarAddMods,
                    Localizer.Instance.ModsStatusSelectPlaySetFirst).ConfigureAwait(true);
                return;
            }

            AddModsRequested?.Invoke(
                this,
                new AddModsNavigationRequest(
                    playSet.Id,
                    playSet.Name,
                    AppServices.CurrentGame?.GameId,
                    NavigationService.PlaySetsTag));
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 打开添加模组页失败", ex);
            await ShowMessageAsync(Localizer.Instance.ModsStatusAddModsFailedTitle, ex.Message).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 应用从「添加模组」子页带回来的选择结果。
    /// </summary>
    /// <remarks>
    /// 必须在重载完成之后调用：重载会用数据库里的内容重建播放集 mod 列表，
    /// 提前写入的 mod 会被覆盖掉。
    /// </remarks>
    private async Task ApplyPendingAddsAsync()
    {
        var result = _pendingAddResult;
        if (result is null)
        {
            return;
        }

        _pendingAddResult = null;

        try
        {
            if (result.Entries.Count == 0)
            {
                return;
            }

            var target = _viewModel.PlaySets.FirstOrDefault(playSet => playSet.Id == result.PlaySetId);
            if (target is null)
            {
                await ShowMessageAsync(
                    Localizer.Instance.ModsToolbarAddMods,
                    Localizer.Instance.ModsAddPageTargetMissing).ConfigureAwait(true);
                return;
            }

            if (_viewModel.SelectedPlaySet?.Id != target.Id)
            {
                await _viewModel.SelectPlaySetAsync(target, _lifetime.Token).ConfigureAwait(true);
            }

            _viewModel.AddMods(result.Entries);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 应用添加模组结果失败", ex);
            await ShowMessageAsync(Localizer.Instance.ModsStatusAddModsFailedTitle, ex.Message).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 搜索框实时过滤。x:Bind 不支持 UpdateSourceTrigger，所以 Text 只做 OneWay 绑定，
    /// 由这里把用户输入写回 ViewModel（写回会触发 RebuildVisibleMods）。
    /// </summary>
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            if (sender is not TextBox box)
            {
                return;
            }

            var text = box.Text ?? string.Empty;
            if (string.Equals(text, _viewModel.SearchText, StringComparison.Ordinal))
            {
                return;
            }

            _viewModel.SearchText = text;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 搜索过滤失败", ex);
        }
    }

    private void OnEnableAllClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.SetAllEnabled(true);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 启用全部失败", ex);
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
            AppServices.Logger.Error("ModsPage 禁用全部失败", ex);
        }
    }

    // ------------------------------------------------------------ 排序编号就地编辑

    /// <summary>
    /// 点击序号进入编辑态。同一时刻只允许一行在编辑：先结束上一行，再置位新行。
    /// </summary>
    private void OnPositionEditClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: ModRowViewModel row })
            {
                return;
            }

            EndPositionEdit();

            row.IsEditingPosition = true;
            _editingRow = row;
            FocusPositionEditor(row);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 进入位次编辑失败", ex);
        }
    }

    /// <summary>
    /// 输入框的显隐由 IsEditingPosition 绑定，翻转后要等布局跑完才真正可见，
    /// 所以排到 DispatcherQueue 里再取文字、抢焦点、全选。
    /// 这是本仓库第一处程序化聚焦（改动前 grep <c>Focus(FocusState</c> 为零命中），
    /// 用在不可见控件上会静默失败，因此必须延后。
    /// </summary>
    private void FocusPositionEditor(ModRowViewModel row, string? text = null)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var box = FindPositionEditor(row);
                if (box is null)
                {
                    return;
                }

                box.Text = text ?? row.PositionText;
                box.Focus(FocusState.Programmatic);
                box.SelectAll();
            }
            catch (Exception ex)
            {
                AppServices.Logger.Error("ModsPage 聚焦位次输入框失败", ex);
            }
        }))
        {
            AppServices.Logger.Warn("ModsPage 无法派发位次输入框聚焦");
        }
    }

    /// <summary>
    /// 按 ModId 找到该行的序号输入框。条目容器会被回收复用，不能靠 x:Name 取控件；
    /// 行模板里只有这一个 TextBox（序号输入框），所以按类型找是唯一且稳定的。
    /// </summary>
    private TextBox? FindPositionEditor(ModRowViewModel row) =>
        ModList.ContainerFromItem(row) is ListViewItem { ContentTemplateRoot: { } root }
            ? FindDescendant<TextBox>(root)
            : null;

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>回车提交：把输入框原文交给视图模型判定（解析、钳位、搜索态都在那边统一处理）。</summary>
    private void OnPositionEditKeyDown(object sender, KeyRoutedEventArgs e)
    {
        try
        {
            if (e.Key is not (VirtualKey.Enter or VirtualKey.Escape) || sender is not TextBox box)
            {
                return;
            }

            e.Handled = true;

            // 以发送者所在行为准：_editingRow 只是「谁在编辑」的记录，重排会让行对象换人，
            // 直接从控件的 DataContext 取才是当前这一行的真身。
            var row = box.DataContext as ModRowViewModel ?? _editingRow;
            if (row is null)
            {
                return;
            }

            // Esc 一律放弃。
            if (e.Key == VirtualKey.Escape)
            {
                EndPositionEdit();
                return;
            }

            // 回车才把原文交出去。注意顺序：非法输入时不能先退出再重进编辑态——
            // 那样输入框会「隐藏又显示」，其自身失焦事件随即把刚恢复的编辑态又关掉。
            // 所以先判定，只有不该继续编辑时才退出。
            if (_viewModel.TryMoveModToPosition(row, box.Text) == PositionEditResult.Invalid)
            {
                FocusPositionEditor(row, box.Text);
                return;
            }

            EndPositionEdit();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 提交位次编辑失败", ex);
        }
    }

    /// <summary>失焦（点别处、Tab 走、窗口切走）一律按取消处理，不做隐式提交。</summary>
    private void OnPositionEditLostFocus(object sender, RoutedEventArgs e) => EndPositionEdit();

    /// <summary>收回编辑态并清掉跟踪；重复调用安全。</summary>
    private void EndPositionEdit()
    {
        var row = _editingRow;
        _editingRow = null;

        if (row is null)
        {
            return;
        }

        try
        {
            row.IsEditingPosition = false;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 退出位次编辑失败", ex);
        }
    }

    /// <summary>
    /// 排序后把目标行滚进视野。重排会重建所有行对象（ObservableCollection.Reset），
    /// 虚拟化列表会因此回到顶部，不主动定位用户就看不到结果。
    /// </summary>
    private void OnRowRevealRequested(Guid modId)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var row = _viewModel.Mods.FirstOrDefault(m => m.ModId == modId);
                if (row is not null)
                {
                    ModList.ScrollIntoView(row, ScrollIntoViewAlignment.Default);
                }
            }
            catch (Exception ex)
            {
                AppServices.Logger.Error("ModsPage 滚动到目标行失败", ex);
            }
        }))
        {
            AppServices.Logger.Warn("ModsPage 无法派发滚动到目标行");
        }
    }

    /// <summary>
    /// 「修复 mod 路径」：先扫出可修项（订阅 mod 缺路径 / 本地 mod 路径含非 ASCII），
    /// 让用户逐项确认并就地改名，再落实并把加载文件重写一遍。
    /// </summary>
    private async void OnRepairModPathsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var repairs = await ViewModel.FindPathRepairsAsync(CancellationToken.None);
            if (repairs.Count == 0)
            {
                await ShowMessageAsync(Localizer.Instance.ModsRepairName, Localizer.Instance.ModsRepairNothing);
                return;
            }

            var confirmed = await ModPathRepairDialog.ShowAsync(XamlRoot, repairs);
            if (confirmed.Count == 0)
            {
                return;
            }

            await ViewModel.ApplyPathRepairsAsync(confirmed, CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 修复 mod 路径失败", ex);
        }
    }

    // ------------------------------------------------------------ 对话框

    private async Task<string?> PromptForNameAsync(string title, string placeholder, string initial)
    {
        var input = new TextBox
        {
            Text = initial,
            PlaceholderText = placeholder,
            SelectionStart = 0,
            SelectionLength = initial.Length,
            MinWidth = 280,
        };
        AutomationProperties.SetAutomationId(input, "txtPlaySetName");
        AutomationProperties.SetName(input, placeholder);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = input,
            PrimaryButtonText = Localizer.Instance.CommonOk,
            CloseButtonText = Localizer.Instance.CommonCancel,
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        return input.Text;
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
                CloseButtonText = Localizer.Instance.CommonClose,
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 显示对话框失败", ex);
        }
    }

}
