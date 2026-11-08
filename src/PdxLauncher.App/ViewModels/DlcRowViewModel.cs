namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Dlc;
using PdxLauncher.Core.Patch;

/// <summary>
/// DLC 列表里的一行。Core 的 <see cref="DlcInfo"/> 是不可变 record，
/// 所以在这里包一层可变的启用状态与简介。不含任何 Microsoft.UI.Xaml 类型。
/// 带文案的属性一律在 getter 里用 <c>T(...)</c> 现取，因此不需要 override <c>Relocalize()</c>：
/// 基类在语言变化时会通知本类的全部公开属性。
/// </summary>
public sealed class DlcRowViewModel : LocalizedObservableObject
{
    private readonly Action<DlcRowViewModel>? _onEnabledChanged;
    private readonly bool _preferSteamArtwork;
    private readonly bool _hasSquareThumbnail;
    private bool _isEnabled;
    private string _description;
    private string _artworkPath = string.Empty;
    private bool? _isOwned;
    private DlcPatchCoverage _coverage = DlcPatchCoverage.None;
    private GamePatchStatus _patchStatus = GamePatchStatus.Unsupported;
    private DlcContentState _contentState = DlcContentState.Unknown;

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
    /// <param name="preferSteamArtwork">
    /// 取到 Steam 商店头图后是否允许覆盖本地缩略图。EU5 的缩略图是 500×500 方形，
    /// 塞进 16:9 卡片会裁掉上下，所以优先用商店的宽幅头图；其他游戏的本地横图更准，保持优先。
    /// </param>
    /// <param name="hasSquareThumbnail">
    /// 本地缩略图是否为方形（EU5 的 500×500 DDS）。方形图不能用 UniformToFill 填满 16:9 框，
    /// 否则上下会被裁掉；显示本地图时改用 Uniform 完整显示。
    /// </param>
    public DlcRowViewModel(
        DlcInfo dlc,
        bool isEnabled,
        bool canBeDisabled,
        Action<DlcRowViewModel>? onEnabledChanged = null,
        bool showsToggle = true,
        bool preferSteamArtwork = false,
        bool hasSquareThumbnail = false)
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
        _preferSteamArtwork = preferSteamArtwork;
        _hasSquareThumbnail = hasSquareThumbnail;
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
    /// 当前显示的图是不是本地缩略图。普通游戏有本地图就一直用本地图；
    /// EU5（<c>preferSteamArtwork</c>）在商店头图回来之后改用商店图，本地图只作离线兜底。
    /// </summary>
    public bool ShowsLocalThumbnail => HasThumbnail && !(_preferSteamArtwork && _artworkPath.Length > 0);

    /// <summary>
    /// 图片框（208×117，16:9）是否应该被填满。
    /// 只有本地横图才填满；EU5 的方形本地图和 Steam 宽幅头图都用 <c>Uniform</c> 完整显示。
    /// </summary>
    public bool FillsImageBox => ShowsLocalThumbnail && !_hasSquareThumbnail;

    /// <summary>
    /// 实际显示的图片：本地缩略图优先（EU5 例外，见 <see cref="ShowsLocalThumbnail"/>），
    /// 没有本地图时用 Steam 商店头图（由 <c>DlcViewModel</c> 后台补全）。都没有时返回空串。
    /// </summary>
    public string ImagePath => ShowsLocalThumbnail ? ThumbnailPath : _artworkPath;

