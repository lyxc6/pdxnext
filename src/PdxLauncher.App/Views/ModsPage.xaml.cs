namespace PdxLauncher.App.Views;

using System.Globalization;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;
using Windows.System;

/// <summary>
/// 播放集页：原版启动器布局（工具条里切换播放集、条目自动保存）、双列布局
/// （左栏勾选即加入、右栏编辑当前播放集）或留空占位。三种布局共用同一套工具条、
/// 保存状态与行模板；页面只负责对话框、确认与生命周期，切换即激活、添加 / 移除 / 排序 /
/// 启停后的自动保存等业务全部在 <see cref="PlaySetEditor"/> 与 <see cref="ModsViewModel"/> 里完成。
/// </summary>
public sealed partial class ModsPage : Page
{
    /// <summary>双列左栏的初始宽度（DIP）。XAML 里 ColumnDefinition 的 Width 初值必须与它一致。</summary>
    private const double DefaultLeftColumnWidth = ColumnResize.DefaultWidth;

    private readonly ModsViewModel _viewModel;

    /// <summary>
    /// 用户想要的双列左栏宽度（DIP），只在拖动分隔条与双击复位时改写。
    /// 窗口拉窄时渲染宽度会被压小，但这个意图值保持不变：窗口拉回去后左栏能回到用户拖出来的宽度，
    /// 不会被缩放过程永久改小。页面 NavigationCacheMode=Required，切页回来仍是拖过的宽度；
    /// 没有写进 settings，是为了不为一个纯视觉宽度动设置 schema。
    /// </summary>
    private double _preferredLeftColumnWidth = ColumnResize.DefaultWidth;

    /// <summary>按下分隔条那一刻的左栏宽度；拖动过程中的宽度 = 它 + 指针相对按下点的总位移。</summary>
    private double _dragStartLeftColumnWidth = ColumnResize.DefaultWidth;

    /// <summary>页面生命周期令牌；NavigationCacheMode=Required 会在返回页面时重建。</summary>
    private CancellationTokenSource _lifetime = new();

    /// <summary>
    /// 进行中的自动排序扫描的取消源（链到 _lifetime）。null 表示没有扫描在跑；
    /// 它同时充当「别开第二份扫描」的门。
    /// </summary>
    private CancellationTokenSource? _autoSortCts;

    /// <summary>重载协调器：连点游戏时取消旧重载，只应用最新那次的结果。</summary>
    private readonly ReloadCoordinator _reload = new();

    /// <summary>当前正在编辑位次的行；null 表示没有行处于编辑态。同一时刻只允许一行。</summary>
    private ModRowViewModel? _editingRow;

    /// <summary>
    /// 从「添加模组」子页带回来的选择结果；null 表示这次回来没有要添加的东西。
    /// 必须在页面重载完成之后再写入，否则重载会用数据库里的内容覆盖掉刚加的 mod。
    /// </summary>
    private AddModsNavigationResult? _pendingAddResult;

    /// <summary>
    /// 当前布局里真正承载「播放集内 mod」的列表：原版启动器是 ModList，双列是右栏的
    /// ModListTwoColumn。位次编辑与滚动定位都按这一栏取容器，两套布局共用同一段逻辑。
    /// </summary>
    private ListView ActiveModList => _viewModel.IsTwoColumnLayout ? ModListTwoColumn : ModList;

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

        // 分隔条的事件用代码接线：PdxColumnSplitter 的 Dragged/ResetRequested 是普通事件，
        // 不是依赖属性，XAML 编译器绑不上（也没有对应的 RoutedEvent）。
        ColumnSplitter.DragStarted += OnSplitterDragStarted;
        ColumnSplitter.Dragged += OnSplitterDragged;
        ColumnSplitter.ResetRequested += OnSplitterReset;

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
    private async void OnPlaySetSelectionChanged(object sender, PdxDropDownSelectionChangedEventArgs e)
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
                if (sender is PdxDropDown dropdown)
                {
                    dropdown.SelectedItem = _viewModel.SelectedPlaySet;
                }

