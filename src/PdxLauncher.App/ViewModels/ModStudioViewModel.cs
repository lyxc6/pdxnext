namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.ModStudio;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Steam;
using PdxLauncher.Core.Text;

/// <summary>「Mod 创建」页右侧当前显示哪一块。</summary>
public enum ModStudioPane
{
    /// <summary>选中 mod 的详情。</summary>
    Detail,

    /// <summary>新建本地 mod 表单。</summary>
    Create,

    /// <summary>上传到创意工坊表单。</summary>
    Upload,
}

/// <summary>
/// 「Mod 创建」页的视图模型：只列本地 mod，负责搜索、选中、新建表单与上传表单。
/// 本类不引用 Microsoft.UI.Xaml；文件选择、确认框和进度派发都在页面 code-behind。
/// </summary>
public sealed partial class ModStudioViewModel : LocalizedObservableObject
{
    private readonly ModStudioService _service;
    private readonly IAppLogger _logger;

    private IReadOnlyList<ModStudioEntry> _entries = Array.Empty<ModStudioEntry>();
    private bool _managedByGame;
    private string? _lastAutoFolder;
    private UploadDraftNoticeKind _uploadDraftNoticeKind;
    private DateTimeOffset? _uploadDraftAppliedAt;

    /// <summary>上传草稿提示的状态。</summary>
    private enum UploadDraftNoticeKind
    {
        None,
        Applied,
        Missing,
        Outdated,
    }

    /// <summary>当前游戏。</summary>
    [ObservableProperty]
    private GameInstallation? _game;

    /// <summary>是否正在加载。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>是否完成过一次加载。</summary>
    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>状态栏文案。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>搜索词。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>当前选中的本地 mod。</summary>
    [ObservableProperty]
    private ModStudioRowViewModel? _selectedRow;

    /// <summary>右侧面板。</summary>
    [ObservableProperty]
    private ModStudioPane _pane = ModStudioPane.Detail;

    /// <summary>是否正在上传。</summary>
    [ObservableProperty]
    private bool _isPublishing;

    /// <summary>上传进度百分比。</summary>
    [ObservableProperty]
    private double _progressPercent;

    /// <summary>上传进度文案。</summary>
    [ObservableProperty]
    private string _progressText = string.Empty;

    /// <summary>上次操作是否被 Steam 法律协议拦住。</summary>
    [ObservableProperty]
    private bool _needsLegalAgreement;

    // ---------------------------------------------------------------- 新建表单

    /// <summary>新 mod 名称。</summary>
    [ObservableProperty]
    private string _createName = string.Empty;

    /// <summary>新 mod 文件夹名。</summary>
    [ObservableProperty]
    private string _createFolderName = string.Empty;

    /// <summary>新 mod 版本。</summary>
    [ObservableProperty]
    private string _createVersion = "0.1.0";

    /// <summary>新 mod 支持的游戏版本。</summary>
    [ObservableProperty]
    private string _createSupportedVersion = string.Empty;

    /// <summary>新 mod 标签（逗号分隔）。</summary>
    [ObservableProperty]
    private string _createTags = string.Empty;

    /// <summary>新 mod 简介。</summary>
    [ObservableProperty]
    private string _createDescription = string.Empty;

    /// <summary>新 mod 封面源文件。</summary>
    [ObservableProperty]
    private string? _createPreviewPath;

    // ---------------------------------------------------------------- 上传表单

    /// <summary>工坊标题。</summary>
    [ObservableProperty]
    private string _uploadTitle = string.Empty;

    /// <summary>工坊简介。</summary>
    [ObservableProperty]
    private string _uploadDescription = string.Empty;

    /// <summary>工坊标签（逗号分隔）。</summary>
    [ObservableProperty]
    private string _uploadTags = string.Empty;

    /// <summary>更新说明。</summary>
    [ObservableProperty]
    private string _uploadChangeNote = string.Empty;

    /// <summary>工坊封面。</summary>
    [ObservableProperty]
    private string? _uploadPreviewPath;

    /// <summary>是否公开。</summary>
    [ObservableProperty]
    private bool _uploadPublic = true;

    /// <summary>上传目标；默认 Steam。</summary>
    [ObservableProperty]
    private ModUploadTarget _uploadTarget = ModUploadTarget.Steam;

    /// <summary>Steam 工坊条目 id 文本；空表示新建。</summary>
    [ObservableProperty]
    private string _uploadModId = string.Empty;

    /// <summary>当前选中 mod 的本地目录，只读展示。</summary>
    [ObservableProperty]
    private string _uploadDirectory = string.Empty;

    /// <summary>工坊条目可见性（Steam 四档）。</summary>
    [ObservableProperty]
    private SteamWorkshopVisibility _uploadVisibility = SteamWorkshopVisibility.Public;

    /// <summary>内容描述词：常见成人内容（AnyMatureContent）。</summary>
    [ObservableProperty]
    private bool _uploadDescriptorAdult;

    /// <summary>内容描述词：频繁出现暴力或血腥（FrequentViolenceOrGore）。</summary>
    [ObservableProperty]
    private bool _uploadDescriptorViolence;

    /// <summary>内容描述词：一些裸露画面或色情内容（NudityOrSexualContent）。</summary>
    [ObservableProperty]
    private bool _uploadDescriptorNudity;

    /// <summary>内容描述词：频繁的裸露画面或色情内容（GratuitousSexualContent）。</summary>
    [ObservableProperty]
    private bool _uploadDescriptorFrequentNudity;

    /// <summary>内容描述词：仅限成人的色情内容（AdultOnlySexualContent）。</summary>
    [ObservableProperty]
    private bool _uploadDescriptorAdultOnly;

    /// <summary>
    /// 当前正在编辑的语言。默认 english，也就是 Steam 工坊条目的默认标题与描述。
    /// </summary>
    [ObservableProperty]
    private SteamWorkshopLanguage? _uploadSelectedLanguage;

    /// <summary>语言下拉的可选项。</summary>
    public IReadOnlyList<SteamWorkshopLanguage> UploadLanguageOptions => SteamWorkshopLanguages.All;

    /// <summary>尚未填标题、但已经有内容的其余语言数量（界面上给个提示）。</summary>
    public string UploadExtraLanguageSummary
    {
        get
        {
            var count = _localizedDrafts.Count(pair => IsFilled(pair.Value) && !IsDefaultLanguage(pair.Key));
            return count == 0 ? string.Empty : T(Keys.ModstudioLocalizationCount, count);
        }
    }

    /// <summary>输入框里的新视频链接。</summary>
    [ObservableProperty]
    private string _uploadNewVideoUrl = string.Empty;

    /// <summary>构造。</summary>
    public ModStudioViewModel(ModStudioService service, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(logger);
        _service = service;
        _logger = logger;
    }

    /// <summary>当前可见的本地 mod 行。</summary>
    public ObservableCollection<ModStudioRowViewModel> Mods { get; } = new();

    /// <summary>上传面板的可选标签。</summary>
    public ObservableCollection<ModTagOption> UploadTagOptions { get; } = new();

    /// <summary>上传面板的附加预览图（不含主封面）。</summary>
    public ObservableCollection<ModUploadPreviewItem> UploadPreviewItems { get; } = new();

    /// <summary>上传面板的 YouTube 视频预览。</summary>
    public ObservableCollection<ModUploadVideoItem> UploadVideoItems { get; } = new();

