namespace PdxLauncher.App.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.ModStudio;

/// <summary>
/// 「Mod 编辑」页的视图模型：四个分区（导出 / 检索 / 建模板 / 清单）的编排都在这里，
/// 具体的路径/检索/模板判定复用 <see cref="ModDefinitionPaths"/> / <see cref="ModDefinitionSearch"/> /
/// <see cref="ModTemplateBuilder"/>。本类不引用 Microsoft.UI.Xaml：文件选择、目录打开与定时器由页面
/// code-behind 负责（通过事件回call）。全程只用本地数据，不构造 URL、不发网络请求。
/// </summary>
public sealed partial class ModEditorViewModel : LocalizedObservableObject
{
    private readonly IFileSystem _fileSystem = AppServices.FileSystem;

    /// <summary>导出流程的取消源（停止 / 卸载时释放）。</summary>
    private CancellationTokenSource? _exportCts;

    /// <summary>当前导出类别；null 表示未在监视。</summary>
    private ModDefinitionPaths.ExportKind? _activeExportKind;

    /// <summary>导出落盘根目录（已校验落在 DataRoot 内）。</summary>
    private string _exportDestination = string.Empty;

    /// <summary>轮询中记录的文件大小：path → 上一次观测到的字节数。</summary>
    private readonly Dictionary<string, long> _seenSizes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>轮询中记录的连续稳定次数：path → 大小未变的轮询次数。</summary>
    private readonly Dictionary<string, int> _stableCounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已经拷贝过的文件，避免同一文件反复导入。</summary>
    private readonly HashSet<string> _copiedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>本次导出已拷贝的文件数。</summary>
    private int _copiedCount;

    /// <summary>页面需要启动 1 秒轮询定时器（导出开始时）。</summary>
    public event Action? WatchStarted;

    /// <summary>页面需要停止轮询定时器（停止 / 导出取消时）。</summary>
    public event Action? WatchStopped;

    /// <summary>页面需要弹出「选择监视目录」选择器。</summary>
    public event Action? PickWatchDirectoryRequested;

    /// <summary>页面需要弹出「选择封面图」选择器。</summary>
    public event Action? PickPreviewImageRequested;

    // ---------------------------------------------------------------- 页头 / 分节

    /// <summary>当前游戏（只读展示）。</summary>
    [ObservableProperty]
    private GameInstallation? _game;

    /// <summary>当前游戏显示名（无游戏时为占位文案）。</summary>
    [ObservableProperty]
    private string _gameDisplayName = string.Empty;

    /// <summary>左栏显隐：直接读写用户设置，切换时立即落盘并广播（与设置页双向即时生效）。</summary>
    public bool IsLeftPaneVisible
    {
        get => AppServices.Settings.ModEditorLeftPaneVisible;
        set
        {
            if (AppServices.Settings.ModEditorLeftPaneVisible == value)
            {
                return;
            }

            AppServices.Settings.ModEditorLeftPaneVisible = value;
            if (!AppServices.SaveSettings().IsSuccess)
            {
                AppServices.Settings.ModEditorLeftPaneVisible = !value;
                OnPropertyChanged();
                SetStatus(Keys.SettingsStatusSaveFailed, string.Empty);
                return;
            }

            OnPropertyChanged();
            AppServices.NotifyModEditorPaneVisibilityChanged();
        }
    }

    /// <summary>设置页改了左栏显隐后，把绑定值拉回最新（页面订阅 AppServices 广播后调用）。</summary>
    public void RefreshLeftPaneVisibility() => OnPropertyChanged(nameof(IsLeftPaneVisible));

    /// <summary>更新当前游戏与显示名；游戏变化时顺手刷新模板可用性与清单。</summary>
    public void SetGame(GameInstallation? game)
    {
        Game = game;
        GameDisplayName = game?.DisplayName ?? T(Keys.SettingsGameNone);
        RefreshTemplateState();
    }

    /// <summary>当前分区序号（0..3，对应左栏四项 / 右栏四个 StackPanel）。</summary>
    [ObservableProperty]
    private int _selectedSectionIndex;

