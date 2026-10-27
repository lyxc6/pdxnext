namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;

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

    public ModsPage()
    {
        InitializeComponent();

        _viewModel = new ModsViewModel(
            new PlaySetEditor(AppServices.PlaySets, AppServices.LoadFiles, AppServices.Logger),
            new ModLibraryService(AppServices.PlaySets, AppServices.Mods, AppServices.Logger),
            AppServices.Logger);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ModsViewModel ViewModel => _viewModel;

    /// <summary>XAML 的文案入口：<c>{x:Bind L.SomeKey, Mode=OneWay}</c>。</summary>
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
                await _viewModel.LoadAsync(null, _lifetime.Token);
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
            AppServices.Logger.Error("ModsPage 初始化失败", ex);
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

    /// <summary>「添加模组」：从 mod 库候选里多选，加入当前播放集（自动保存）。</summary>
    private async void OnAddModsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel.SelectedPlaySet is null)
            {
                await ShowMessageAsync(
                    Localizer.Instance.ModsToolbarAddMods,
                    Localizer.Instance.ModsStatusSelectPlaySetFirst).ConfigureAwait(true);
                return;
            }

            var candidates = await _viewModel
                .GetAddCandidateModsAsync(_lifetime.Token)
                .ConfigureAwait(true);
            if (candidates.Count == 0)
            {
                await ShowMessageAsync(
                    Localizer.Instance.ModsToolbarAddMods,
                    Localizer.Instance.ModsDialogNoAddableMods).ConfigureAwait(true);
                return;
            }

            var options = new List<AddModOption>(candidates.Count);
            foreach (var entry in candidates)
            {
                options.Add(new AddModOption(entry));
            }

            var selected = new HashSet<AddModOption>();
            var restoringSelection = false;

            var search = new TextBox
            {
                PlaceholderText = Localizer.Instance.ModsAddDialogSearchPlaceholder,
                MinWidth = 420,
            };
            AutomationProperties.SetAutomationId(search, "txtAddModsSearch");
            AutomationProperties.SetName(search, Localizer.Instance.ModsAddDialogSearchName);

            var list = new ListView
            {
                SelectionMode = ListViewSelectionMode.Multiple,
                MinWidth = 420,
                MinHeight = 220,
                MaxHeight = 360,
                ItemsSource = options,
            };
            AutomationProperties.SetAutomationId(list, "lstAddModsCandidates");
            AutomationProperties.SetName(list, Localizer.Instance.ModsAddDialogListName);

            list.SelectionChanged += (_, _) =>
            {
                if (restoringSelection)
                {
                    return;
                }

                selected.Clear();
                foreach (var item in list.SelectedItems.OfType<AddModOption>())
                {
                    selected.Add(item);
                }
            };

            search.TextChanged += (_, _) =>
            {
                var query = (search.Text ?? string.Empty).Trim();
                var next = query.Length == 0
                    ? options
                    : options.FindAll(option => option.Matches(query));

                restoringSelection = true;
                try
                {
                    list.ItemsSource = next;
                    foreach (var option in next)
                    {
                        if (selected.Contains(option))
                        {
                            list.SelectedItems.Add(option);
                        }
                    }
                }
                finally
                {
                    restoringSelection = false;
                }
            };

            var panel = new Grid
            {
                RowSpacing = 8,
                MinWidth = 420,
            };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(search, 0);
            Grid.SetRow(list, 1);
            panel.Children.Add(search);
            panel.Children.Add(list);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Localizer.Instance.ModsToolbarAddMods,
                Content = panel,
                PrimaryButtonText = Localizer.Instance.ModsAddDialogAdd,
                CloseButtonText = Localizer.Instance.CommonCancel,
                DefaultButton = ContentDialogButton.Primary,
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            var chosen = new List<ModLibraryEntry>(selected.Count);
            foreach (var option in selected)
            {
                chosen.Add(option.Entry);
            }

            _viewModel.AddMods(chosen);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModsPage 添加模组失败", ex);
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

    /// <summary>「添加模组」对话框里的一行：包一层显示文本，避免 ListView 直接显示 record 的 ToString。</summary>
    private sealed class AddModOption
    {
        public AddModOption(ModLibraryEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            Entry = entry;

            var size = ModsViewModel.FormatSize(entry.SizeBytes);
            Display = string.IsNullOrWhiteSpace(size)
                ? entry.Name
                : entry.Name + "  ·  " + size;
        }

        public ModLibraryEntry Entry { get; }

        public string Display { get; }

        /// <summary>按名称 / 注册 id 过滤（大小不参与匹配，避免输入 "1" 命中一堆行）。</summary>
        public bool Matches(string query)
        {
            return Entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || Entry.GameRegistryId.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString() => Display;
    }
}