    /// <summary>
    /// 各语言的标题与描述（键为归一化语言码）。
    /// <para>
    /// 表单上只显示当前语言的一对标题/描述，切语言时从这里读写，
    /// 这样不必在界面上铺一堆语言行，和 Steam 客户端的「编辑标题与描述」一致。
    /// </para>
    /// </summary>
    private readonly Dictionary<string, ModLocalizedText> _localizedDrafts = new(StringComparer.Ordinal);

    /// <summary>可见性下拉的可选项。</summary>
    public IReadOnlyList<VisibilityOption> VisibilityOptions { get; } = new[]
    {
        new VisibilityOption(SteamWorkshopVisibility.Public, Keys.ModstudioVisibilityPublic),
        new VisibilityOption(SteamWorkshopVisibility.FriendsOnly, Keys.ModstudioVisibilityFriendsOnly),
        new VisibilityOption(SteamWorkshopVisibility.Private, Keys.ModstudioVisibilityPrivate),
        new VisibilityOption(SteamWorkshopVisibility.Unlisted, Keys.ModstudioVisibilityUnlisted),
    };

    /// <summary>可见性下拉的选中项（绑定 ComboBox.SelectedItem 用）。</summary>
    public VisibilityOption? SelectedVisibilityOption
    {
        get => VisibilityOptions.FirstOrDefault(option => option.Value == UploadVisibility);
        set
        {
            if (value is not null)
            {
                UploadVisibility = value.Value;
            }
        }
    }

    /// <summary>游戏名。</summary>
    public string GameTitle => Game?.DisplayName ?? T(Keys.ModstudioNoGameTitle);

    /// <summary>是否有游戏。</summary>
    public bool HasGame => Game is not null;

    /// <summary>是否能创建 mod。</summary>
    public bool CanCreate => Game is { SupportsContentManagement: true } && ModDescriptorRules.CanCreate(Game.GameId);

    /// <summary>选中行是否能上传。</summary>
    public bool CanUploadSelected => CanPublishGame && SelectedRow?.CanUpload == true;

    /// <summary>当前游戏是否有创意工坊。</summary>
    public bool CanPublishGame => Game is { SupportsContentManagement: true } && ModDescriptorRules.CanPublish(Game.GameId);

    /// <summary>详情面板是否可见。</summary>
    public bool IsDetailPane => Pane == ModStudioPane.Detail;

    /// <summary>新建面板是否可见。</summary>
    public bool IsCreatePane => Pane == ModStudioPane.Create;

    /// <summary>上传面板是否可见。</summary>
    public bool IsUploadPane => Pane == ModStudioPane.Upload;

    /// <summary>空状态：没有游戏。</summary>
    public bool ShowNoGameHint => IsLoaded && !HasGame;

    /// <summary>空状态：游戏只支持启动，内容由游戏本体管理。</summary>
    public bool ShowManagedHint => IsLoaded && Game is { SupportsContentManagement: false };

    /// <summary>提示：游戏完整适配，但不支持在本启动器创建 mod。</summary>
    public bool ShowUnsupportedCreateHint
        => IsLoaded && Game is { SupportsContentManagement: true } && !ModDescriptorRules.CanCreate(Game.GameId);

    /// <summary>提示：没有可用的 Steam 创意工坊。</summary>
    public bool ShowNoWorkshopHint
        => IsLoaded && Game is { SupportsContentManagement: true } && !ModDescriptorRules.CanPublish(Game.GameId);

    /// <summary>空状态：该游戏还没有本地 mod。</summary>
    public bool ShowNoModsHint
        => IsLoaded && CanCreate && Mods.Count == 0;

    /// <summary>是否需要空状态面板。</summary>
    public bool ShowEmptyState
        => ShowNoGameHint || ShowManagedHint || ShowNoModsHint;

    /// <summary>描述符家族说明。</summary>
    public string DescriptorHint => Game is null
        ? string.Empty
        : T(ModDescriptorRules.DescribeKey(ModDescriptorRules.FamilyFor(Game.GameId)));

    /// <summary>选中行的名称。</summary>
    public string SelectedName => SelectedRow?.Name ?? T(Keys.ModstudioDetailNoSelection);

    /// <summary>选中行的目录。</summary>
    public string SelectedFolder => SelectedRow?.DirPath ?? string.Empty;

    /// <summary>选中行的版本。</summary>
    public string SelectedVersion => SelectedRow?.VersionText ?? string.Empty;

    /// <summary>选中行的支持版本。</summary>
    public string SelectedSupportedVersion => SelectedRow?.SupportedVersionText ?? string.Empty;

    /// <summary>选中行的标签。</summary>
    public string SelectedTags => SelectedRow?.TagsText ?? string.Empty;

    /// <summary>选中行的简介。</summary>
    public string SelectedDescription => SelectedRow?.Descriptor?.Description ?? string.Empty;

    /// <summary>选中行的描述符说明。</summary>
    public string SelectedDescriptor => SelectedRow?.DescriptorText ?? string.Empty;

    /// <summary>选中行的发布状态。</summary>
    public string SelectedPublication => SelectedRow?.PublicationText ?? string.Empty;

    /// <summary>是否选中了一行。</summary>
    public bool HasSelection => SelectedRow is not null;

    /// <summary>选中行是否可以打开工坊页面。</summary>
    public bool CanOpenSelectedWorkshop => SelectedRow?.CanOpenWorkshop == true;

    /// <summary>选中行是否有本地目录可打开（归档 mod 没有目录）。</summary>
    public bool CanOpenSelectedFolder => SelectedRow?.DirPath is { Length: > 0 };

    /// <summary>选中行是否有可编辑的描述符文件。</summary>
    public bool CanEditSelectedDescriptor => SelectedRow?.CanEditDescriptor == true;

    /// <summary>是否已有发布记录。</summary>
    public bool HasSelectedPublication => SelectedRow?.HasPublication == true;

    /// <summary>选中行在 Steam 侧的事实可见性文案；没核实过显示「未核实」。</summary>
    public string SelectedSteamVisibility => SelectedRow?.Publication?.SteamVisibility switch
    {
        0 => T(Keys.ModstudioVisibilityPublic),
        1 => T(Keys.ModstudioVisibilityFriendsOnly),
        2 => T(Keys.ModstudioVisibilityPrivate),
        3 => T(Keys.ModstudioVisibilityUnlisted),
        _ => T(Keys.ModstudioVisibilityUnknown),
    };

    /// <summary>是否从 Steam 核实过可见性（决定详情面板那一行要不要显示）。</summary>
    public bool HasSelectedSteamVisibility => SelectedRow?.Publication?.SteamVisibility is not null;

    /// <summary>能否绑定一个已存在的工坊条目；与上传同口径，唯一硬性前提是有本地目录。</summary>
    public bool CanBindWorkshop => CanPublishGame && !IsPublishing && SelectedRow?.DirPath is { Length: > 0 };

    /// <summary>绑定入口的说明文案。</summary>
    public string BindWorkshopHint => T(Keys.ModstudioBindHint);

    /// <summary>上传按钮是否可用（上传中或选择 Paradox 目标时禁用）。</summary>
    public bool CanStartUpload => !IsPublishing && UploadTarget == ModUploadTarget.Steam;

    /// <summary>上传表单是否可编辑（上传过程中禁用）。</summary>
    public bool CanEditUploadFields => !IsPublishing;

    /// <summary>Steam 工坊条目 id 提示。</summary>
    public string UploadModIdHint => T(Keys.ModstudioUploadModIdHint);

    /// <summary>Paradox Mod 目标暂不可用。</summary>
    public bool CanUploadToParadox => false;