    /// <summary>是否有任何可显示的图片（占位图按它切换）。</summary>
    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath);

    /// <summary>
    /// 是否还需要向 Steam 商店要头图：没有本地图的行一直需要；
    /// EU5 即使有本地方形图也要（取到后覆盖本地图）。已经取到过就不再重复请求。
    /// </summary>
    public bool NeedsSteamArtwork => !string.IsNullOrWhiteSpace(SteamId)
        && _artworkPath.Length == 0
        && (_preferSteamArtwork || !HasThumbnail);

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

    /// <summary>
    /// 后台补到的商店头图路径。只由 <c>DlcViewModel</c> 在联网补全后设置；
    /// 普通游戏已经有磁盘缩略图时忽略（本地素材更准），EU5 例外（宽幅商店图优先）。
    /// </summary>
    public void SetArtworkPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || (HasThumbnail && !_preferSteamArtwork))
        {
            return;
        }

        var text = path.Trim();
        if (string.Equals(_artworkPath, text, StringComparison.Ordinal))
        {
            return;
        }

        _artworkPath = text;
        OnPropertyChanged(nameof(ImagePath));
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(ShowsLocalThumbnail));
        OnPropertyChanged(nameof(FillsImageBox));
        OnPropertyChanged(nameof(NeedsSteamArtwork));
    }

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
                OnPropertyChanged(nameof(ShowsPatchBadge));
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

    /// <summary>
    /// 补丁覆盖状态。只有<strong>未拥有</strong>的行才会显示徽标：已拥有的 DLC 本来就能玩，
    /// 再标一条"补丁里有它"只会让列表更吵。
    /// </summary>
    public DlcPatchCoverage Coverage => _coverage;

    /// <summary>
    /// 补丁徽标的配色类别：applied / overwritten / covered / none（由 XamlBrush 换成主题画笔）。
    /// <para>
    /// 「可覆盖」与「被覆盖」共用 <see cref="DlcPatchCoverage.Covered"/>，靠补丁态区分：
    /// 被 Steam 更新冲掉时补丁并没有生效，绝不能借 <c>applied</c> 的绿色说成已打补丁。
    /// </para>
    /// </summary>
    public string CoverageKind => _coverage switch
    {
        DlcPatchCoverage.Applied => "applied",
        DlcPatchCoverage.Covered when _patchStatus == GamePatchStatus.SteamOverwrote => "overwritten",
        DlcPatchCoverage.Covered => "covered",
        _ => "none",
    };

    /// <summary>补丁徽标文字（已打补丁 / 补丁可覆盖 / 补丁被覆盖）；无徽标时为空串。</summary>
    public string CoverageText => _coverage switch
    {
        DlcPatchCoverage.Applied => T(Keys.DlcPatchAppliedBadge),
        DlcPatchCoverage.Covered when _patchStatus == GamePatchStatus.SteamOverwrote
            => T(Keys.DlcPatchOverwrittenBadge),
        DlcPatchCoverage.Covered => T(Keys.DlcPatchCoveredBadge),
        _ => string.Empty,
    };

    /// <summary>补丁徽标的自动化 Id（供 UI 测试确认补丁覆盖状态确实显示出来了）。</summary>
    public string CoverageAutomationId => "txtDlcPatchCovered_" + EffectiveId;

    /// <summary>补丁徽标悬停提示：说明这条 DLC 在补丁清单里、以及它在哪个 AppID 下。</summary>
    public string CoverageToolTip => string.IsNullOrWhiteSpace(SteamId)
        ? T(Keys.DlcOwnershipRowNoSteam)
        : T(Keys.DlcPatchCoverageToolTip, SteamId);

    /// <summary>
    /// 是否显示补丁徽标：只给未拥有的行看，且必须真的读到了覆盖结论
    /// （<see cref="DlcPatchCoverage.Unknown"/> 表示清单都没读到，此时不显示比乱标更诚实）。
    /// </summary>
    public bool ShowsPatchBadge => _isOwned == false
        && _coverage is DlcPatchCoverage.Applied or DlcPatchCoverage.Covered;

    /// <summary>
    /// 由 <c>DlcViewModel</c> 在拿到未拥有清单与补丁态后回填。
    /// 补丁态与覆盖结论一起传：同一个 <see cref="DlcPatchCoverage.Covered"/> 在
    /// 「还没打补丁」和「补丁被 Steam 覆盖」两种情况下要显示不同的文案。
    /// </summary>
    public void SetPatchCoverage(DlcPatchCoverage coverage, GamePatchStatus patchStatus)
    {
        _coverage = coverage;
        _patchStatus = patchStatus;
        OnPropertyChanged(nameof(Coverage));
        OnPropertyChanged(nameof(CoverageKind));
        OnPropertyChanged(nameof(CoverageText));
        OnPropertyChanged(nameof(CoverageToolTip));
        OnPropertyChanged(nameof(ShowsPatchBadge));
    }

    /// <summary>
    /// DLC <strong>内容</strong>的来源（与补丁覆盖是两个维度：补丁只管授权，不管资源文件在不在）。
    /// </summary>
    public DlcContentState ContentState => _contentState;

    /// <summary>内容徽标的配色类别：steam / files / missing / none（由 XamlBrush 换成主题画笔）。</summary>
    public string ContentKind => _contentState switch
    {
        DlcContentState.SteamInstalled => "steam",
        DlcContentState.FilesPresent => "files",
        DlcContentState.Missing => "missing",
        _ => "none",
    };

    /// <summary>内容徽标文字（Steam 已装 / 仅本地文件 / 内容未安装）；无徽标时为空串。</summary>
    public string ContentText => _contentState switch
    {
        DlcContentState.SteamInstalled => T(Keys.DlcContentSteamText),
        DlcContentState.FilesPresent => T(Keys.DlcContentFilesText),
        DlcContentState.Missing => T(Keys.DlcContentMissingText),
        _ => string.Empty,
    };

    /// <summary>内容徽标的自动化 Id（供 UI 测试确认内容来源确实显示出来了）。</summary>
    public string ContentAutomationId => "txtDlcContent_" + EffectiveId;

    /// <summary>
    /// 内容徽标悬停提示：说明这份内容归谁维护、以及缺的时候该怎么补。
    /// 没有 Steam AppID 的行不会走到这里（那时内容是 <see cref="DlcContentState.Unknown"/>）。
    /// </summary>
    public string ContentToolTip => _contentState switch
    {
        DlcContentState.SteamInstalled => T(Keys.DlcContentSteamToolTip, SteamId),
        DlcContentState.FilesPresent => T(Keys.DlcContentFilesToolTip, SteamId),
        DlcContentState.Missing => T(Keys.DlcContentMissingToolTip, SteamId),
        _ => string.Empty,
    };

    /// <summary>
    /// 是否显示内容徽标：<see cref="DlcContentState.Unknown"/> 表示判不了（没有 steam_id，
    /// 或 CS2 这种没有磁盘描述文件的游戏），此时不显示比乱标更诚实。
    /// </summary>
    public bool ShowsContentBadge => _contentState != DlcContentState.Unknown;

    /// <summary>由 <c>DlcViewModel</c> 在拿到扫描结果与 Steam 清单后一次性回填。</summary>
    public void SetContentState(DlcContentState state)
    {
        if (_contentState == state)
        {
            return;
        }

        _contentState = state;
        OnPropertyChanged(nameof(ContentState));
        OnPropertyChanged(nameof(ContentKind));
        OnPropertyChanged(nameof(ContentText));
        OnPropertyChanged(nameof(ContentToolTip));
        OnPropertyChanged(nameof(ShowsContentBadge));
    }

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
