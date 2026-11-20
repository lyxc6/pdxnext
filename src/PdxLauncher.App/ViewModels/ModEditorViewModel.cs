namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 「Mod 编辑」页的视图模型骨架（第 2 部分）：只提供 XAML 绑定所需的属性与占位命令，
/// 四个分区的所有实际逻辑（导出 / 检索 / 建模板 / 清单）留到第 3 部分接入。
/// 本类不引用 Microsoft.UI.Xaml；文件选择、目录打开与定时器派发都在页面 code-behind。
/// 全程只用本地数据，不构造 URL、不发网络请求。
/// </summary>
public sealed partial class ModEditorViewModel : LocalizedObservableObject
{
    public ModEditorViewModel()
    {
    }

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

    /// <summary>更新当前游戏与显示名（无游戏时用占位文案）。</summary>
    public void SetGame(GameInstallation? game)
    {
        Game = game;
        GameDisplayName = game?.DisplayName ?? T(Keys.SettingsGameNone);
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

    /// <summary>目录名校验提示。</summary>
    [ObservableProperty]
    private string _createFolderHint = string.Empty;

    /// <summary>能否创建模板（第 3 部分由 ModDescriptorRules.CanCreate 决定）。</summary>
    [ObservableProperty]
    private bool _canCreate;

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

    // ---------------------------------------------------------------- 占位命令（第 3 部分接入）

    /// <summary>导出脚本文档（script_docs）——第 3 部分接入。</summary>
    [RelayCommand]
    private void ExportScriptDocs() => SetStatus(Keys.ModEditorExportWaiting);

    /// <summary>导出数据类型文档（dump_data_types）——第 3 部分接入。</summary>
    [RelayCommand]
    private void ExportDumpDataTypes() => SetStatus(Keys.ModEditorExportWaiting);

    /// <summary>以 debug 参数启动当前游戏——第 3 部分接入（三参 LaunchAsync）。</summary>
    [RelayCommand]
    private void LaunchDebug() => SetStatus(Keys.ModEditorExportWaiting);

    /// <summary>停止导出监视——第 3 部分接入。</summary>
    [RelayCommand]
    private void StopWatch() => IsBusy = false;

    /// <summary>执行检索——第 3 部分接入。</summary>
    [RelayCommand]
    private void Search() => SetStatus(Keys.ModEditorSearchEmpty);

    /// <summary>选择监视目录 / 封面图（真正的 picker 在页面 code-behind）——第 3 部分接入。</summary>
    [RelayCommand]
    private void PickWatchDirectory() => SetStatus(Keys.ModEditorExportPickerUnavailable);

    /// <summary>选择封面图——第 3 部分接入。</summary>
    [RelayCommand]
    private void PickPreviewImage() => SetStatus(Keys.ModEditorExportPickerUnavailable);

    /// <summary>创建模板——第 3 部分接入（ModStudioService.Create）。</summary>
    [RelayCommand]
    private void CreateTemplate() => SetStatus(Keys.ModEditorTemplateUnsupported);

    /// <summary>刷新 Mod 清单——第 3 部分接入（ModLibraryService.LoadAsync）。</summary>
    [RelayCommand]
    private void RefreshLibrary() => SetStatus(Keys.ModEditorLibraryEmpty);
}

/// <summary>检索命中行骨架（文件 / 行号 / 片段）。</summary>
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
}

/// <summary>本地 Mod 清单行骨架（名称 / 路径 / 来源）。</summary>
public sealed partial class ModEditorLibraryRow : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private string _source = string.Empty;
}
