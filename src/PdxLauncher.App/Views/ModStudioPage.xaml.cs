namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.ModStudio;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Steam;
using Windows.Storage.Pickers;
using WinRT.Interop;

/// <summary>
/// 「Mod 创建」页：只列当前游戏的本地 mod，右侧提供详情、新建和上传三块面板。
/// 页面负责生命周期、文件选择、确认框和把后台上传进度派发回 UI；业务逻辑都在
/// <see cref="ModStudioViewModel"/> / <see cref="ModStudioService"/> 里。
/// </summary>
public sealed partial class ModStudioPage : Page
{
    private readonly ModStudioViewModel _viewModel;

    /// <summary>页面生命周期令牌；NavigationCacheMode=Required 会在返回页面时重建。</summary>
    private CancellationTokenSource _lifetime = new();

    /// <summary>当前上传的取消源；只在上传期间存在。</summary>
    private CancellationTokenSource? _publishLifetime;

    private bool _isReloading;

    /// <summary>预览图选择器允许的格式；Steam 侧只吃静态图片，视频走 YouTube 链接。</summary>
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    public ModStudioPage()
    {
        InitializeComponent();

        _viewModel = new ModStudioViewModel(
            new ModStudioService(
                AppServices.PlaySets,
                AppServices.Mods,
                AppServices.FileSystem,
                AppServices.Logger,
                new ModPublicationStore(AppServices.Documents),
                new SteamHostClient(AppServices.Logger),
                new SteamApiLocator(AppServices.FileSystem, AppServices.Logger),
                AppServices.Settings.SteamApiDllPath),
            AppServices.Logger);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ModStudioViewModel ViewModel => _viewModel;

    /// <summary>XAML 里 <c>{x:Bind L.SomeKey, Mode=OneWay}</c> 的入口。</summary>
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

            await ReloadAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModStudioPage 初始化失败", ex);
            _viewModel.SetStatus(L.T(Keys.ModstudioLoadFailed, ex.Message));
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.CurrentGameChanged -= OnCurrentGameChanged;
        _publishLifetime?.Cancel();
        _publishLifetime?.Dispose();
        _publishLifetime = null;
        _lifetime.Cancel();
    }

    private void OnCurrentGameChanged(object? sender, GameInstallation? game)
    {
        DispatcherQueue.TryEnqueue(() => _ = ReloadAsync());
    }

    private async Task ReloadAsync()
    {
        if (_isReloading || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _isReloading = true;
        try
        {
            var game = AppServices.IsInitialized ? AppServices.CurrentGame : null;
            await _viewModel.LoadAsync(game, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModStudioPage 加载失败", ex);
            _viewModel.SetStatus(L.T(Keys.ModstudioLoadFailed, ex.Message));
        }
        finally
        {
            _isReloading = false;
        }
    }

    // ------------------------------------------------------------ 顶部操作

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanCreate)
        {
            _viewModel.SetStatus(L.T(Keys.ModstudioUnsupportedCreate));
            return;
        }

        _viewModel.ShowCreate();
    }

