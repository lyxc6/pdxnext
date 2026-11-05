namespace PdxLauncher.App.ViewModels;

using System.Globalization;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Saves;

/// <summary>
/// 存档列表的一行：先由第一段的 <see cref="SaveFileEntry"/> 立即显示文件名/大小/回退日期，
/// 第二段解析完成后通过 <see cref="ApplyDetail"/> 原位补齐存档名、国家、游戏内日期与版本。
/// <para>
/// 本类不引用任何 Microsoft.UI.Xaml 类型（可见性由 XAML 侧的 XamlVisibility 决定）。
/// 元数据缺失时不显示空白：日期回退到文件修改时间并明确标注来源，解析失败的档显示
/// 「元数据不可读」，启发式格式另有可见的弱化标记，三者互不冒充精确数据。
/// </para>
/// </summary>
public sealed class SaveGameRowViewModel : LocalizedObservableObject
{
    /// <summary>
    /// 元数据解析是启发式的游戏（V3 命名档、EU5 部分档、CS2 的 .cok JSON 元数据）。
    /// 这些游戏的存档行必须带可见的弱化标记（文案来自 <c>saves.experimental</c>）；CK3 / Stellaris 不走这里。
    /// </summary>
    private static readonly HashSet<string> ExperimentalGameIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "victoria3",
        "eu5",
        "cities_skylines_2",
    };

    private readonly int _index;
    private SaveRowState _state;
    private bool _isBusy;
    private bool _backupAllowed = true;
    private bool _actionAllowed = true;

    public SaveGameRowViewModel(SaveFileEntry file, int index)
    {
        ArgumentNullException.ThrowIfNull(file);
        _state = SaveRowState.FromFile(file);
        _index = index;
    }

    /// <summary>粗略文件信息（第一段就有）。</summary>
    public SaveFileEntry File => _state.File;

    /// <summary>解析后的细节；第二段补到之前为 null。</summary>
    private SaveGameMeta? Meta => _state.Detail;

    /// <summary>交给备份服务的元数据快照（只读）；细节未到时为 null，备份仍可进行。</summary>
    public SaveGameMeta? Detail => _state.Detail;

    public string FileName => _state.File.FileName;

    /// <summary>完整路径只作为悬停提示，不占列表宽度。</summary>
    public string FilePath => _state.File.FilePath;

    /// <summary>UI 自动化 id；保持 ASCII，不随语言变化。挂在行容器（ListViewItem）上。</summary>
    public string AutomationId => "save_" + _index.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 读屏用的整行名字：文件名 + 日期 + 详情。列表项的 Content 是视图模型对象，
    /// 不设这个属性时 UIA 会把整列每一行都读成类型全名（<c>PdxLauncher.App.ViewModels.SaveGameRowViewModel</c>）。
    /// 用 i18n 的成对连接片段拼，中文用「，」、英文用「, 」，不写死任何一侧的标点。
    /// </summary>
    public string AutomationName
    {
        get
        {
            var name = T(Keys.SavesRowNamePair, FileName, DateText);
            return HasDetails ? T(Keys.SavesRowNamePair, name, DetailsText) : name;
        }
    }

    public bool IsExperimental => ExperimentalGameIds.Contains(_state.File.GameId);

    public string ExperimentalText => T(Keys.SavesExperimental);

    public string ExperimentalTooltip => T(Keys.SavesExperimentalTooltip);

    public bool IsDegraded => Meta?.ParseState == SaveParseState.Degraded;

    public string DegradedText => T(Keys.SavesMetadataUnreadable);

    public bool HasTags => IsExperimental || IsDegraded;

    public bool HasGameDate => !string.IsNullOrWhiteSpace(Meta?.GameDate);

    /// <summary>游戏内日期；细节未到或没有时用文件修改时间（本地时区）。</summary>
    public string DateText => HasGameDate
        ? Meta!.GameDate!
        : _state.File.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    /// <summary>日期来自文件修改时间时显示弱化说明，避免被当成游戏内日期。</summary>
    public bool HasDateFallback => !HasGameDate;

    public string DateFallbackText => T(Keys.SavesDateFallback);

    public string SizeText => SizeFormatter.Format(_state.File.FileSize);

    public bool HasVersion => !string.IsNullOrWhiteSpace(Meta?.GameVersion);

    public string VersionText => HasVersion ? T(Keys.SavesVersion, Meta!.GameVersion!) : string.Empty;

    public bool HasDetails =>
        SaveRowText.HasSaveName(Meta?.SaveName) || SaveRowText.ShouldShowPlace(Meta?.PlayerCountry, Meta?.SaveName);

    /// <summary>
    /// 存档名 / 国家（CS2 是城市）；有哪个显示哪个，都没有时行内不留空标签。
    /// 国家段与存档名相同时不重复显示。
    /// </summary>
    public string DetailsText
    {
        get
        {
            var meta = Meta;
            if (meta is null)
            {
                return string.Empty;
            }

            var parts = new List<string>(2);
            if (SaveRowText.HasSaveName(meta.SaveName))
            {
                parts.Add(T(Keys.SavesSaveName, meta.SaveName!));
            }

            if (SaveRowText.ShouldShowPlace(meta.PlayerCountry, meta.SaveName))
            {
                parts.Add(SaveRowText.IsCityPlace(meta.Container)
                    ? T(Keys.SavesCity, meta.PlayerCountry!)
                    : T(Keys.SavesCountry, meta.PlayerCountry!));
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// 本行是否正在执行备份/删除。为 true 时行内两个按钮都禁用，避免同一份档案上
    /// 备份与删除并发（服务端没有行级锁，但并发写同一个 .bak/存档目录会互相踩）。
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanBackup));
                OnPropertyChanged(nameof(CanDelete));
            }
        }
    }

    /// <summary>备份按钮可用：不在本行操作中，且配额还有余量（配额满时由页面提示「可删除旧备份」）。</summary>
    public bool CanBackup => !IsBusy && _backupAllowed;

    /// <summary>删除按钮可用：不在本行操作中、页面不忙，且没有被配额/扫描的全局禁用。</summary>
    public bool CanDelete => !IsBusy && _actionAllowed;

    /// <summary>行内按钮文案：DataTemplate 里只能绑行 VM，绑不到页面上的 <c>L</c>。</summary>
    public string BackupText => T(Keys.SavesActionBackup);

    public string DeleteText => T(Keys.SavesActionDelete);

    /// <summary>配额变化时由视图模型统一下发；不改变行自身的忙碌态。</summary>
    public void SetBackupAllowed(bool allowed)
    {
        if (_backupAllowed == allowed)
        {
            return;
        }

        _backupAllowed = allowed;
        OnPropertyChanged(nameof(CanBackup));
    }

    /// <summary>页面整体忙碌（扫描或任一写操作）时由视图模型统一下发，避免后台竞态下仍可点删除。</summary>
    public void SetActionAllowed(bool allowed)
    {
        if (_actionAllowed == allowed)
        {
            return;
        }

        _actionAllowed = allowed;
        OnPropertyChanged(nameof(CanDelete));
    }

    /// <summary>由视图模型在操作开始时置位；操作结束（含失败/取消）必须在 finally 里复位。</summary>
    internal void SetBusy(bool busy) => IsBusy = busy;

    /// <summary>
    /// 第二段回填细节。内部经 <see cref="SaveRowState.WithDetail"/> 做「只填一次」保护：
    /// 已填好的行不会被后续批次覆盖；重复调用也不会重复发通知。
    /// </summary>
    /// <returns>本次是否真的写入了新细节（已填过时为 false）。</returns>
    public bool ApplyDetail(SaveGameMeta detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var next = _state.WithDetail(detail);
        if (next == _state)
        {
            return false;
        }

        _state = next;
        NotifyDetailChanged();
        return true;
    }

    private void NotifyDetailChanged()
    {
        // x:Bind 是 OneWay：不发通知的话，第二段填好的字段在界面上永远不刷新。
        OnPropertyChanged(nameof(AutomationName));
        OnPropertyChanged(nameof(IsDegraded));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(HasGameDate));
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(HasDateFallback));
        OnPropertyChanged(nameof(HasVersion));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(HasDetails));
        OnPropertyChanged(nameof(DetailsText));
    }
}
