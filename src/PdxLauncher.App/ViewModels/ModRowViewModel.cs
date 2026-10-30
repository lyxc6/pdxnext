namespace PdxLauncher.App.ViewModels;

using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PdxLauncher.App.Localization;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 播放集列表里的一行。Core 的 <see cref="PlaySetMod"/> 是不可变 record，
/// 无法直接双向绑定 ToggleSwitch，所以在这里包一层可变视图模型。
/// 不引用任何 Microsoft.UI.Xaml 类型：启用、上移下移、移除都通过回调交给 <see cref="ModsViewModel"/>。
/// </summary>
public sealed class ModRowViewModel : LocalizedObservableObject
{
    private readonly Action<ModRowViewModel>? _onEnabledChanged;
    private readonly Action<ModRowViewModel, int>? _onMove;
    private readonly Action<ModRowViewModel>? _onRemove;

    private bool _isEnabled;
    private int _position;
    private string? _sizeText;
    private string _status;
    private string? _source;
    private string? _originText;
    private bool _isLocalOrigin;
    private string? _warningText;

    /// <summary>可见列表总行数（1 起口径），默认 0 表示尚未由列表写入。</summary>
    private int _totalCount;

    /// <summary>能否改位次，默认 true；搜索过滤时被列表置为 false。</summary>
    private bool _canMove = true;

    /// <summary>序号单元格是否处于编辑态。</summary>
    private bool _isEditingPosition;

    public ModRowViewModel(
        PlaySetMod mod,
        Action<ModRowViewModel>? onEnabledChanged = null,
        Action<ModRowViewModel, int>? onMove = null,
        Action<ModRowViewModel>? onRemove = null,
        int thumbnailSize = ModThumbnailSizes.MediumPixels)
    {
        ArgumentNullException.ThrowIfNull(mod);

        ThumbnailSize = thumbnailSize;
        ModId = mod.ModId;
        Name = mod.Name;
        DirPath = mod.DirPath;
        ArchivePath = mod.ArchivePath;
        GameRegistryId = mod.GameRegistryId;
        ThumbnailPath = mod.ThumbnailPath;
        RequiredVersion = mod.RequiredVersion;
        SteamId = mod.SteamId;
        _source = mod.Source;
        _status = mod.Status;
        _isEnabled = mod.Enabled;
        _position = mod.Position;

        _onEnabledChanged = onEnabledChanged;
        _onMove = onMove;
        _onRemove = onRemove;

        MoveUpCommand = new RelayCommand(() => _onMove?.Invoke(this, -1));
        MoveDownCommand = new RelayCommand(() => _onMove?.Invoke(this, 1));
        RemoveCommand = new RelayCommand(() => _onRemove?.Invoke(this));
    }

    public Guid ModId { get; }

    public string Name { get; }

    public string DirPath { get; }

    public string? ArchivePath { get; }

    public string GameRegistryId { get; }

    /// <summary>缩略图绝对路径；为空时界面显示占位色块。</summary>
    public string? ThumbnailPath { get; }

    /// <summary>缩略图边长（像素）：由设置里的「Mod 图片大小」三档（32 / 48 / 72）决定。</summary>
    public double ThumbnailSize { get; }

    public string RequiredVersion { get; }

    /// <summary>Steam 创意工坊物品 id；没有（本地 mod / 老库）时为 null。</summary>
    public string? SteamId { get; }

    /// <summary>列表左侧的序号（position + 1），从 1 开始给人看。</summary>
    public string PositionText => (_position + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 序号单元格的读屏文案：「第 N 位，共 M 位，可编辑」。
    /// M 由 <see cref="ModsViewModel"/> 在重建行时写入 <see cref="TotalCount"/>。
    /// </summary>
    public string PositionEditName => T(Keys.ModsRowPositionEditName, PositionText, TotalCount);

    /// <summary>mod 大小（如 "1.2 MB"）；未知时为空，界面不显示。</summary>
    public string? SizeText
    {
        get => _sizeText;
        private set
        {
            if (SetProperty(ref _sizeText, value))
            {
                OnPropertyChanged(nameof(Subtitle));
            }
        }
    }

    /// <summary>
    /// 名称下方的一行小字：有大小就显示大小（更贴近原版启动器），否则退回注册 id；
    /// 有 RequiredVersion 时保持旧格式 "v{版本}  ·  {明细}"。
    /// </summary>
    public string Subtitle
    {
        get
        {
            var detail = GameRegistryId;
            if (_sizeText is { Length: > 0 } size)
            {
                detail = size;
            }

            return string.IsNullOrWhiteSpace(RequiredVersion)
                ? detail
                : "v" + RequiredVersion + "  ·  " + detail;
        }
    }

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath);

    /// <summary>启用开关的自动化 Id（每个 mod 唯一，供 UI 测试定位）。</summary>
    public string ToggleAutomationId => "modToggle_" + ModId.ToString("N");

    /// <summary>上移按钮的自动化 Id。</summary>
    public string MoveUpAutomationId => "modMoveUp_" + ModId.ToString("N");