    // ---------------------------------------------------------------- 一、导出区

    /// <summary>监视目录（用户可粘贴绝对路径）。</summary>
    [ObservableProperty]
    private string _watchDirectory = string.Empty;

    /// <summary>debug 启动参数（默认 -debug，用户可编辑）。</summary>
    [ObservableProperty]
    private string _debugArg = "-debug";

    /// <summary>导出流程是否占用（按钮禁用 + 进度环）。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>导出状态文案。</summary>
    [ObservableProperty]
    private string _exportStatus = string.Empty;

    /// <summary>由选择器回填监视目录。</summary>
    public void SetWatchDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            WatchDirectory = path.Trim();
        }
    }

    // ---------------------------------------------------------------- 二、检索区

    /// <summary>检索词。</summary>
    [ObservableProperty]
    private string _query = string.Empty;

    /// <summary>检索命中集合（行：文件 / 行号 / 片段）。</summary>
    public ObservableCollection<ModEditorSearchRow> SearchHits { get; } = new();

    /// <summary>命中数文案（空词时为提示文案）。</summary>
    [ObservableProperty]
    private string _searchResultCount = string.Empty;

    // ---------------------------------------------------------------- 三、模板区

    /// <summary>模板名称。</summary>
    [ObservableProperty]
    private string _templateName = string.Empty;

    /// <summary>模板目录名。</summary>
    [ObservableProperty]
    private string _templateFolder = string.Empty;

    /// <summary>模板版本。</summary>
    [ObservableProperty]
    private string _templateVersion = string.Empty;

    /// <summary>支持版本。</summary>
    [ObservableProperty]
    private string _templateSupported = string.Empty;

    /// <summary>标签（逗号分隔）。</summary>
    [ObservableProperty]
    private string _templateTags = string.Empty;

    /// <summary>简介。</summary>
    [ObservableProperty]
    private string _templateDescription = string.Empty;

    /// <summary>封面图路径。</summary>
    [ObservableProperty]
    private string _templatePreviewPath = string.Empty;

    /// <summary>目录名校验提示（键，由 XAML 侧本地化；这里存翻译后的文本）。</summary>
    [ObservableProperty]
    private string _createFolderHint = string.Empty;

    /// <summary>能否创建模板（由 ModDescriptorRules.CanCreate + 表单字段决定）。</summary>
    [ObservableProperty]
    private bool _canCreate;

    /// <summary>由选择器回填封面图路径。</summary>
    public void SetPreviewImage(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            TemplatePreviewPath = path.Trim();
        }
    }

    partial void OnTemplateNameChanged(string value) => RefreshTemplateState();

    partial void OnTemplateFolderChanged(string value) => RefreshTemplateState();

    /// <summary>按当前游戏与表单字段刷新「能否创建」与目录名提示。</summary>
    public void RefreshTemplateState()
    {
        var gameId = Game?.GameId;
        CanCreate = ModTemplateBuilder.CanCreate(gameId, TemplateName, TemplateFolder);
        CreateFolderHint = T(ModTemplateBuilder.FolderHintKey(gameId, TemplateFolder));
    }

    // ---------------------------------------------------------------- 四、清单区

    /// <summary>本地 Mod 清单行。</summary>
    public ObservableCollection<ModEditorLibraryRow> LibraryRows { get; } = new();

    /// <summary>清单统计文案。</summary>
    [ObservableProperty]
    private string _libraryCount = string.Empty;

    // ---------------------------------------------------------------- 底部状态条

    /// <summary>底部状态栏文案。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>状态栏是否显示。</summary>
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    private void SetStatus(string key, params object?[] args) => StatusMessage = T(key, args);

    /// <summary>供页面 code-behind 写底部状态栏（shell 动作失败等）。</summary>
    public void SetStatusFromPage(string key, params object?[] args) => SetStatus(key, args);

    private void SetExportStatus(string key, params object?[] args) => ExportStatus = T(key, args);

    // ---------------------------------------------------------------- 一、导出流程

    /// <summary>导出脚本文档（script_docs）：debug 启动 + 开始监视，用户在游戏内输入命令。</summary>
    [RelayCommand]
    private void ExportScriptDocs() => BeginExport(ModDefinitionPaths.ExportKind.ScriptDocs);

    /// <summary>导出数据类型文档（dump_data_types）：debug 启动 + 开始监视。</summary>
    [RelayCommand]
    private void ExportDumpDataTypes() => BeginExport(ModDefinitionPaths.ExportKind.DataTypes);

    /// <summary>仅以 debug 参数启动当前游戏（不开始监视）。</summary>
    [RelayCommand]
    private async Task LaunchDebugAsync()
    {
        var game = Game;
        if (game is null)
        {
            SetStatus(Keys.ModEditorExportNoGame);
            return;
        }

        if (!game.SupportsContentManagement)
        {
            SetStatus(Keys.ModEditorExportUnsupportedGame);
            return;
        }

        await LaunchDebugCoreAsync(game).ConfigureAwait(true);
    }

    private void BeginExport(ModDefinitionPaths.ExportKind kind)
    {
        var game = Game;
        if (game is null)
        {
            SetStatus(Keys.ModEditorExportNoGame);
            return;
        }

        if (!game.SupportsContentManagement)
        {
            SetStatus(Keys.ModEditorExportUnsupportedGame);
            return;
        }

        // 用户没填就按游戏数据目录派生一个默认候选；用户可随时在文本框里改成真实输出目录。
        var watch = WatchDirectory;
        if (string.IsNullOrWhiteSpace(watch))
        {
            watch = game.GameDataDir;
            WatchDirectory = watch;
        }

        try
        {
            if (!_fileSystem.DirectoryExists(watch))
            {
                SetStatus(Keys.ModEditorExportWatchDirInvalid);
                return;
            }

            var destination = ModDefinitionPaths.DestinationRoot(AppServices.DataRoot, game.GameId, kind);
            if (!ModDefinitionPaths.IsUnderRoot(destination, AppServices.DataRoot))
            {
                SetStatus(Keys.ModEditorExportFailed);
                return;
            }

            _exportDestination = destination;
            _activeExportKind = kind;
            _seenSizes.Clear();
            _stableCounts.Clear();
            _copiedPaths.Clear();
            _copiedCount = 0;
            _exportCts?.Dispose();
            _exportCts = new CancellationTokenSource();

            IsBusy = true;
            SetExportStatus(Keys.ModEditorExportWatching);
            WatchStarted?.Invoke();

            // debug 启动失败只提示，不中止监视：用户仍可能已手动开好了游戏控制台。
            _ = LaunchDebugCoreAsync(game);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModEditorPage 开始导出失败", ex);
            SetStatus(Keys.ModEditorExportFailed);
        }
    }

    /// <summary>三参 LaunchAsync：自定义参数原样透传，取消令牌来自导出 CTS。</summary>
    private async Task LaunchDebugCoreAsync(GameInstallation game)
    {
        try
        {
            var args = string.IsNullOrWhiteSpace(DebugArg)
                ? Array.Empty<string>()
                : new[] { DebugArg.Trim() };
            var request = new LaunchRequest(null, args, false);
            var token = _exportCts?.Token ?? CancellationToken.None;
            var result = await AppServices.Launcher
                .LaunchAsync(game, request, token)
                .ConfigureAwait(true);
            if (!result.Started)
            {
                SetStatus(Keys.ModEditorExportFailed);
            }
        }
        catch (OperationCanceledException) when (_exportCts?.IsCancellationRequested == true)
        {
            // 用户停止导出导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModEditorPage debug 启动失败", ex);
            SetStatus(Keys.ModEditorExportFailed);
        }
    }

    /// <summary>页面 1 秒轮询调用：发现稳定大小的新文件就拷贝；未监视时是廉价 no-op。</summary>
    public void Tick()
    {
        if (!IsBusy || _activeExportKind is null)
        {
            return;
        }

        try
        {
            var kind = _activeExportKind.Value;
            var files = _fileSystem.EnumerateFiles(WatchDirectory, "*", recursive: false);
            foreach (var path in files)
            {
                if (_copiedPaths.Contains(path)
                    || ModDefinitionPaths.MatchKind(_fileSystem.GetFileName(path)) != kind)
                {
                    continue;
                }

                var size = _fileSystem.GetFileSize(path);
                var previous = _seenSizes.TryGetValue(path, out var last) ? last : -1;
                if (size > 0 && size == previous)
                {
                    var stable = _stableCounts.TryGetValue(path, out var count) ? count : 0;
                    stable++;
                    _stableCounts[path] = stable;
                    if (stable >= 2)
                    {
                        CopyImportedFile(path);
                    }
                }
                else
                {
                    _seenSizes[path] = size;
                    _stableCounts[path] = 0;
                }
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("ModEditorPage 轮询导出目录失败: " + ex.Message);
        }
    }

    /// <summary>把一个稳定的文件拷进 DataRoot 内的目标目录（overwrite=true 便于重导）。</summary>
    private void CopyImportedFile(string source)
    {
        try
        {
            _fileSystem.EnsureDirectory(_exportDestination);
            var fileName = _fileSystem.GetFileName(source);
            var destination = _fileSystem.Combine(_exportDestination, fileName);
            if (!ModDefinitionPaths.IsUnderRoot(destination, AppServices.DataRoot))
            {
                AppServices.Logger.Warn("ModEditorPage 拒绝越界拷贝: " + destination);
                return;
            }

            _fileSystem.Copy(source, destination, overwrite: true);
            _copiedPaths.Add(source);
            _copiedCount++;
            AppServices.Logger.Info("ModEditorPage 导入导出文件: " + source + " -> " + destination);
            SetExportStatus(Keys.ModEditorExportCopied, _copiedCount, _exportDestination);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("ModEditorPage 拷贝导出文件失败: " + ex.Message);
            SetStatus(Keys.ModEditorExportFailed);
        }
    }

    /// <summary>停止导出：停表、释放 CTS、清状态（页面卸载也走这里）。</summary>
    [RelayCommand]
    private void StopWatch()
    {
        if (!IsBusy && _exportCts is null)
        {
            return;
        }

        try
        {
            _exportCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已释放，忽略。
        }

        _exportCts?.Dispose();
        _exportCts = null;
        _activeExportKind = null;
        IsBusy = false;
        SetExportStatus(Keys.ModEditorExportWaiting);
        WatchStopped?.Invoke();
    }

    /// <summary>导出目标目录（供页面「打开目录」按钮使用）。</summary>
    public string ExportDestination => _exportDestination;

    // ---------------------------------------------------------------- 二、检索

    [RelayCommand]
    private void Search()
    {
        var game = Game;
        if (game is null)
        {
            SetStatus(Keys.ModEditorSearchNoGame);
            return;
        }

        SearchHits.Clear();
        var keyword = Query;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            SearchResultCount = T(Keys.ModEditorSearchEmpty);
            return;
        }

        try
        {
            var files = CollectImportedFiles(game);
            if (files.Count == 0)
            {
                SearchResultCount = T(Keys.ModEditorSearchNoDocs);
                return;
            }

            var index = ModDefinitionSearch.BuildIndex(files, _fileSystem);
            var hits = ModDefinitionSearch.Search(index, keyword);
            var openLabel = T(Keys.ModEditorSearchOpenFile);
            foreach (var hit in hits)
            {
                SearchHits.Add(new ModEditorSearchRow
                {
                    FileName = _fileSystem.GetFileName(hit.Path),
                    LineNumber = hit.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Snippet = hit.LineText,
                    Path = hit.Path,
                    OpenFileLabel = openLabel,
                });
            }

            SearchResultCount = T(Keys.ModEditorSearchResultCount, hits.Count);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("ModEditorPage 检索失败: " + ex.Message);
            SetStatus(Keys.ModEditorSearchFailed);
        }
    }

    /// <summary>收集当前游戏已导入的脚本文档 / 数据类型文档。</summary>
    private IReadOnlyList<string> CollectImportedFiles(GameInstallation game)
    {
        var result = new List<string>();
        foreach (var kind in new[] { ModDefinitionPaths.ExportKind.ScriptDocs, ModDefinitionPaths.ExportKind.DataTypes })
        {
            var directory = ModDefinitionPaths.DestinationRoot(AppServices.DataRoot, game.GameId, kind);
            if (!_fileSystem.DirectoryExists(directory))
            {
                continue;
            }

            foreach (var file in _fileSystem.EnumerateFiles(directory, "*", recursive: true))
            {
                result.Add(file);
            }
        }

        return result;
    }

    /// <summary>检索结果行对应的文件路径，供页面「打开文件」使用。</summary>
    public string? HitPath(ModEditorSearchRow row) => row?.Path;

    // ---------------------------------------------------------------- 三、建模板

    [RelayCommand]
    private void CreateTemplate()
    {
        var game = Game;
        if (game is null || !CanCreate)
        {
            SetStatus(Keys.ModEditorTemplateUnsupported);
            return;
        }

        try
        {
            var request = ModTemplateBuilder.BuildRequest(
                TemplateName, TemplateFolder, TemplateVersion, TemplateSupported,
                TemplateTags, TemplateDescription, TemplatePreviewPath);
            var result = ModCreator.Create(game, request, _fileSystem);
            if (!result.Ok)
            {
                SetStatus(Keys.ModEditorTemplateFailed, result.ErrorCode ?? T(Keys.CommonUnknown));
                return;
            }

            if (AppServices.Mods is IScanCacheInvalidator invalidator)
            {
                invalidator.InvalidateCache(game);
            }

            AppServices.Logger.Info("ModEditorPage 已创建模板: " + (result.DirPath ?? request.FolderName));
            SetStatus(Keys.ModEditorTemplateSuccess, TemplateName.Trim());
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModEditorPage 创建模板失败", ex);
            SetStatus(Keys.ModEditorTemplateFailed, ex.Message);
        }
    }

    // ---------------------------------------------------------------- 四、清单

    [RelayCommand]
    private async Task RefreshLibraryAsync()
    {
        var game = Game;
        if (game is null)
        {
            SetStatus(Keys.ModEditorLibraryNoGame);
            return;
        }

        try
        {
            var service = new ModLibraryService(AppServices.PlaySets, AppServices.Mods, AppServices.Logger);
            var snapshot = await service.LoadAsync(game, CancellationToken.None).ConfigureAwait(true);
            var rows = ModLibraryListBuilder.Build(snapshot);
            LibraryRows.Clear();
            foreach (var row in rows)
            {
                LibraryRows.Add(new ModEditorLibraryRow
                {
                    Name = row.Name,
                    Path = row.Path,
                    Source = T(row.SourceKey),
                });
            }

            LibraryCount = T(Keys.ModEditorLibraryCount, rows.Count);
            if (rows.Count == 0)
            {
                SetStatus(Keys.ModEditorLibraryEmpty);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("ModEditorPage 刷新清单失败: " + ex.Message);
            SetStatus(Keys.ModEditorLibraryFailed);
        }
    }

    /// <summary>清单目录（供页面「打开目录」按钮使用）：当前游戏 mod 目录。</summary>
    public string? LibraryFolder => Game?.ModDir;

    // ---------------------------------------------------------------- 选择器事件转发

    [RelayCommand]
    private void PickWatchDirectory() => PickWatchDirectoryRequested?.Invoke();

    [RelayCommand]
    private void PickPreviewImage() => PickPreviewImageRequested?.Invoke();
}

/// <summary>检索命中行（文件 / 行号 / 片段 / 完整路径）。</summary>
public sealed partial class ModEditorSearchRow : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _lineNumber = string.Empty;

    [ObservableProperty]
    private string _snippet = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    /// <summary>「打开文件」按钮文案（由 VM 本地化后填入，避免 DataTemplate 里的 x:Bind L.*）。</summary>
    [ObservableProperty]
    private string _openFileLabel = string.Empty;
}

/// <summary>本地 Mod 清单行（名称 / 路径 / 来源）。</summary>
public sealed partial class ModEditorLibraryRow : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private string _source = string.Empty;
}