                return;
            }

            if (e.SelectedItem is not PlaySet playSet)
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

            var confirmed = await ConfirmOverlay.ConfirmAsync(
                Localizer.Instance.ModsToolbarDeleteName,
                content,
                Localizer.Instance.CommonDelete,
                Localizer.Instance.CommonCancel,
                initialFocus: InitialFocus.Cancel);
            if (!confirmed)
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

    // ------------------------------------------------------------ 导入 / 导出（官方分享格式）

    /// <summary>
    /// 「导入播放集」：选官方分享 .json → 本地已有同名播放集时询问合并还是新建副本 →
    /// 导入 → 结果框（缺失的 Steam 模组可一键订阅）。
    /// </summary>
    private async void OnImportPlaySetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await FilePickers.PickOpenJsonAsync();
            if (path is null)
            {
                return;
            }

            var peek = await _viewModel.PeekImportAsync(path, _lifetime.Token).ConfigureAwait(true);
            if (!peek.IsSuccess)
            {
                await ShowMessageAsync(
                    Localizer.Instance.ModsImportErrorTitle,
                    PlaySetShareText.DescribeError(Localizer.Instance, peek.ErrorMessage)).ConfigureAwait(true);
                return;
            }

            if (peek.Value is not { } peeked)
            {
                return;
            }

            Guid? mergeInto = null;
            if (peeked.ExistingPlaySetId is { } existingId)
            {
                var merge = await ConfirmOverlay.ConfirmAsync(
                    Localizer.Instance.ModsImportSameNameTitle,
                    Localizer.Instance.T(
                        Keys.ModsImportSameNameContent,
                        peeked.ExistingPlaySetName ?? peeked.Name),
                    Localizer.Instance.ModsImportMergeConfirm,
                    Localizer.Instance.ModsImportMergeNewCopy).ConfigureAwait(true);
                if (merge)
                {
                    mergeInto = existingId;
                }
            }

            var imported = await _viewModel
                .ImportPlaySetAsync(path, mergeInto, _lifetime.Token)
                .ConfigureAwait(true);
            if (!imported.IsSuccess)
            {
                await ShowMessageAsync(
                    Localizer.Instance.ModsImportErrorTitle,
                    PlaySetShareText.DescribeError(Localizer.Instance, imported.ErrorMessage)).ConfigureAwait(true);
                return;
            }

            if (imported.Value is not { } importedValue)
            {
                return;
            }

            var canSubscribe = AppServices.Settings.SteamWorkshopSubscribeEnabled
                && SteamWorkshopBackends.CreateForSubscribe(_viewModel.Game) is { IsAvailable: true };
            var subscribe = await PlaySetImportDialog
                .ShowAsync(XamlRoot, importedValue, canSubscribe)
                .ConfigureAwait(true);
            if (subscribe)
            {
                await _viewModel
                    .SubscribeMissingSteamModsAsync(importedValue.Missing, _lifetime.Token)
                    .ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 导入播放集失败", ex);
            await ShowMessageAsync(Localizer.Instance.ModsImportErrorTitle, ex.Message).ConfigureAwait(true);
        }
    }

    /// <summary>「导出播放集」：先落盘自动保存，再按官方分享格式写 .json；有排除项时弹说明框。</summary>
    private async void OnExportPlaySetClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var playSet = _viewModel.SelectedPlaySet;
            if (playSet is null)
            {
                return;
            }

            var path = await FilePickers.PickSaveJsonAsync(playSet.Name);
            if (path is null)
            {
                return;
            }

            var exported = await _viewModel
                .ExportSelectedAsync(path, _lifetime.Token)
                .ConfigureAwait(true);
            if (!exported.IsSuccess)
            {
                await ShowMessageAsync(
                    Localizer.Instance.ModsExportErrorTitle,
                    PlaySetShareText.DescribeError(Localizer.Instance, exported.ErrorMessage)).ConfigureAwait(true);
                return;
            }

            if (exported.Value is not { } exportedValue)
            {
                return;
            }

            if (exportedValue.LocalExcluded > 0 || exportedValue.UnsupportedNames.Count > 0)
            {
                await PlaySetExportNoticeDialog.ShowAsync(XamlRoot, exportedValue).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 导出播放集失败", ex);
            await ShowMessageAsync(Localizer.Instance.ModsExportErrorTitle, ex.Message).ConfigureAwait(true);
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
    /// 建议只跟"手打"走：程序化改 Text 也会触发 TextChanged（比如原版/双列两个框共用同一个
    /// SearchText，一者回写另一者就跟着变），那时弹下拉就是和用户抢焦点。
    /// </summary>
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        try
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }

            var text = sender.Text ?? string.Empty;
            if (!string.Equals(text, _viewModel.SearchText, StringComparison.Ordinal))
            {
                _viewModel.SearchText = text;
            }

            SearchSuggestBox.Update(sender, _viewModel.Mods.Select(mod => mod.Name), text);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 搜索过滤失败", ex);
        }
    }

    /// <summary>回车 / 放大镜 / 选中建议：过滤用的就是当前文本，收起下拉即可。</summary>
    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        try
        {
            _viewModel.SearchText = (args.QueryText ?? string.Empty).Trim();
            sender.IsSuggestionListOpen = false;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 搜索过滤失败", ex);
        }
    }

    /// <summary>
    /// 双列左栏的搜索框：只过滤「可添加的 mod」，和右栏的 SearchText 互不影响。
    /// 与 <see cref="OnSearchTextChanged"/> 一样，x:Bind 只能 OneWay，输入由这里写回。
    /// </summary>
    private void OnCandidateSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        try
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }

            var text = sender.Text ?? string.Empty;
            if (!string.Equals(text, _viewModel.CandidateSearchText, StringComparison.Ordinal))
            {
                _viewModel.CandidateSearchText = text;
            }

            SearchSuggestBox.Update(sender, _viewModel.Candidates.Select(row => row.Name), text);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 候选搜索过滤失败", ex);
        }
    }

    /// <summary>双列左栏搜索框的提交（回车 / 放大镜 / 选中建议）：收起下拉即可。</summary>
    private void OnCandidateSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        try
        {
            _viewModel.CandidateSearchText = (args.QueryText ?? string.Empty).Trim();
            sender.IsSuggestionListOpen = false;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 候选搜索过滤失败", ex);
        }
    }

    /// <summary>
    /// 双列左栏「勾选即加入」：勾上候选行就把这个 mod 追加到当前播放集末尾（走自动保存）。
    /// 加入延后一帧派发：此刻仍在 CheckBox 的 Click 里，而加入会把这行从 Candidates 里摘掉，
    /// 在绑定回调中途移除「正在求值的这一行」容易让列表控件丢掉容器状态。
    /// </summary>
    private void OnCandidateChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not CheckBox { DataContext: AddModRowViewModel row }
                || !row.IsSelected
                || _viewModel.IsBusy)
            {
                return;
            }

            var entry = row.Entry;
            if (!DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    _viewModel.AddMods(new[] { entry });
                }
                catch (Exception ex)
                {
                    AppServices.Logger.Error("ModsPage 加入候选 mod 失败", ex);
                }
            }))
            {
                AppServices.Logger.Warn("ModsPage 无法派发候选 mod 加入");
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 处理候选 mod 勾选失败", ex);
        }
    }

    // ------------------------------------------------------------ 双列分栏

    /// <summary>
    /// 拖动分隔条：<see cref="PdxColumnSplitter"/> 给的是**相对按下点**的总位移，
    /// 所以这里用拖动开始时的宽度加上位移，而不是在旧值上累加增量——
    /// 重复投递的移动事件、或控件自身被挤动，都不会让宽度越推越大。
    /// </summary>
    private void OnSplitterDragged(object? sender, double offset)
    {
        var available = TwoColumnGrid.ActualWidth;
        _preferredLeftColumnWidth = ColumnResize.Clamp(
            _dragStartLeftColumnWidth + offset,
            available);
        ApplyLeftColumnWidth(available);
    }

    /// <summary>记住按下时的宽度：拖动过程中的宽度全部由「起点宽度 + 总位移」算出。</summary>
    private void OnSplitterDragStarted(object? sender, EventArgs e)
    {
        _dragStartLeftColumnWidth = _preferredLeftColumnWidth;
    }

    /// <summary>双击分隔条复位到默认宽度：拖窄之后想一次回到能看见完整名字的状态。</summary>
    private void OnSplitterReset(object? sender, EventArgs e)
    {
        _preferredLeftColumnWidth = ColumnResize.DefaultWidth;
        ApplyLeftColumnWidth(TwoColumnGrid.ActualWidth);
    }

    /// <summary>
    /// 窗口尺寸变化时重新钳一次。左栏是像素宽度，窗口拉窄时右栏会先被挤掉，
    /// 所以宽度上限必须随总宽重算。
    /// </summary>
    /// <remarks>
    /// 这里必须用事件自带的 <see cref="SizeChangedEventArgs.NewSize"/>，不能读
    /// <c>TwoColumnGrid.ActualWidth</c>：在 SizeChanged 处理里 ActualWidth 还是上一次 arrange 的旧值，
    /// 拿旧总宽去钳制会算回和原来一样的宽度，于是「值没变」早退、左栏不让位，
    /// 右栏被挤成负宽、整张 Grid 撑出窗口——用户缩小窗口后看到的右栏被裁就是这个原因。
    /// </remarks>
    private void OnTwoColumnGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyLeftColumnWidth(e.NewSize.Width);
    }

    /// <summary>
    /// 按可用宽度把左栏渲染到「意图宽度」钳制后的值（钳制规则见 <see cref="ColumnResize.Clamp"/>）。
    /// 不写回 <see cref="_preferredLeftColumnWidth"/>：窗口变窄只是临时让位，不是用户改了宽度。
    /// </summary>
    private void ApplyLeftColumnWidth(double availableWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            // 布局还没测量：什么都不做，否则会被当成「窗口太窄」直接压到 0。
            return;
        }

        var clamped = ColumnResize.Clamp(_preferredLeftColumnWidth, availableWidth);

        // 值没变就别写：SizeChanged 会反复进来，每次写 Width 都白排一次版。
        if (TwoColumnLeftColumn.Width.GridUnitType == GridUnitType.Pixel
            && Math.Abs(TwoColumnLeftColumn.Width.Value - clamped) < 0.5)
        {
            return;
        }

        TwoColumnLeftColumn.Width = new GridLength(clamped);

        if (Math.Abs(clamped - _preferredLeftColumnWidth) >= 0.5)
        {
            // 只有「让位」时才记：拖动过程中 clamped 恒等于期望宽度，不会刷日志。
            AppServices.Logger.Info(
                $"双列分栏：总宽 {availableWidth:0.#}，左栏让位到 {clamped:0.#}（期望 {_preferredLeftColumnWidth:0.#}）");
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
    /// 所以排到 DispatcherQueue 里再写值、抢焦点。
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

                // NumberBox 的真源是 Value；用户刚敲的非法原文解析不出数字时回退到当前位次，
                // 保持编辑态等着重新输入（ValidationMode 也会把非法文本覆盖成上一个有效值）。
                box.Value = ParseEditorValue(text) ?? row.Position + 1;
                box.Focus(FocusState.Programmatic);
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

    /// <summary>把输入框原文解析成 NumberBox 能接收的 double；空串 / 非法 / NaN 返回 null。</summary>
    private static double? ParseEditorValue(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && !double.IsNaN(value)
            && !double.IsInfinity(value)
                ? value
                : null;
    }

    /// <summary>
    /// 按 ModId 找到该行的序号输入框。条目容器会被回收复用，不能靠 x:Name 取控件；
    /// 行模板里只有这一个 NumberBox（序号输入框），所以按类型找是唯一且稳定的。
    /// </summary>
    private NumberBox? FindPositionEditor(ModRowViewModel row) =>
        ActiveModList.ContainerFromItem(row) is ListViewItem { ContentTemplateRoot: { } root }
            ? FindDescendant<NumberBox>(root)
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
            if (e.Key is not (VirtualKey.Enter or VirtualKey.Escape) || sender is not NumberBox box)
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

            // 回车才把值交出去。注意顺序：非法输入时不能先退出再重进编辑态——
            // 那样输入框会「隐藏又显示」，其自身失焦事件随即把刚恢复的编辑态又关掉。
            // 所以先判定，只有不该继续编辑时才退出。
            // NumberBox 空着或输入被覆盖成 NaN 时无从提交，回退到当前位次、保持编辑态。
            if (double.IsNaN(box.Value) || double.IsInfinity(box.Value))
            {
                FocusPositionEditor(row);
                return;
            }

            // 文本层仍是最终判据：小数、0、负数等照样交给 PositionInput 拒绝。
            var text = box.Value.ToString(CultureInfo.InvariantCulture);
            if (_viewModel.TryMoveModToPosition(row, text) == PositionEditResult.Invalid)
            {
                FocusPositionEditor(row, text);
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

    // ------------------------------------------------------------ 行右键菜单

    /// <summary>在资源管理器里打开 mod 的本地位置：目录型打开目录，压缩包型定位到压缩包。</summary>
    private void OnRowOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: ModRowViewModel row })
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(row.DirPath))
            {
                if (ShellLauncher.TryStart("explorer.exe", "\"" + row.DirPath + "\"", "ModsPage 打开 mod 文件夹"))
                {
                    _viewModel.ReportOpenedFolder(row);
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(row.ArchivePath))
            {
                if (ShellLauncher.TryStart("explorer.exe", "/select,\"" + row.ArchivePath + "\"", "ModsPage 定位 mod 压缩包"))
                {
                    _viewModel.ReportOpenedFolder(row);
                }

                return;
            }

            _viewModel.ReportNoLocalFiles();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 打开 mod 本地位置失败", ex);
        }
    }

    /// <summary>用 steam:// 协议在 Steam 客户端里打开该 mod 的创意工坊页面。</summary>
    private void OnRowOpenSteamClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: ModRowViewModel row })
            {
                return;
            }

            var address = SteamWorkshop.GetSteamAddress(
                SteamWorkshop.GetWorkshopId(row.SteamId, row.GameRegistryId));
            if (address is null)
            {
                _viewModel.ReportNoWorkshopAddress();
                return;
            }

            ShellLauncher.TryOpenSteam(address, "ModsPage 打开 mod 创意工坊");
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 打开 mod 创意工坊失败", ex);
        }
    }

    /// <summary>把注册 ID 写进剪贴板，并在状态栏确认（状态栏才有 mod 名，菜单里不放名字）。</summary>
    private void OnRowCopyIdClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: ModRowViewModel row })
            {
                return;
            }

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(row.GameRegistryId);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            _viewModel.ReportClipboardCopied(row);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 复制 mod 注册 ID 失败", ex);
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
                    ActiveModList.ScrollIntoView(row, ScrollIntoViewAlignment.Default);
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

    /// <summary>
    /// 「自动排序」：按文件改动足迹预览新顺序，用户确认后应用并留一次撤销入口。
    /// 扫描与页面生命周期、取消按钮共用一个令牌；离开页面即停。
    /// </summary>
    private async void OnAutoSortClick(object sender, RoutedEventArgs e)
    {
        if (_autoSortCts is not null)
        {
            return;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _autoSortCts = cts;
        try
        {
            var preview = await ViewModel.BuildAutoSortPreviewAsync(cts.Token, ViewModel.CreateAutoSortProgress());
            if (preview is null)
            {
                return;
            }

            var moved = preview.Result.Entries.Count(entry => entry.OldIndex != entry.NewIndex);
            if (moved == 0)
            {
                await ShowMessageAsync(Localizer.Instance.ModsAutoSortPreviewTitle, Localizer.Instance.ModsAutoSortNoChange);
                return;
            }

            var confirmed = await AutoSortPreviewDialog.ShowAsync(XamlRoot, preview, ViewModel.CurrentPlaySetMods);
            if (confirmed)
            {
                // 预览到确认之间列表可能被自动保存或刷新改过；Apply 会自行拒绝过期预览。
                ViewModel.ApplyAutoSort(preview);
            }
        }
        catch (OperationCanceledException)
        {
            // 用户点了取消或已离开页面：状态栏由视图模型写成「已取消」。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 自动排序失败", ex);
        }
        finally
        {
            _autoSortCts = null;
            cts.Dispose();
        }
    }

    /// <summary>「取消排序」：只取消扫描，不改动列表。</summary>
    private void OnAutoSortCancelClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _autoSortCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 扫描恰好已经结束并释放了取消源：没有可取消的工作。
        }
    }

    /// <summary>
    /// 「撤销排序」：恢复应用自动排序前的顺序（快照只在内存，换播放集即作废）。
    /// </summary>
    private void OnUndoAutoSortClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.UndoAutoSort();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 撤销自动排序失败", ex);
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

        var confirmed = await ConfirmOverlay.ConfirmAsync(
            title,
            input,
            Localizer.Instance.CommonOk,
            Localizer.Instance.CommonCancel);
        if (!confirmed)
        {
            return null;
        }

        return input.Text;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        try
        {
            await ConfirmOverlay.ShowMessage(title, message, Localizer.Instance.CommonClose);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 显示对话框失败", ex);
        }
    }

}