    /// <summary>下移按钮的自动化 Id。</summary>
    public string MoveDownAutomationId => "modMoveDown_" + ModId.ToString("N");

    /// <summary>移除按钮的自动化 Id。</summary>
    public string RemoveAutomationId => "modRemove_" + ModId.ToString("N");

    /// <summary>序号显示态按钮（点击进入编辑）的自动化 Id。</summary>
    public string PositionButtonAutomationId => "modPosition_" + ModId.ToString("N");

    /// <summary>序号编辑态输入框的自动化 Id。</summary>
    public string PositionEditAutomationId => "modPositionEdit_" + ModId.ToString("N");

    /// <summary>上移一格（由页面按钮的 Command 调用）。</summary>
    public ICommand MoveUpCommand { get; }

    /// <summary>下移一格。</summary>
    public ICommand MoveDownCommand { get; }

    /// <summary>从当前播放集中移除（只改内存，需保存后才落盘）。</summary>
    public ICommand RemoveCommand { get; }

    /// <summary>相对排序位置（0 起），保存时写回 sqlite 的 position 字段。</summary>
    public int Position
    {
        get => _position;
        set
        {
            if (SetProperty(ref _position, value))
            {
                OnPropertyChanged(nameof(PositionText));
                OnPropertyChanged(nameof(PositionEditName));
            }
        }
    }

    /// <summary>
    /// 可见列表总行数（1 起口径），用于序号单元格的读屏文案与输入范围提示。
    /// 由 <see cref="ModsViewModel.RebuildVisibleMods"/> 在每次重建时写入。
    /// </summary>
    public int TotalCount
    {
        get => _totalCount;
        set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(PositionEditName));
            }
        }
    }

    /// <summary>
    /// 本行当前能否改位次：搜索过滤时为 false（可见顺序与真实 position 不一致）。
    /// 逐行镜像 <see cref="ModsViewModel.CanMoveMods"/>，界面用它禁用序号入口与 ↑↓。
    /// </summary>
    public bool CanMove
    {
        get => _canMove;
        set => SetProperty(ref _canMove, value);
    }

    /// <summary>
    /// 序号单元格是否处于编辑态。由 code-behind 写入（同一时刻只有一行为 true）。
    /// 行对象在每次重建时重新创建，因此该状态天然随重建复位。
    /// </summary>
    public bool IsEditingPosition
    {
        get => _isEditingPosition;
        set => SetProperty(ref _isEditingPosition, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _onEnabledChanged?.Invoke(this);
            }
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    /// <summary>
    /// 用 mod 库里的信息补全行细节：大小、来源徽标、加载警告。未知就传 null。
    /// </summary>
    /// <param name="sizeText">已格式化的大小文案。</param>
    /// <param name="originText">已翻译的来源文案（本地 / 订阅）。</param>
    /// <param name="warningText">已翻译的警告文案（这一行会被加载文件跳过 / 游戏读不到）。</param>
    public void SetDetails(
        string? sizeText,
        string? originText = null,
        bool isLocalOrigin = false,
        string? warningText = null)
    {
        SizeText = string.IsNullOrWhiteSpace(sizeText) ? null : sizeText;
        IsLocalOrigin = isLocalOrigin;
        OriginText = originText;
        WarningText = warningText;
    }

    /// <summary>来源徽标文案（本地 / 订阅）；拿不到来源时为空，界面不显示徽标。</summary>
    public string? OriginText
    {
        get => _originText;
        private set
        {
            if (SetProperty(ref _originText, value))
            {
                OnPropertyChanged(nameof(HasOrigin));
                OnPropertyChanged(nameof(IsLocalOrigin));
            }
        }
    }

    /// <summary>是否有来源徽标。</summary>
    public bool HasOrigin => !string.IsNullOrWhiteSpace(_originText);

    /// <summary>是不是本地 mod（XAML 用不同底色区分）。</summary>
    public bool IsLocalOrigin
    {
        get => _isLocalOrigin;
        private set => SetProperty(ref _isLocalOrigin, value);
    }

    /// <summary>这一行为什么不会被加载（空串表示没问题）。</summary>
    public string? WarningText
    {
        get => _warningText;
        private set
        {
            if (SetProperty(ref _warningText, value))
            {
                OnPropertyChanged(nameof(HasWarning));
            }
        }
    }

    /// <summary>是否有加载警告。</summary>
    public bool HasWarning => !string.IsNullOrWhiteSpace(_warningText);

    /// <summary>来源原始值（local / steam / …），导出时原样带走。</summary>
    public string? Source
    {
        get => _source;
        private set => SetProperty(ref _source, value);
    }

    /// <summary>导出成 Core 契约（保存用）。</summary>
    public PlaySetMod ToPlaySetMod() => new(
        ModId,
        Name,
        DirPath,
        ArchivePath,
        GameRegistryId,
        ThumbnailPath,
        RequiredVersion,
        Status,
        IsEnabled,
        Position,
        SteamId,
        Source);
}
