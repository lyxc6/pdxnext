namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Dlc;

/// <summary>
/// DLC 列表里的一行。Core 的 <see cref="DlcInfo"/> 是不可变 record，
/// 所以在这里包一层可变的启用状态与简介。不含任何 Microsoft.UI.Xaml 类型。
/// 带文案的属性一律在 getter 里用 <c>T(...)</c> 现取，因此不需要 override <c>Relocalize()</c>：
/// 基类在语言变化时会通知本类的全部公开属性。
/// </summary>
public sealed class DlcRowViewModel : LocalizedObservableObject
{
    private readonly Action<DlcRowViewModel>? _onEnabledChanged;
    private bool _isEnabled;
    private string _description;
    private bool? _isOwned;

    /// <param name="dlc">磁盘上的 DLC 描述。</param>
    /// <param name="isEnabled">该 DLC 在激活播放集里是否启用。</param>
    /// <param name="canBeDisabled">
    /// 本游戏的加载文件格式能否表达"禁用"。V1/V1_1 的 disabledDLC 只能写 pops_id，
    /// pops_id 为空时无法表达（官方 mapPopsId 会丢弃），由调用方按 formatVersion 决定；
    /// V0 的 disabled_dlcs 写的是 .dlc 相对路径，pops_id 为空也能禁用。
    /// </param>
    /// <param name="onEnabledChanged">启用状态变化回调。</param>
    /// <param name="showsToggle">
    /// 是否显示启用开关。CS2 这类由 Steam 授权决定归属的游戏没有 pops_id、播放集里也没有 DLC 条目，
    /// 打开/禁用在任何文件里都无法表达，此时整列开关直接隐藏（而不是给一个置灰的无效控件）。
    /// </param>
    public DlcRowViewModel(
        DlcInfo dlc,
        bool isEnabled,
        bool canBeDisabled,
        Action<DlcRowViewModel>? onEnabledChanged = null,
        bool showsToggle = true)
    {
        ArgumentNullException.ThrowIfNull(dlc);
        DisplayName = dlc.DisplayName;
        PopsId = dlc.PopsId;
        EffectiveId = DlcCatalog.GetEffectiveDlcId(dlc);
        CanBeDisabled = canBeDisabled;
        SteamId = dlc.SteamId;
        FilePathRelativeToDlcPath = dlc.FilePathRelativeToDlcPath;
        ThumbnailPath = dlc.ThumbnailPath;

        // 描述文件自带的简介（V3 有，CK3/Stellaris 为空串）；缺的由 DlcViewModel 联网补全后回填。
        _description = dlc.Description ?? string.Empty;

        // 不可禁用的 DLC（V1/V1_1 下 pops_id 为空）无法出现在 disabledDLC 里（官方 mapPopsId 会丢弃空 id），
        // 因此它们永远视为启用：即使上层传了 false 也强制回到 true，避免"UI 显示禁用、加载文件里没写"的不一致。
        _isEnabled = CanBeDisabled ? isEnabled : true;
        _onEnabledChanged = onEnabledChanged;
        ShowsToggle = showsToggle;
    }

    public string DisplayName { get; }

    /// <summary>DLC 描述文件里的 pops_id（可能为空串：真机 Stellaris 有 4 个如此）。</summary>
    public string PopsId { get; }

    /// <summary>
    /// 有效 id：pops_id 非空时就是 pops_id，为空时退化为相对 dlcPath 的 POSIX 路径。
    /// 用作启用状态的 key 与 AutomationId，保证 31 个 Stellaris DLC 不撞车。
    /// </summary>
    public string EffectiveId { get; }

    /// <summary>
    /// 是否可以通过本游戏的加载文件禁用。false 时 UI 把开关置灰。
    /// V1/V1_1 下 pops_id 为空的 DLC 为 false；V0 下所有 DLC 都为 true。
    /// </summary>
    public bool CanBeDisabled { get; }

    /// <summary>
    /// 是否显示启用开关。Steam 清单（CS2）没有可写的启用状态，所以整列开关隐藏；
    /// 其余游戏恒为 true，与 <see cref="CanBeDisabled"/> 一起决定"可切换 / 置灰 / 隐藏"。
    /// </summary>
    public bool ShowsToggle { get; }

    public string SteamId { get; }

    public string FilePathRelativeToDlcPath { get; }