    /// <summary>当前目标是否为 Steam。</summary>
    public bool IsSteamTarget
    {
        get => UploadTarget == ModUploadTarget.Steam;
        set
        {
            if (value)
            {
                UploadTarget = ModUploadTarget.Steam;
            }
        }
    }

    /// <summary>当前目标是否为 Paradox Mod。</summary>
    public bool IsParadoxTarget
    {
        get => UploadTarget == ModUploadTarget.Paradox;
        set
        {
            if (value)
            {
                UploadTarget = ModUploadTarget.Paradox;
            }
        }
    }

    /// <summary>当前上传目标说明。</summary>
    public string UploadTargetHint => UploadTarget == ModUploadTarget.Paradox
        ? T(Keys.ModstudioUploadTargetPdxUnavailable)
        : string.Empty;

    /// <summary>已选标签计数文案。</summary>
    public string UploadSelectedTagCountText => T(
        Keys.ModstudioUploadTagCount,
        UploadTagOptions.Count(option => option.IsSelected));

    /// <summary>AI 草稿提示文案；没有提示时为空串。</summary>
    public string UploadDraftNotice => _uploadDraftNoticeKind switch
    {
        UploadDraftNoticeKind.Applied when _uploadDraftAppliedAt is { } appliedAt =>
            T(Keys.ModstudioMcpDraftApplied, appliedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
        UploadDraftNoticeKind.Missing => T(Keys.ModstudioMcpDraftNone),
        UploadDraftNoticeKind.Outdated => T(Keys.ModstudioMcpDraftOutdated),
        _ => string.Empty,
    };

    /// <summary>是否有 AI 草稿提示。</summary>
    public bool HasUploadDraftNotice => _uploadDraftNoticeKind != UploadDraftNoticeKind.None;

    /// <summary>新建表单的文件夹提示。</summary>
    public string CreateFolderHintText => T(Keys.ModstudioCreateFolderHint);

    /// <summary>上传表单里当前 mod 的名称。</summary>
    public string UploadModName => SelectedRow?.Name ?? string.Empty;

    /// <summary>上传表单里当前 mod 的目录。</summary>
    public string UploadDirectoryText => UploadDirectory;

    /// <summary>上传提示。</summary>
    public string UploadHintText => T(Keys.ModstudioUploadHint);

    /// <summary>新建封面的显示文案。</summary>
    public string CreatePreviewText => string.IsNullOrWhiteSpace(CreatePreviewPath)
        ? T(Keys.ModstudioPreviewNone)
        : Path.GetFileName(CreatePreviewPath);

    /// <summary>上传封面的显示文案。</summary>
    public string UploadPreviewText => string.IsNullOrWhiteSpace(UploadPreviewPath)
        ? T(Keys.ModstudioPreviewNone)
        : Path.GetFileName(UploadPreviewPath);

    /// <summary>法律协议地址。</summary>
    public string LegalUrl => "https://steamcommunity.com/sharedfiles/workshoplegalagreement";

    /// <summary>加载本地 mod 清单。</summary>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken, bool resetPane = true)
    {
        Game = game;
        _entries = Array.Empty<ModStudioEntry>();
        _managedByGame = game?.SupportsContentManagement == false;

        if (game is null)
        {
            Mods.Clear();
            SelectedRow = null;
            IsLoaded = true;
            Pane = ModStudioPane.Detail;
            StatusMessage = T(Keys.ModstudioNoGameHint);
            RaiseDerived();
            return;
        }

        if (_managedByGame || !game.SupportsContentManagement)
        {
            Mods.Clear();
            SelectedRow = null;
            IsLoaded = true;
            Pane = ModStudioPane.Detail;
            StatusMessage = T(Keys.ModstudioManagedHint);
            RaiseDerived();
            return;
        }

        var selectedPath = SelectedRow?.DirPath;
        IsBusy = true;
        try
        {
            _entries = await _service.LoadAsync(game, cancellationToken).ConfigureAwait(true);
            RebuildRows(selectedPath);
            IsLoaded = true;
            if (resetPane)
            {
                Pane = ModStudioPane.Detail;
            }

            if (SelectedRow is null && Mods.Count > 0)
            {
                SelectedRow = Mods[0];
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _entries = Array.Empty<ModStudioEntry>();
            Mods.Clear();
            SelectedRow = null;
            IsLoaded = true;
            StatusMessage = T(Keys.ModstudioLoadFailed, exception.Message);
            _logger.Error("Mod 创建页加载失败（gameId=" + game.GameId + "）", exception);
        }
        finally
        {
            IsBusy = false;
            RaiseDerived();
        }
    }

    /// <summary>刷新当前游戏。</summary>
    public Task RefreshAsync(CancellationToken cancellationToken) => LoadAsync(Game, cancellationToken, resetPane: false);

    /// <summary>切到新建面板，并填入按当前游戏推导的默认值。</summary>
    public void ShowCreate()
    {
        if (!CanCreate)
        {
            StatusMessage = T(Keys.ModstudioUnsupportedCreate);
            return;
        }

        Pane = ModStudioPane.Create;
        CreateVersion = string.IsNullOrWhiteSpace(CreateVersion) ? "0.1.0" : CreateVersion;
        CreateSupportedVersion = string.IsNullOrWhiteSpace(CreateSupportedVersion) || CreateSupportedVersion == "1.*"
            ? SuggestSupportedVersion(Game?.Version)
            : CreateSupportedVersion;
        RaiseDerived();
    }

    /// <summary>切到详情面板。</summary>
    public void ShowDetail()
    {
        Pane = ModStudioPane.Detail;
        RaiseDerived();
    }

    /// <summary>切到上传面板；选中的 mod 不可上传时只更新状态栏。</summary>
    public void ShowUpload()
    {
        if (SelectedRow is null)
        {
            StatusMessage = T(Keys.ModstudioUploadSelectMod);
            return;
        }

        if (!SelectedRow.CanUpload)
        {
            StatusMessage = SelectedRow.UploadDisabledText;
            return;
        }

        // 进入上传面板时自动应用最新的 AI 草稿；没有草稿就不打扰用户。
        ApplyLatestUploadDraft(notifyWhenMissing: false);
        Pane = ModStudioPane.Upload;
        RaiseDerived();
    }

    /// <summary>重新从磁盘读取 AI 草稿并填入上传表单。</summary>
    /// <param name="notifyWhenMissing">没有草稿时是否在状态栏说明；页面恢复时用 false。</param>
    public void ReloadUploadDraft(bool notifyWhenMissing = true)
    {
        ApplyLatestUploadDraft(notifyWhenMissing);
        if (_uploadDraftNoticeKind != UploadDraftNoticeKind.None)
        {
            StatusMessage = UploadDraftNotice;
        }
    }

    /// <summary>把选中的 mod 信息填进上传表单。</summary>
    public void ResetUploadForm()
    {
        SetUploadDraftNotice(UploadDraftNoticeKind.None);
        UploadTarget = ModUploadTarget.Steam;
        if (SelectedRow is null)
        {
            UploadTitle = string.Empty;
            UploadDescription = string.Empty;
            UploadModId = string.Empty;
            UploadDirectory = string.Empty;
            UploadChangeNote = string.Empty;
            UploadPreviewPath = null;
            UploadPublic = true;
            UploadVisibility = SteamWorkshopVisibility.Public;
            ClearUploadExtras();
            ResetLanguageEditing(string.Empty, string.Empty);
            BuildUploadTagOptions(Array.Empty<string>());
            return;
        }

        var publication = SelectedRow.Publication;
        var descriptor = SelectedRow.Descriptor;
        var publishedTitle = publication?.Title;

        // 早期版本把 Steam 的 UTF-8 标题按 ANSI 解过，存量记录里会留下 U+FFFD；这种标题不能再填回上传框，
        // 否则用户一上传就把「锟斤拷」原样写回工坊，回退到 mod 描述符里的名字。
        UploadTitle = publishedTitle is not null && !publishedTitle.Contains('\uFFFD')
            ? publishedTitle
            : descriptor?.Name ?? SelectedRow.Name;
        UploadDescription = publication?.Description ?? descriptor?.Description ?? string.Empty;
        UploadModId = publication?.PublishedFileId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        UploadDirectory = SelectedRow.DirPath ?? string.Empty;
        UploadPreviewPath = publication?.PreviewPath ?? descriptor?.PicturePath;
        UploadChangeNote = string.Empty;
        UploadPublic = publication?.Public ?? true;
        UploadVisibility = publication?.EffectiveVisibility
            ?? SteamWorkshopVisibilityExtensions.Effective(UploadPublic);
        ClearUploadExtras();
        ResetLanguageEditing(UploadTitle, UploadDescription);
        ApplyPublicationExtras(publication);
        BuildUploadTagOptions(publication?.Tags ?? descriptor?.Tags ?? Array.Empty<string>());
    }

    /// <summary>把磁盘上最新的 AI 草稿填进上传表单。</summary>
    private void ApplyLatestUploadDraft(bool notifyWhenMissing)
    {
        if (Game is null || SelectedRow is null)
        {
            SetUploadDraftNotice(
                notifyWhenMissing ? UploadDraftNoticeKind.Missing : UploadDraftNoticeKind.None);
            return;
        }

        var draft = _service.TryGetUploadDraft(Game, SelectedRow.Entry);
        if (draft is null)
        {
            SetUploadDraftNotice(
                notifyWhenMissing ? UploadDraftNoticeKind.Missing : UploadDraftNoticeKind.None);
            return;
        }

        // 草稿早于上次发布：它描述的是已经上传过的内容，再填回去只会让用户误以为有新改动。
        if (SelectedRow.Publication is { } publication && draft.UpdatedAt <= publication.UpdatedAt)
        {
            SetUploadDraftNotice(UploadDraftNoticeKind.Outdated);
            return;
        }

        ApplyUploadDraft(draft);
        SetUploadDraftNotice(UploadDraftNoticeKind.Applied, draft.UpdatedAt);
    }

    /// <summary>应用草稿里与上传表单同名的字段；预览图、附加语言和 Mod ID 保持原样。</summary>
    private void ApplyUploadDraft(ModUploadDraft draft)
    {
        // 先保存当前语言的草稿，避免用户切到别的语言时把正在编辑的内容丢掉。
        SaveCurrentLanguageDraft();

        UploadTitle = draft.Title;
        UploadDescription = draft.Description;
        UploadChangeNote = draft.ChangeNote ?? string.Empty;
        UploadVisibility = draft.Visibility;
        UploadPublic = draft.Visibility == SteamWorkshopVisibility.Public;
        BuildUploadTagOptions(draft.Tags);

        UploadSelectedLanguage = SteamWorkshopLanguages.All.First(option => IsDefaultLanguage(option.Code));
        _localizedDrafts[SteamWorkshopLanguages.Default] = new ModLocalizedText(
            SteamWorkshopLanguages.Default,
            draft.Title,
            draft.Description);
        OnPropertyChanged(nameof(UploadExtraLanguageSummary));

        if (draft.ContentDescriptors is { } descriptors)
        {
            UploadDescriptorNudity = descriptors.Contains(1);
            UploadDescriptorViolence = descriptors.Contains(2);
            UploadDescriptorAdultOnly = descriptors.Contains(3);
            UploadDescriptorFrequentNudity = descriptors.Contains(4);
            UploadDescriptorAdult = descriptors.Contains(5);
        }
    }

    private void SetUploadDraftNotice(UploadDraftNoticeKind kind, DateTimeOffset? appliedAt = null)
    {
        _uploadDraftNoticeKind = kind;
        _uploadDraftAppliedAt = appliedAt;
        OnPropertyChanged(nameof(UploadDraftNotice));
        OnPropertyChanged(nameof(HasUploadDraftNotice));
    }

    /// <summary>语言编辑重置到 english，并把它作为默认槽位。</summary>
    private void ResetLanguageEditing(string title, string description)
    {
        _localizedDrafts.Clear();
        UploadSelectedLanguage = SteamWorkshopLanguages.All.First(option => IsDefaultLanguage(option.Code));
        _localizedDrafts[SteamWorkshopLanguages.Default] = new ModLocalizedText(
            SteamWorkshopLanguages.Default,
            title,
            description);
        OnPropertyChanged(nameof(UploadExtraLanguageSummary));
    }

    /// <summary>清空上传表单里所有非文本的附加项（预览图、视频、描述词、语言）。</summary>
    private void ClearUploadExtras()
    {
        UploadPreviewItems.Clear();
        UploadVideoItems.Clear();
        UploadNewVideoUrl = string.Empty;
        UploadDescriptorAdult = false;
        UploadDescriptorViolence = false;
        UploadDescriptorNudity = false;
        UploadDescriptorFrequentNudity = false;
        UploadDescriptorAdultOnly = false;
    }

    /// <summary>把上次发布的扩展字段回填进表单。</summary>
    private void ApplyPublicationExtras(ModPublication? publication)
    {
        if (publication is null)
        {
            return;
        }

        foreach (var path in publication.ExtraPreviews)
        {
            UploadPreviewItems.Add(new ModUploadPreviewItem(path));
        }

        foreach (var videoId in publication.Videos)
        {
            UploadVideoItems.Add(new ModUploadVideoItem(videoId));
        }

        foreach (var descriptorId in publication.Descriptors)
        {
            switch (descriptorId)
            {
                case 1:
                    UploadDescriptorNudity = true;
                    break;
                case 2:
                    UploadDescriptorViolence = true;
                    break;
                case 3:
                    UploadDescriptorAdultOnly = true;
                    break;
                case 4:
                    UploadDescriptorFrequentNudity = true;
                    break;
                case 5:
                    UploadDescriptorAdult = true;
                    break;
            }
        }

        foreach (var text in publication.ExtraLocalizations)
        {
            _localizedDrafts[SteamWorkshopLanguages.Normalize(text.Language)] = text;
        }
    }

    private void BuildUploadTagOptions(IReadOnlyList<string> selected)
    {
        foreach (var existing in UploadTagOptions)
        {
            existing.PropertyChanged -= OnUploadTagOptionChanged;
        }

        UploadTagOptions.Clear();
        var selectedSet = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        var tags = ModTagCatalog.For(Game?.GameId).ToList();

        foreach (var tag in selected)
        {
            if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(tag);
            }
        }

        foreach (var tag in tags)
        {
            var option = new ModTagOption(tag) { IsSelected = selectedSet.Contains(tag) };
            option.PropertyChanged += OnUploadTagOptionChanged;
            UploadTagOptions.Add(option);
        }

        OnPropertyChanged(nameof(UploadSelectedTagCountText));
    }

    private void OnUploadTagOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModTagOption.IsSelected))
        {
            OnPropertyChanged(nameof(UploadSelectedTagCountText));
        }
    }

    /// <summary>新建一个本地 mod。</summary>
    public async Task CreateAsync(CancellationToken cancellationToken)
    {
        if (Game is null || !CanCreate)
        {
            StatusMessage = T(Keys.ModstudioUnsupportedCreate);
            return;
        }

        var name = CreateName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = T(Keys.ModstudioCreateNameRequired);
            return;
        }

        var folder = CreateFolderName.Trim();
        if (folder.Length == 0)
        {
            folder = SuggestFolder(name);
            CreateFolderName = folder;
        }

        var request = new ModCreateRequest(
            name,
            folder,
            string.IsNullOrWhiteSpace(CreateVersion) ? "0.1.0" : CreateVersion.Trim(),
            string.IsNullOrWhiteSpace(CreateSupportedVersion)
                ? SuggestSupportedVersion(Game.Version)
                : CreateSupportedVersion.Trim(),
            SplitTags(CreateTags),
            string.IsNullOrWhiteSpace(CreateDescription) ? null : CreateDescription.Trim(),
            string.IsNullOrWhiteSpace(CreatePreviewPath) ? null : CreatePreviewPath);

        IsBusy = true;
        try
        {
            var result = _service.Create(Game, request);
            if (!result.Ok)
            {
                StatusMessage = T(Keys.ModstudioCreateFailed, result.ErrorCode ?? T(Keys.CommonUnknown));
                return;
            }

            await LoadAsync(Game, cancellationToken, resetPane: true).ConfigureAwait(true);
            if (result.DirPath is { Length: > 0 } dirPath)
            {
                SelectByPath(dirPath);
            }

            Pane = ModStudioPane.Detail;
            StatusMessage = T(Keys.ModstudioCreateSuccess, name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            StatusMessage = T(Keys.ModstudioCreateFailed, exception.Message);
            _logger.Error("创建本地 mod 失败", exception);
        }
        finally
        {
            IsBusy = false;
            RaiseDerived();
        }
    }

    /// <summary>
    /// 把编辑后的字段写回选中 mod 的描述符文件，然后重新加载列表与详情。
    /// <para>只改名称/版本/支持版本/标签/简介；文件里的注释、未知键与 path 原样保留。</para>
    /// </summary>
    /// <param name="edit">用户编辑后的字段。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否写入成功。</returns>
    public async Task<bool> SaveDescriptorAsync(ModDescriptorEdit edit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);

        if (Game is null || SelectedRow?.Descriptor is not { } descriptor || !CanEditSelectedDescriptor)
        {
            StatusMessage = T(Keys.ModstudioEditUnavailable);
            return false;
        }

        IsBusy = true;
        try
        {
            var result = _service.SaveDescriptor(Game, descriptor, edit);
            if (!result.Ok)
            {
                StatusMessage = T(Keys.ModstudioEditFailed, result.ErrorCode ?? T(Keys.CommonUnknown));
                return false;
            }

            await LoadAsync(Game, cancellationToken, resetPane: false).ConfigureAwait(true);
            StatusMessage = T(Keys.ModstudioEditSuccess);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            IsBusy = false;
            RaiseDerived();
        }
    }

    /// <summary>把当前选中的本地 mod 发布到创意工坊。</summary>
    public async Task PublishAsync(
        Action<ModPublishProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (Game is null || SelectedRow is null)
        {
            _logger.Warn("上传被拦下：没有选中可上传的 Mod");
            StatusMessage = T(Keys.ModstudioUploadSelectMod);
            return;
        }

        if (!SelectedRow.CanUpload)
        {
            _logger.Warn("上传被拦下：" + SelectedRow.UploadDisabledText + "（" + SelectedRow.Name + "）");
            StatusMessage = SelectedRow.UploadDisabledText;
            return;
        }

        if (UploadTarget != ModUploadTarget.Steam)
        {
            _logger.Warn("上传被拦下：上传目标不是 Steam");
            StatusMessage = T(Keys.ModstudioUploadTargetPdxUnavailable);
            return;
        }

        if (!ModIdInput.TryParse(UploadModId, out var publishedFileId))
        {
            _logger.Warn("上传被拦下：工坊 id 不合法（" + UploadModId + "）");
            StatusMessage = T(Keys.ModstudioUploadModIdInvalid);
            return;
        }

        SaveCurrentLanguageDraft();

        var title = UploadTitle.Trim();
        if (title.Length == 0)
        {
            _logger.Warn("上传被拦下：标题为空");
            StatusMessage = T(Keys.ModstudioUploadTitleRequired);
            return;
        }

        // Steam 的标题 / 描述是定长缓冲，超长会被静默截断或只返回一个「设置失败」。
        // 这里先按 UTF-8 字节挡住，用户还能改；到了宿主再失败就只剩一句看不懂的英文。
        if (IsUploadTitleTooLong)
        {
            _logger.Warn("上传被拦下：" + UploadTitleWarningText);
            StatusMessage = UploadTitleWarningText;
            return;
        }

        if (IsUploadDescriptionTooLong)
        {
            _logger.Warn("上传被拦下：" + UploadDescriptionWarningText);
            StatusMessage = UploadDescriptionWarningText;
            return;
        }

        var tooLarge = UploadPreviewItems.FirstOrDefault(item => item.IsTooLarge);
        if (tooLarge is not null)
        {
            _logger.Warn("上传被拦下：预览图超过 1 MB（" + tooLarge.FileName + "）");
            StatusMessage = T(Keys.ModstudioPreviewTooLarge) + "：" + tooLarge.FileName;
            return;
        }

        var localizations = CollectLocalizations();

        IsPublishing = true;
        NeedsLegalAgreement = false;
        ProgressPercent = 0;
        ProgressText = T(Keys.ModstudioStagePreparing);
        StatusMessage = string.Empty;

        try
        {
            var result = await _service.PublishAsync(
                Game,
                SelectedRow.Entry,
                publishedFileId,
                title,
                UploadDescription.Trim(),
                UploadTagOptions.Where(option => option.IsSelected).Select(option => option.Name).ToArray(),
                UploadVisibility,
                string.IsNullOrWhiteSpace(UploadChangeNote) ? null : UploadChangeNote.Trim(),
                string.IsNullOrWhiteSpace(UploadPreviewPath) ? null : UploadPreviewPath,
                CollectContentDescriptors(),
                UploadPreviewItems.Select(item => item.Path).ToArray(),
                UploadVideoItems.Select(item => item.VideoId).ToArray(),
                localizations,
                progress,
                cancellationToken).ConfigureAwait(true);

            NeedsLegalAgreement = result.NeedsLegalAgreement;
            if (!result.Ok)
            {
                StatusMessage = T(Keys.ModstudioUploadFailed, result.Error ?? T(Keys.CommonUnknown));
                return;
            }

            ProgressPercent = 100;
            ProgressText = T(Keys.ModstudioStageDone);
            var selectedPath = SelectedRow.DirPath;
            await LoadAsync(Game, cancellationToken, resetPane: true).ConfigureAwait(true);
            SelectByPath(selectedPath);
            Pane = ModStudioPane.Detail;
            StatusMessage = T(Keys.ModstudioUploadSuccess);
        }
        catch (OperationCanceledException)
        {
            // 取消上传就把用户送回详情：留在一个已经作废的上传面板上会让人以为按钮失灵。
            Pane = ModStudioPane.Detail;
            StatusMessage = T(Keys.ModstudioUploadCanceled);
        }
        catch (Exception exception)
        {
            StatusMessage = T(Keys.ModstudioUploadFailed, exception.Message);
            _logger.Error("发布到创意工坊失败", exception);
        }
        finally
        {
            IsPublishing = false;
            RaiseDerived();
        }
    }

    /// <summary>
    /// 把选中的本地 mod 绑定到一条**已经存在**的创意工坊条目上。
    /// <para>
    /// 这是给「条目已经发布，但启动器查不到」准备的出口：隐藏 / 私密 / 仅好友的条目
    /// 对匿名 API 一律回 EResult 9，只有作者手动给 id 才认得出来。
    /// </para>
    /// </summary>
    /// <param name="publishedFileId">工坊条目 id。</param>
    /// <param name="verifyVisibility">是否顺便从 Steam 读回真实可见性。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>绑定结果。</returns>
    public async Task<ModBindResult> BindWorkshopAsync(
        ulong publishedFileId,
        bool verifyVisibility,
        CancellationToken cancellationToken)
    {
        if (Game is null || SelectedRow is null)
        {
            StatusMessage = T(Keys.ModstudioUploadSelectMod);
            return ModBindResult.Fail(T(Keys.ModstudioUploadSelectMod));
        }

        if (!CanBindWorkshop)
        {
            var blocked = string.IsNullOrWhiteSpace(SelectedRow.DirPath)
                ? T(Keys.ModstudioUploadNotLocal)
                : T(Keys.ModstudioNoWorkshopHint);
            _logger.Warn("绑定工坊条目被拦下：" + blocked + "（" + SelectedRow.Name + "）");
            StatusMessage = blocked;
            return ModBindResult.Fail(blocked);
        }

        var selectedPath = SelectedRow.DirPath;
        var result = await _service
            .BindWorkshopItemAsync(Game, SelectedRow.Entry, publishedFileId, verifyVisibility, cancellationToken)
            .ConfigureAwait(true);

        if (!result.Ok)
        {
            StatusMessage = T(Keys.ModstudioBindFailed, result.Error ?? T(Keys.CommonUnknown));
            _logger.Warn("绑定创意工坊条目失败：" + result.Error);
            return result;
        }

        await LoadAsync(Game, cancellationToken, resetPane: true).ConfigureAwait(true);
        SelectByPath(selectedPath);
        Pane = ModStudioPane.Detail;
        StatusMessage = result.Verified
            ? T(Keys.ModstudioBindSuccess, publishedFileId)
            : T(Keys.ModstudioBindUnverified, result.VerifyError ?? T(Keys.CommonUnknown));
        return result;
    }

    /// <summary>
    /// 列出当前账号在本游戏下自己发布的工坊条目，供绑定对话框挑选。
    /// <para>
    /// 原生枚举只给 id，不给标题，所以标题位置放的是 id 本身；选中后绑定流程会读详情补上真标题。
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>条目列表与失败原因。</returns>
    public async Task<(IReadOnlyList<(ulong Id, string Title, bool Unlisted)> Items, string? Error)> ReadOwnWorkshopItemsAsync(
        CancellationToken cancellationToken)
    {
        if (Game is null)
        {
            return (Array.Empty<(ulong Id, string Title, bool Unlisted)>(), T(Keys.ModstudioNoGameHint));
        }

        var (result, error) = await _service.ReadOwnItemsAsync(Game, cancellationToken).ConfigureAwait(true);
        if (result is null || !result.Ok)
        {
            return (Array.Empty<(ulong Id, string Title, bool Unlisted)>(), error ?? T(Keys.CommonUnknown));
        }

        var items = result.Ids
            .Select(id => (Id: id, Title: id.ToString(CultureInfo.InvariantCulture), Unlisted: false))
            .ToArray();
        if (result.Truncated)
        {
            _logger.Warn("我的工坊条目超过上限，只列出了前 " + items.Length + " 条");
        }

        return (items, null);
    }

    /// <summary>页面把后台进度派发回 UI 线程后调用。</summary>
    public void ApplyProgress(ModPublishProgress progress)
    {
        ProgressPercent = Math.Clamp(progress.Percent, 0, 100);
        ProgressText = progress.Stage switch
        {
            "creating" => T(Keys.ModstudioStageCreating),
            "preparing" => T(Keys.ModstudioStagePreparing),
            "uploading" => T(Keys.ModstudioStageUploading),
            "preview" => T(Keys.ModstudioStagePreview),
            "committing" => T(Keys.ModstudioStageCommitting),
            "localizing" => T(Keys.ModstudioStageLocalizing),
            "done" => T(Keys.ModstudioStageDone),
            _ => T(Keys.ModstudioStagePreparing),
        };
    }

    /// <summary>页面选好封面后写回表单。</summary>
    public void SetCreatePreview(string? path)
    {
        CreatePreviewPath = path;
        OnPropertyChanged(nameof(CreatePreviewText));
    }

    /// <summary>页面选好上传封面后写回表单。</summary>
    public void SetUploadPreview(string? path)
    {
        UploadPreviewPath = path;
        OnPropertyChanged(nameof(UploadPreviewText));
    }

    /// <summary>覆盖状态栏文案。</summary>
    public void SetStatus(string message) => StatusMessage = message ?? string.Empty;

    // ---------------------------------------------------------------- 上传：附加项

    /// <summary>把选好的图片追加成附加预览（超过 8 张的部分直接丢弃）。</summary>
    public void AddUploadPreviews(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || UploadPreviewItems.Count >= ModPublishLimits.MaxAdditionalPreviews)
            {
                continue;
            }

            var normalized = Normalize(path);
            if (UploadPreviewItems.Any(item => string.Equals(Normalize(item.Path), normalized, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            UploadPreviewItems.Add(new ModUploadPreviewItem(path));
        }
    }

    /// <summary>移除一行附加预览。</summary>
    public void RemoveUploadPreview(ModUploadPreviewItem? item)
    {
        if (item is not null)
        {
            UploadPreviewItems.Remove(item);
        }
    }

    /// <summary>把附加预览上移一位（第一张会成为主封面之外的首图）。</summary>
    public void MoveUploadPreviewUp(ModUploadPreviewItem? item) => MoveUploadPreview(item, -1);

    /// <summary>把附加预览下移一位。</summary>
    public void MoveUploadPreviewDown(ModUploadPreviewItem? item) => MoveUploadPreview(item, 1);

    /// <summary>把主封面换成启动器内置的那张。</summary>
    public void UseBuiltInCover()
    {
        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "pdxnext-cover-" + Guid.NewGuid().ToString("N") + ".png");
            if (ModCoverAssets.WriteDefault(AppServices.FileSystem, temp))
            {
                SetUploadPreview(temp);
                return;
            }

            SetStatus(T(Keys.ModstudioUploadFailed, T(Keys.CommonUnknown)));
        }
        catch (IOException ex)
        {
            _logger.Warn("写入内置封面失败：" + ex.Message);
            SetStatus(T(Keys.ModstudioUploadFailed, ex.Message));
        }
    }

    /// <summary>解析并追加一条 YouTube 视频预览。</summary>
    /// <returns>成功返回 true；链接无效时写状态栏并返回 false。</returns>
    public bool AddUploadVideo(string? url)
    {
        if (!YouTubeVideoId.TryParse(url, out var videoId))
        {
            SetStatus(T(Keys.ModstudioVideoUrlInvalid));
            return false;
        }

        if (UploadVideoItems.Any(item => string.Equals(item.VideoId, videoId, StringComparison.Ordinal)))
        {
            UploadNewVideoUrl = string.Empty;
            return true;
        }

        if (UploadVideoItems.Count >= ModPublishLimits.MaxAdditionalPreviews)
        {
            UploadNewVideoUrl = string.Empty;
            return false;
        }

        UploadVideoItems.Add(new ModUploadVideoItem(videoId));
        UploadNewVideoUrl = string.Empty;
        return true;
    }

    /// <summary>移除一行视频预览。</summary>
    public void RemoveUploadVideo(ModUploadVideoItem? item)
    {
        if (item is not null)
        {
            UploadVideoItems.Remove(item);
        }
    }

    /// <summary>
    /// 切换正在编辑的语言：先把输入框里的标题与描述存进当前语言的槽位，再载入目标语言的文本。
    /// </summary>
    public void ChangeUploadLanguage(SteamWorkshopLanguage? language)
    {
        if (_changingLanguage)
        {
            return;
        }

        _changingLanguage = true;
        try
        {
            SaveCurrentLanguageDraft();
            UploadSelectedLanguage = language
                ?? SteamWorkshopLanguages.All.First(option => IsDefaultLanguage(option.Code));
            LoadLanguageDraft(CurrentLanguageCode);
            OnPropertyChanged(nameof(UploadExtraLanguageSummary));
        }
        finally
        {
            _changingLanguage = false;
        }
    }

    /// <summary>正在切换语言，用来挡住下拉框双向回写造成的重入。</summary>
    private bool _changingLanguage;

    /// <summary>除 english 之外已经填了标题的语言数量。</summary>
    public int UploadLanguageCount
        => _localizedDrafts.Count(pair => IsFilled(pair.Value) && !IsDefaultLanguage(pair.Key));

    private static bool IsDefaultLanguage(string code)
        => string.Equals(code, SteamWorkshopLanguages.Default, StringComparison.Ordinal);

    private static bool IsFilled(ModLocalizedText? text)
        => text is not null && !string.IsNullOrWhiteSpace(text.Title);

    // ------------------------------------------------- 标题 / 描述的字节与预览

    /// <summary>标题用了多少 UTF-8 字节（Steam 的缓冲是定长 <c>char[128]</c>，一个汉字占 3 字节）。</summary>
    public int UploadTitleBytes => ModPublishLimits.Utf8Length(UploadTitle);

    /// <summary>描述用了多少 UTF-8 字节（上限 8000）。</summary>
    public int UploadDescriptionBytes => ModPublishLimits.Utf8Length(UploadDescription);

    /// <summary>标题字节计数文案（「12 / 128 字节」）。</summary>
    public string UploadTitleCountText
        => T(Keys.ModstudioBytesCounter, UploadTitleBytes, ModPublishLimits.MaxTitleBytes);

    /// <summary>描述字节计数文案（「512 / 8000 字节」）。</summary>
    public string UploadDescriptionCountText
        => T(Keys.ModstudioBytesCounter, UploadDescriptionBytes, ModPublishLimits.MaxDescriptionBytes);

    /// <summary>标题是否超出 Steam 上限。</summary>
    public bool IsUploadTitleTooLong => UploadTitleBytes > ModPublishLimits.MaxTitleBytes;

    /// <summary>描述是否超出 Steam 上限。</summary>
    public bool IsUploadDescriptionTooLong => UploadDescriptionBytes > ModPublishLimits.MaxDescriptionBytes;

    /// <summary>标题超限提示；没超时是空串。</summary>
    public string UploadTitleWarningText
        => IsUploadTitleTooLong
            ? T(Keys.ModstudioTitleTooLong, UploadTitleBytes, ModPublishLimits.MaxTitleBytes)
            : string.Empty;

    /// <summary>描述超限提示；没超时是空串。</summary>
    public string UploadDescriptionWarningText
        => IsUploadDescriptionTooLong
            ? T(Keys.ModstudioDescriptionTooLong, UploadDescriptionBytes, ModPublishLimits.MaxDescriptionBytes)
            : string.Empty;

    /// <summary>描述框下方那块只读预览：和详情页用同一个解析器，所见即所得。</summary>
    public MarkdownDocument UploadDescriptionPreview => SteamBbCodeParser.Parse(UploadDescription);

    /// <summary>描述是否非空（空的时候不显示预览框，免得页面上多一块空白卡片）。</summary>
    public bool HasUploadDescriptionPreview => !string.IsNullOrWhiteSpace(UploadDescription);

    private string CurrentLanguageCode
        => SteamWorkshopLanguages.Normalize(UploadSelectedLanguage?.Code ?? SteamWorkshopLanguages.Default);

    /// <summary>把输入框里的标题与描述写回当前语言的槽位。</summary>
    private void SaveCurrentLanguageDraft()
    {
        var code = CurrentLanguageCode;
        var title = UploadTitle.Trim();
        if (title.Length == 0 && !IsDefaultLanguage(code))
        {
            _localizedDrafts.Remove(code);
            return;
        }

        _localizedDrafts[code] = new ModLocalizedText(code, title, UploadDescription.Trim());
    }

    /// <summary>把某个语言的标题与描述载入输入框。</summary>
    private void LoadLanguageDraft(string code)
    {
        if (_localizedDrafts.TryGetValue(code, out var text))
        {
            UploadTitle = text.Title;
            UploadDescription = text.Description;
            return;
        }

        UploadTitle = string.Empty;
        UploadDescription = string.Empty;
    }

    /// <summary>提交给宿主的多语言列表：除 english 外所有填了标题的语言。</summary>
    private IReadOnlyList<ModLocalizedText> CollectLocalizations()
        => _localizedDrafts
            .Where(pair => IsFilled(pair.Value) && !IsDefaultLanguage(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .Take(ModPublishLimits.MaxLocalizations)
            .ToArray();

    /// <summary>把勾选的描述词翻成 Steam 的 id（1-5）。</summary>
    private IReadOnlyList<int> CollectContentDescriptors()
    {
        var ids = new List<int>(5);
        if (UploadDescriptorNudity)
        {
            ids.Add(1);
        }

        if (UploadDescriptorViolence)
        {
            ids.Add(2);
        }

        if (UploadDescriptorAdultOnly)
        {
            ids.Add(3);
        }

        if (UploadDescriptorFrequentNudity)
        {
            ids.Add(4);
        }

        if (UploadDescriptorAdult)
        {
            ids.Add(5);
        }

        return ids;
    }

    private void MoveUploadPreview(ModUploadPreviewItem? item, int delta)
    {
        if (item is null)
        {
            return;
        }

        var index = UploadPreviewItems.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= UploadPreviewItems.Count)
        {
            return;
        }

        UploadPreviewItems.Move(index, target);
    }

    /// <summary>按路径选中一行（新建/发布后刷新列表时用）。</summary>
    public void SelectByPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalized = Normalize(path);
        foreach (var row in Mods)
        {
            if (row.DirPath is not null && string.Equals(Normalize(row.DirPath), normalized, StringComparison.OrdinalIgnoreCase))
            {
                SelectedRow = row;
                return;
            }
        }
    }

    /// <summary>语言切换：行自己会刷新，这里重算表单外的派生文案。</summary>
    protected override void Relocalize()
    {
        if (!IsPublishing)
        {
            StatusMessage = BuildCurrentStatusMessage();
        }

        RaiseDerived();
    }

    partial void OnSearchTextChanged(string value) => RebuildRows(SelectedRow?.DirPath);

    partial void OnSelectedRowChanged(ModStudioRowViewModel? value)
    {
        ResetUploadForm();

        // 在上传 / 新建面板里点左侧另一个 mod 时，用户想看的是新选中的 mod 详情；
        // 上传进行中不抢面板，免得进度条突然消失。
        if (!IsPublishing && Pane != ModStudioPane.Detail)
        {
            Pane = ModStudioPane.Detail;
        }

        RaiseDerived();
    }

    partial void OnPaneChanged(ModStudioPane value) => RaiseDerived();

    partial void OnIsPublishingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartUpload));
        OnPropertyChanged(nameof(CanEditUploadFields));
        OnPropertyChanged(nameof(CanBindWorkshop));
    }

    partial void OnUploadTargetChanged(ModUploadTarget value)
    {
        OnPropertyChanged(nameof(IsSteamTarget));
        OnPropertyChanged(nameof(IsParadoxTarget));
        OnPropertyChanged(nameof(CanStartUpload));
        OnPropertyChanged(nameof(UploadTargetHint));
    }

    partial void OnCreateNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var suggested = SuggestFolder(value);
        if (string.IsNullOrWhiteSpace(CreateFolderName)
            || string.Equals(CreateFolderName, _lastAutoFolder, StringComparison.Ordinal))
        {
            CreateFolderName = suggested;
            _lastAutoFolder = suggested;
        }
    }

    partial void OnCreatePreviewPathChanged(string? value) => OnPropertyChanged(nameof(CreatePreviewText));

    partial void OnUploadPreviewPathChanged(string? value) => OnPropertyChanged(nameof(UploadPreviewText));

    partial void OnUploadVisibilityChanged(SteamWorkshopVisibility value)
        => OnPropertyChanged(nameof(SelectedVisibilityOption));

    partial void OnUploadSelectedLanguageChanged(SteamWorkshopLanguage? value)
        => OnPropertyChanged(nameof(UploadExtraLanguageSummary));

    partial void OnUploadTitleChanged(string value)
    {
        OnPropertyChanged(nameof(UploadTitleBytes));
        OnPropertyChanged(nameof(UploadTitleCountText));
        OnPropertyChanged(nameof(IsUploadTitleTooLong));
        OnPropertyChanged(nameof(UploadTitleWarningText));
    }

    partial void OnUploadDescriptionChanged(string value)
    {
        OnPropertyChanged(nameof(UploadDescriptionBytes));
        OnPropertyChanged(nameof(UploadDescriptionCountText));
        OnPropertyChanged(nameof(IsUploadDescriptionTooLong));
        OnPropertyChanged(nameof(UploadDescriptionWarningText));
        OnPropertyChanged(nameof(UploadDescriptionPreview));
        OnPropertyChanged(nameof(HasUploadDescriptionPreview));
    }

    private void RebuildRows(string? keepPath)
    {
        if (!HasGame)
        {
            Mods.Clear();
            RaiseDerived();
            return;
        }

        var query = SearchText.Trim();
        var filtered = query.Length == 0
            ? _entries
            : _entries.Where(entry => Matches(entry, query)).ToArray();

        Mods.Clear();
        foreach (var entry in filtered.OrderBy(static entry => entry.Mod.Name, StringComparer.OrdinalIgnoreCase))
        {
            Mods.Add(new ModStudioRowViewModel(
                entry,
                ModDescriptorRules.CanPublish(Game!.GameId),
                Game.Version,
                ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize)));
        }

        if (keepPath is not null)
        {
            SelectByPath(keepPath);
        }

        if (SelectedRow is null || !Mods.Contains(SelectedRow))
        {
            SelectedRow = Mods.FirstOrDefault();
        }

        StatusMessage = BuildCurrentStatusMessage();
        RaiseDerived();
    }

    private string BuildCurrentStatusMessage()
    {
        if (!HasGame)
        {
            return T(Keys.ModstudioNoGameHint);
        }

        if (_managedByGame)
        {
            return T(Keys.ModstudioManagedHint);
        }

        if (!ModDescriptorRules.CanCreate(Game!.GameId))
        {
            return T(Keys.ModstudioUnsupportedHint);
        }

        var query = SearchText.Trim();
        if (query.Length == 0)
        {
            return T(Keys.ModstudioStatusLoaded, Mods.Count);
        }

        return Mods.Count == 0
            ? T(Keys.ModstudioStatusNoMatch, query)
            : T(Keys.ModstudioStatusFiltered, Mods.Count, _entries.Count);
    }

    private static bool Matches(ModStudioEntry entry, string query) =>
        entry.Mod.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (entry.Mod.DirPath is not null
            && entry.Mod.DirPath.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> SplitTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return text
            .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
    }

    private static string SuggestSupportedVersion(string? gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return "1.*";
        }

        var text = gameVersion.Trim().TrimStart('v', 'V');
        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[0] + "." + parts[1] + ".*" : text + ".*";
    }

    private static string SuggestFolder(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        var text = builder.ToString().Trim('_');
        if (text.Length == 0)
        {
            return "my_mod";
        }

        if (text.Length > 64)
        {
            text = text[..64].TrimEnd('_');
        }

        return char.IsAsciiLetterOrDigit(text[0]) ? text : "mod_" + text;
    }

    private static string Normalize(string path) => path
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(GameTitle));
        OnPropertyChanged(nameof(HasGame));
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanPublishGame));
        OnPropertyChanged(nameof(CanUploadSelected));
        OnPropertyChanged(nameof(IsDetailPane));
        OnPropertyChanged(nameof(IsCreatePane));
        OnPropertyChanged(nameof(IsUploadPane));
        OnPropertyChanged(nameof(ShowNoGameHint));
        OnPropertyChanged(nameof(ShowManagedHint));
        OnPropertyChanged(nameof(ShowUnsupportedCreateHint));
        OnPropertyChanged(nameof(ShowNoWorkshopHint));
        OnPropertyChanged(nameof(ShowNoModsHint));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(DescriptorHint));
        OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(SelectedFolder));
        OnPropertyChanged(nameof(SelectedVersion));
        OnPropertyChanged(nameof(SelectedSupportedVersion));
        OnPropertyChanged(nameof(SelectedTags));
        OnPropertyChanged(nameof(SelectedDescription));
        OnPropertyChanged(nameof(SelectedDescriptor));
        OnPropertyChanged(nameof(SelectedPublication));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanOpenSelectedWorkshop));
        OnPropertyChanged(nameof(CanOpenSelectedFolder));
        OnPropertyChanged(nameof(CanEditSelectedDescriptor));
        OnPropertyChanged(nameof(CanStartUpload));
        OnPropertyChanged(nameof(CanEditUploadFields));
        OnPropertyChanged(nameof(HasSelectedPublication));
        OnPropertyChanged(nameof(SelectedSteamVisibility));
        OnPropertyChanged(nameof(HasSelectedSteamVisibility));
        OnPropertyChanged(nameof(CanBindWorkshop));
        OnPropertyChanged(nameof(BindWorkshopHint));
        OnPropertyChanged(nameof(CreateFolderHintText));
        OnPropertyChanged(nameof(UploadModName));
        OnPropertyChanged(nameof(UploadDirectoryText));
        OnPropertyChanged(nameof(UploadModIdHint));
        OnPropertyChanged(nameof(UploadTargetHint));
        OnPropertyChanged(nameof(UploadSelectedTagCountText));
        OnPropertyChanged(nameof(IsSteamTarget));
        OnPropertyChanged(nameof(IsParadoxTarget));
        OnPropertyChanged(nameof(UploadHintText));
        OnPropertyChanged(nameof(CreatePreviewText));
        OnPropertyChanged(nameof(UploadPreviewText));
    }
}