    private void OnUploadClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanUploadSelected)
        {
            _viewModel.SetStatus(_viewModel.SelectedRow?.UploadDisabledText
                ?? L.T(Keys.ModstudioUploadSelectMod));
            return;
        }

        _viewModel.ShowUpload();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        _ = ReloadAsync();
    }

    // ------------------------------------------------------------ 新建面板

    private void OnCreateCancelClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ShowDetail();
    }

    private async void OnCreateConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        try
        {
            await _viewModel.CreateAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("创建本地 Mod 失败", ex);
            _viewModel.SetStatus(L.T(Keys.ModstudioCreateFailed, ex.Message));
        }
    }

    private async void OnPickCreatePreviewClick(object sender, RoutedEventArgs e)
    {
        var path = await PickPreviewAsync().ConfigureAwait(true);
        if (path is not null)
        {
            _viewModel.SetCreatePreview(path);
        }
    }

    // ------------------------------------------------------------ 上传面板

    private void OnUploadCancelClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsPublishing)
        {
            _publishLifetime?.Cancel();
            _viewModel.SetStatus(L.T(Keys.ModstudioUploadCanceling));
            return;
        }

        _viewModel.ShowDetail();
    }

    private async void OnUploadConfirmClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanStartUpload || _viewModel.IsPublishing || _viewModel.SelectedRow is null)
        {
            return;
        }

        var languageCount = _viewModel.UploadLanguageCount;
        var body = languageCount > 0
            ? L.T(Keys.ModstudioUploadConfirmMulti, _viewModel.SelectedRow.Name, languageCount)
            : L.T(Keys.ModstudioUploadConfirmBody, _viewModel.SelectedRow.Name);

        var confirmed = await ConfirmOverlay.ConfirmAsync(
            L.T(Keys.ModstudioUploadConfirmTitle),
            body,
            L.T(Keys.ModstudioUploadConfirm),
            L.T(Keys.ModstudioCancel)).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        _publishLifetime?.Dispose();
        _publishLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _publishLifetime.Token;

        try
        {
            await _viewModel.PublishAsync(
                progress => DispatcherQueue.TryEnqueue(() => _viewModel.ApplyProgress(progress)),
                token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _viewModel.SetStatus(L.T(Keys.ModstudioUploadCanceled));
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("上传创意工坊失败", ex);
            _viewModel.SetStatus(L.T(Keys.ModstudioUploadFailed, ex.Message));
        }
        finally
        {
            _publishLifetime?.Dispose();
            _publishLifetime = null;
        }
    }

    private async void OnPickUploadPreviewClick(object sender, RoutedEventArgs e)
    {
        var path = await PickPreviewAsync().ConfigureAwait(true);
        if (path is not null)
        {
            _viewModel.SetUploadPreview(path);
        }
    }

    private void OnLegalClick(object sender, RoutedEventArgs e)
    {
        ShellLauncher.TryOpenUrl(_viewModel.LegalUrl, "ModStudioPage");
    }

    // ------------------------------------------------------------ 详情面板

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var dir = _viewModel.SelectedRow?.DirPath;
        if (string.IsNullOrWhiteSpace(dir))
        {
            _viewModel.SetStatus(L.T(Keys.ModstudioOpenFolderUnavailable));
            return;
        }

        if (!ShellLauncher.TryStart("explorer.exe", "\"" + dir + "\"", "ModStudioPage"))
        {
            _viewModel.SetStatus(L.T(Keys.ModlibStatusCannotOpen, dir));
        }
    }

    private void OnOpenWorkshopClick(object sender, RoutedEventArgs e)
    {
        var workshopId = _viewModel.SelectedRow?.WorkshopId;
        var url = SteamWorkshop.GetBrowserAddress(workshopId);
        if (url is null)
        {
            _viewModel.SetStatus(L.T(Keys.ModstudioOpenWorkshopUnavailable));
            return;
        }

        if (!ShellLauncher.TryOpenUrl(url, "ModStudioPage"))
        {
            _viewModel.SetStatus(L.T(Keys.ModlibStatusCannotOpen, url));
        }
    }

    // ------------------------------------------------------------ 文件选择
    private static async Task<string?> PickPreviewAsync() => await PickImageAsync(multiple: false) is { Count: > 0 } paths
        ? paths[0]
        : null;

    /// <summary>打开图片选择器；允许的格式与 Steam 侧一致（不含视频文件）。</summary>
    private static async Task<IReadOnlyList<string>> PickImageAsync(bool multiple)
    {
        if (App.MainWindowInstance is null)
        {
            return Array.Empty<string>();
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
        };
        foreach (var extension in ImageExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        InitializeWithWindow.Initialize(
            picker,
            WindowNative.GetWindowHandle(App.MainWindowInstance));

        if (!multiple)
        {
            var single = await picker.PickSingleFileAsync();
            return single is null ? Array.Empty<string>() : new[] { single.Path };
        }

        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0)
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>(files.Count);
        foreach (var file in files)
        {
            paths.Add(file.Path);
        }

        return paths;
    }

    // ------------------------------------------------------------ 附加预览 / 视频 / 多语言

    private async void OnAddPreviewsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var paths = await PickImageAsync(multiple: true);
            if (paths.Count > 0)
            {
                _viewModel.AddUploadPreviews(paths);
            }
        }
        catch (Exception exception)
        {
            AppServices.Logger.Error("ModStudioPage 选择附加预览图失败", exception);
        }
    }

    private void OnRemovePreviewClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModUploadPreviewItem item })
        {
            _viewModel.RemoveUploadPreview(item);
        }
    }

    private void OnMovePreviewUpClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModUploadPreviewItem item })
        {
            _viewModel.MoveUploadPreviewUp(item);
        }
    }

    private void OnMovePreviewDownClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModUploadPreviewItem item })
        {
            _viewModel.MoveUploadPreviewDown(item);
        }
    }

    private void OnUseBuiltInCoverClick(object sender, RoutedEventArgs e) => _viewModel.UseBuiltInCover();

    private void OnAddVideoClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.AddUploadVideo(_viewModel.UploadNewVideoUrl);
        }
        catch (Exception exception)
        {
            AppServices.Logger.Error("ModStudioPage 添加视频预览失败", exception);
        }
    }

    private void OnRemoveVideoClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModUploadVideoItem item })
        {
            _viewModel.RemoveUploadVideo(item);
        }
    }

    private void OnUploadLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: SteamWorkshopLanguage language })
        {
            _viewModel.ChangeUploadLanguage(language);
        }
    }

    /// <summary>
    /// 描述框的 BBCode 工具条：把标记插到光标处，或者把选中的文字包起来。
    /// <para>
    /// 这里直接改 <c>TextBox.Text</c> 而不是走视图模型：输入框是 <c>UpdateSourceTrigger=PropertyChanged</c>
    /// 的双向绑定，写完会自己同步回 <c>UploadDescription</c>，预览也随之刷新。
    /// </para>
    /// </summary>
    private void OnBbCodeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action } || txtUgcDescription is null)
        {
            return;
        }

        var box = txtUgcDescription;

        switch (action)
        {
            case "bold":
                ApplyWrap(box, "[b]", "[/b]");
                break;
            case "italic":
                ApplyWrap(box, "[i]", "[/i]");
                break;
            case "underline":
                ApplyWrap(box, "[u]", "[/u]");
                break;
            case "strike":
                ApplyWrap(box, "[strike]", "[/strike]");
                break;
            case "code":
                ApplyWrap(box, "[code]" + Environment.NewLine, Environment.NewLine + "[/code]");
                break;
            case "link":
                // 光标停在 [url= 后面，用户直接粘链接。
                ApplyWrap(box, "[url=]", "[/url]", caretOffset: 5);
                break;
            case "image":
                ApplyWrap(box, "[img]", "[/img]", caretOffset: 5);
                break;
            case "heading":
                ApplyLineBlock(box, line => "[h2]" + line.Trim() + "[/h2]");
                break;
            case "bullets":
                ApplyList(box, "list");
                break;
            case "numbered":
                ApplyList(box, "olist");
                break;
            case "rule":
                InsertAtCaret(box, Environment.NewLine + "[hr]" + Environment.NewLine);
                break;
            default:
                return;
        }

        box.Focus(FocusState.Programmatic);
    }

    /// <summary>把选区（或光标处）用一对标签包起来，光标默认落在标签内部。</summary>
    private static void ApplyWrap(TextBox box, string open, string close, int caretOffset = -1)
    {
        var text = box.Text ?? string.Empty;
        var start = Math.Clamp(box.SelectionStart, 0, text.Length);
        var length = Math.Clamp(box.SelectionLength, 0, text.Length - start);
        var selection = text.Substring(start, length);

        var offset = caretOffset >= 0 ? Math.Min(caretOffset, open.Length) : open.Length;
        box.Text = text[..start] + open + selection + close + text[(start + length)..];
        box.SelectionStart = start + offset;
        box.SelectionLength = selection.Length;
    }

    /// <summary>把光标所在整行交给 <paramref name="transform"/> 重写。</summary>
    private static void ApplyLineBlock(TextBox box, Func<string, string> transform)
    {
        var text = box.Text ?? string.Empty;
        var caret = Math.Clamp(box.SelectionStart, 0, text.Length);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
        if (caret == 0)
        {
            lineStart = 0;
        }

        var lineEnd = text.IndexOf('\n', Math.Min(caret, text.Length));
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var line = text[lineStart..lineEnd];
        var replaced = transform(line);
        box.Text = text[..lineStart] + replaced + text[lineEnd..];
        box.SelectionStart = lineStart + replaced.Length;
        box.SelectionLength = 0;
    }

    /// <summary>把光标所在行变成一条列表项，并套上 [list] / [olist]。</summary>
    private static void ApplyList(TextBox box, string tag)
    {
        var text = box.Text ?? string.Empty;
        var caret = Math.Clamp(box.SelectionStart, 0, text.Length);
        var lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        var lineEnd = text.IndexOf('\n', Math.Min(caret, text.Length));
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var line = text[lineStart..lineEnd].Trim();

        var prefix = "[" + tag + "]" + Environment.NewLine + "[*] ";
        var block = prefix + line + Environment.NewLine + "[/" + tag + "]";
        box.Text = text[..lineStart] + block + text[lineEnd..];
        box.SelectionStart = lineStart + prefix.Length;
        box.SelectionLength = 0;
    }

    /// <summary>在光标处插入一段固定文本，光标落到插入内容之后。</summary>
    private static void InsertAtCaret(TextBox box, string snippet)
    {
        var text = box.Text ?? string.Empty;
        var caret = Math.Clamp(box.SelectionStart, 0, text.Length);
        var length = Math.Clamp(box.SelectionLength, 0, text.Length - caret);
        box.Text = text[..caret] + snippet + text[(caret + length)..];
        box.SelectionStart = caret + snippet.Length;
        box.SelectionLength = 0;
    }
}