    /// <summary>缩略图绝对路径（280×160，可能不存在）。</summary>
    public string ThumbnailPath { get; }

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath);

    /// <summary>
    /// 简介：优先用描述文件自带的值（V3 的 description.en），否则由 <c>DlcViewModel</c> 用 Steam 商店数据回填。
    /// 可写是因为它要等联网结果，UI 用 OneWay 绑定监听变化。
    /// </summary>
    public string Description
    {
        get => _description;
        set
        {
            var text = value ?? string.Empty;
            if (SetProperty(ref _description, text))
            {
                OnPropertyChanged(nameof(HasDescription));
                OnPropertyChanged(nameof(DescriptionToolTip));
            }
        }
    }

    /// <summary>是否有简介（没有时 UI 折叠简介行，标题居中）。</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(_description);

    /// <summary>悬停提示（完整简介；没有简介时返回 null，避免空提示框）。</summary>
    public string? DescriptionToolTip => HasDescription ? _description : null;

    /// <summary>启用开关的自动化 Id（用有效 id，保证每个 DLC 唯一，供 UI 测试定位）。</summary>
    public string ToggleAutomationId => "dlcToggle_" + EffectiveId;

    /// <summary>简介文本的自动化 Id（供 UI 测试确认简介已显示）。</summary>
    public string DescriptionAutomationId => "txtDlcDescription_" + EffectiveId;

    /// <summary>开关的提示文案（可禁用/置灰两种说明）。</summary>
    public string ToggleToolTip => CanBeDisabled
        ? T(Keys.DlcToggleToolTip)
        : T(Keys.DlcToggleToolTipLocked);

    /// <summary>
    /// Steam 授权判定的结果：true/false 来自实际探测，null 表示未知
    /// （没配 appid、探测失败，或这个 DLC 没有 Steam AppID）。
    /// </summary>
    public bool? IsOwned
    {
        get => _isOwned;
        private set
        {
            if (SetProperty(ref _isOwned, value))
            {
                OnPropertyChanged(nameof(OwnershipKind));
                OnPropertyChanged(nameof(OwnershipText));
                OnPropertyChanged(nameof(OwnershipToolTip));
            }
        }
    }

    /// <summary>徽标配色类别：owned / unowned / unknown（由 XamlBrush 换成主题画笔）。</summary>
    public string OwnershipKind => _isOwned switch
    {
        true => "owned",
        false => "unowned",
        _ => "unknown",
    };

    /// <summary>徽标文字（已拥有 / 未拥有 / 授权未知）。</summary>
    public string OwnershipText => _isOwned switch
    {
        true => T(Keys.DlcOwnedBadge),
        false => T(Keys.DlcUnownedBadge),
        _ => T(Keys.DlcUnknownBadge),
    };

    /// <summary>徽标的自动化 Id（供 UI 测试确认每行都显示了拥有状态）。</summary>
    public string OwnershipAutomationId => "txtDlcOwned_" + EffectiveId;

    /// <summary>徽标悬停提示：带 Steam AppID，或说明为什么判不了。</summary>
    public string OwnershipToolTip => string.IsNullOrWhiteSpace(SteamId)
        ? T(Keys.DlcOwnershipRowNoSteam)
        : T(Keys.DlcOwnershipRowToolTip, SteamId);

    /// <summary>由 <c>DlcViewModel</c> 在探测完成后回填；null 表示未知。</summary>
    public void SetOwnership(bool? isOwned) => IsOwned = isOwned;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            // 不可禁用的 DLC 只允许保持启用；UI 上开关已置灰，这里再兜一层防止代码误改。
            if (!CanBeDisabled && !value)
            {
                return;
            }

            if (SetProperty(ref _isEnabled, value))
            {
                _onEnabledChanged?.Invoke(this);
            }
        }
    }

    /// <summary>
    /// 导出成 Core 契约（保存用）。key 用有效 id：pops_id 非空时与官方
    /// launcher-v2.sqlite 的 playsets_dlcs.dlcId 完全一致；pops_id 为空时退化为相对 dlcPath 的路径，
    /// 保证这几个 DLC 的启用状态在库里互不覆盖（回读时 MergeDlcs 用同一套有效 id，可正确往返）。
    /// </summary>
    public PlaySetDlc ToPlaySetDlc() => new(EffectiveId, IsEnabled);
}
