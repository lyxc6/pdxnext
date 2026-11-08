namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Dlc;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Patch;
using PdxLauncher.Core.Steam;

/// <summary>
/// DLC 页的视图模型：列出当前游戏磁盘上真实的 DLC，允许逐项启用/禁用，
/// 保存时写 playsets_dlcs（sqlite）并同步重写加载文件。
/// <para>CS2（<c>PlaysetConfigV1</c>）没有 DLC 描述文件，列表改由游戏目录里那份
/// <c>steam_api64.dll</c> 向 Steam 要（清单与归属都是 Steam 的），且没有可写的启用状态；
/// 其余游戏仍走磁盘清单 + 播放集。</para>
/// 不含任何 Microsoft.UI.Xaml 类型。
/// </summary>
public sealed class DlcViewModel : LocalizedObservableObject
{
    private readonly PlaySetEditor _editor;

    private GameInstallation? _game;
    private bool _isBusy;
    private bool _isLoaded;
    private bool _isCheckingOwnership;
    private string _statusMessage = string.Empty;

    /// <summary>最近一次 Steam 授权探测的报告；null 表示还没探测或当前游戏不适用。</summary>
    private SteamOwnershipReport? _ownership;

    /// <summary>当前列表是不是由 Steam 授权报告构建的（CS2 这类没有 DLC 描述文件的游戏）。</summary>
    private bool _isSteamDlcList;

    /// <summary>由报告与磁盘 DLC 对账得出的结论（判定条的文案来源）。</summary>
    private DlcOwnershipVerdict? _ownershipVerdict;

    /// <summary>状态栏主句的文案键；null 表示状态栏是外部文本（核心库返回的错误信息等），不随语言重算。</summary>
    private string? _statusKey;

    /// <summary>主句键的格式化参数（数量、播放集名、DLC 名等数据，不是文案）。</summary>
    private object?[] _statusArgs = [];

    /// <summary>可选附加句的文案键（例如"其中 N 个无法禁用"）；null 表示没有附加句。</summary>
    private string? _statusDetailKey;

    /// <summary>附加句键的格式化参数。</summary>
    private object?[] _statusDetailArgs = [];

    public DlcViewModel(PlaySetEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _editor = editor;
    }

    public ObservableCollection<DlcRowViewModel> Dlcs { get; } = new();

    /// <summary>「未拥有」段的行：磁盘上没有、Steam 清单里有、账号也没授权的 DLC（只读，无开关）。</summary>
    public ObservableCollection<DlcRowViewModel> UnownedDlcs { get; } = new();

    /// <summary>最近一次规划结果（段标题计数、未拥有段状态都从这里取）。</summary>
    private DlcListPlan? _plan;

    /// <summary>
    /// 本游戏的补丁 DLC 清单（<c>dlc-catalog.json</c> 里按游戏分节的那一段），每次加载取一次。
    /// null 表示读不到清单：此时任何一条 DLC 都只能说「未知」，不显示补丁徽标。
    /// </summary>
    private IReadOnlyList<DlcEntry>? _patchedDlc;

    /// <summary>
    /// 本次加载时的补丁态。决定同一个「清单里有」该显示成「已打补丁」还是「可覆盖 / 被覆盖」——
    /// 只有 <see cref="GamePatchStatus.Patched"/> / <see cref="GamePatchStatus.PatchedOther"/> 才算生效。
    /// </summary>
    private GamePatchStatus _patchStatus = GamePatchStatus.Unsupported;

    public GameInstallation? Game
    {
        get => _game;
        private set
        {
            if (SetProperty(ref _game, value))
            {
                OnPropertyChanged(nameof(GameTitle));
            }
        }
    }

    public string GameTitle => Game?.DisplayName ?? T(Keys.DlcNoGameTitle);

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetProperty(ref _isLoaded, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public int TotalCount => Dlcs.Count;

    public int EnabledCount => Dlcs.Count(static d => d.IsEnabled);

    public string CountsText => _isSteamDlcList
        ? T(Keys.DlcCountsTextSteam, TotalCount)
        : T(Keys.DlcCountsText, TotalCount, EnabledCount);

    /// <summary>当前列表是否来自 Steam（CS2 没有 DLC 描述文件，磁盘扫描必然为空）。</summary>
    public bool IsSteamDlcList => _isSteamDlcList;

    /// <summary>
    /// 能否逐项启用/禁用 DLC。Steam 清单没有 pops_id、播放集里也没有 DLC 条目，
    /// 在 CS2 上打开/禁用写不进任何文件，所以整组编辑能力（含全部启用/禁用、保存）都关掉。
    /// </summary>
    public bool CanEditDlcToggles => !_isSteamDlcList;

    /// <summary>判定条是否可见：有磁盘清单时照旧；CS2 的清单也来自探测，所以探测过就要显示（失败时给重试）。</summary>
    public bool HasOwnershipStrip => CanRecheckOwnership && (Dlcs.Count > 0 || _ownership is not null);

    /// <summary>能否对当前游戏做授权探测（只有会写播放集的游戏才有 DLC 目录与 appid）。</summary>
    public bool CanRecheckOwnership => Game is { SupportsContentManagement: true };

    /// <summary>
    /// 是否按「已拥有 / 未拥有」两段展示。只有磁盘清单模式的游戏才分段：
    /// CS2 的列表本身就是 Steam 给的（全部已拥有），再分一段「未拥有」没有意义。
    /// </summary>
    public bool CanListUnowned => !_isSteamDlcList && Game is { SupportsContentManagement: true };

    /// <summary>
    /// 第一段的标题（带行数）。口径由 Core 规划器判定：全部磁盘行都已拥有才叫「已拥有」，
    /// 否则（Steam 没跑 → 全是「授权未知」，或报告里有未拥有行）叫「本地 DLC」，
    /// 避免出现「已拥有（31）」下面 31 条「授权未知」这种自相矛盾的标题。
    /// </summary>
    public string OwnedSectionTitle => _plan is { OwnedSectionLabel: DlcOwnedSectionLabel.Owned } plan
        ? T(Keys.DlcOwnedSection, plan.OwnedCount)
        : T(Keys.DlcLocalSection, Dlcs.Count);

    /// <summary>「已拥有」段标题是否可见（列表为空时整段不显示）。</summary>
    public bool HasOwnedSection => CanListUnowned && Dlcs.Count > 0;

    /// <summary>「未拥有」段标题（带行数）。</summary>
    public string UnownedSectionTitle => T(Keys.DlcUnownedSection, UnownedDlcs.Count);

    /// <summary>
    /// 未拥有行里被补丁清单覆盖的条数。loader 在岗且清单里有它 = 不需要买也能玩，
    /// 这个数字就是「补丁能补回多少」的直接答案。
    /// </summary>
    public int UnownedPatchCount => UnownedDlcs.Count(static d => d.ShowsPatchBadge);

    /// <summary>未拥有段的统计句（「其中 N 个在补丁清单内」）。</summary>
    public string UnownedPatchStatsText => T(Keys.DlcUnownedPatchStats, UnownedPatchCount);

    /// <summary>
    /// 统计句是否可见：一条都没覆盖时不显示（「其中 0 个」只会制造噪音），
    /// 清单读不到时也不显示（那时一条徽标都没有，统计没有意义）。
    /// </summary>
    public bool HasUnownedPatchStats => CanListUnowned && UnownedPatchCount > 0;

    /// <summary>「未拥有」段是否可见（没有未拥有条目时整段不显示）。</summary>
    public bool HasUnownedSection => CanListUnowned && UnownedDlcs.Count > 0;

    /// <summary>「未拥有」列表的自动化名称。</summary>
    public string UnownedListName => T(Keys.DlcUnownedListName);

    /// <summary>
    /// 「未拥有」段不可用时的说明：Steam 没跑 / 报告不可用 / 报告像 loader 模板，各给一句人话，
    /// 而不是留一片空白让用户以为 DLC 都拥有了。
    /// </summary>
    public string UnownedNote => _plan?.UnownedState switch
    {
        DlcUnownedListState.UntrustedCatalog => T(
            Keys.DlcUnownedNoteLoader,
            _plan.CatalogCount,
            _plan.EmptyNameCount),
        DlcUnownedListState.SteamUnavailable => _ownership is null
            ? T(Keys.DlcUnownedNoteSteamOff)
            : T(Keys.DlcUnownedNoteUnavailable, DescribeOwnershipError(_ownership)),
        _ => string.Empty,
    };

    /// <summary>说明文案是否可见。</summary>
    public bool HasUnownedNote => CanListUnowned && UnownedNote.Length > 0;

    /// <summary>判定条标题的自动化名称。</summary>
    public string OwnershipName => T(Keys.DlcOwnershipName);

    public bool IsCheckingOwnership
    {
        get => _isCheckingOwnership;
        private set
        {
            if (SetProperty(ref _isCheckingOwnership, value))
            {
                RaiseOwnership();
            }
        }
    }

    /// <summary>判定句：补丁到底有没有被游戏看到（这是本页最重要的结论）。</summary>
    public string OwnershipHeadline => BuildOwnershipHeadline();

    /// <summary>证据句：探测的 DLL、AppID、SteamID64、补丁与清单归属，便于用户自己核对。</summary>
    public string OwnershipDetail => BuildOwnershipDetail();

    public bool HasOwnershipDetail => OwnershipDetail.Length > 0;

    /// <summary>判定条配色类别：ok（补丁已生效）/ warn（补丁没完全生效）/ unknown。</summary>
    public string OwnershipKind => _ownershipVerdict?.Kind switch
    {
        DlcOwnershipVerdictKind.PatchedEffective => "ok",
        DlcOwnershipVerdictKind.VanillaAllOwned => "ok",
        DlcOwnershipVerdictKind.PatchedIncomplete => "warn",
        _ => "unknown",
    };

    /// <summary>「重新检测」按钮可用性：探测进行中时置灰，避免并发抢 Steam 闸门。</summary>
    public bool IsRecheckEnabled => CanRecheckOwnership && !IsCheckingOwnership;

    /// <summary>
    /// 加载：读取磁盘 DLC 目录 + 激活播放集的 playsets_dlcs 记录。
    /// 契约要求：没有激活播放集时全部 DLC 视为启用（与官方启动器一致）。
    /// </summary>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        Game = game;
        SetSteamDlcList(false);

        // 切游戏时先清掉上一份「未拥有」段，避免旧条目闪在新列表旁边。
        UnownedDlcs.Clear();
        _plan = null;
        _patchedDlc = null;
        _patchStatus = GamePatchStatus.Unsupported;
        if (game is null)
        {
            Dlcs.Clear();
            IsLoaded = false;
            SetStatus(Keys.DlcNoGameHint);
            RaiseCounts();
            ResetOwnership();
            return;
        }

        if (!game.SupportsContentManagement)
        {
            // 仅启动的游戏：DLC 只由 Steam 授权控制，扫目录没有意义，也绝不写它的播放集库。
            Dlcs.Clear();
            IsLoaded = true;
            SetStatus(Keys.ShellSupportManagedByGame);
            RaiseCounts();
            ResetOwnership();
            return;
        }

        IsBusy = true;
        try
        {
            var catalog = await AppServices.Dlcs
                .DiscoverAsync(game, cancellationToken)
                .ConfigureAwait(true);

            // 补丁清单按游戏分节（Vic3 只有 23 条，不是 loader ini 那张 490 条全表），每次加载读一次就够；
            // 读不到时是 null，未拥有行会标成「未知」而不是「补丁里没有它」——两者含义完全不同。
            _patchedDlc = AppServices.GamePatch.DlcCatalogFor(game.GameId);
            var playSet = await AppServices.PlaySets
                .GetActivePlaySetAsync(game, cancellationToken)
                .ConfigureAwait(true);

            // key 一律用有效 id（pops_id 为空时退化为相对 dlcPath 的路径），
            // 否则真机 Stellaris 那 4 个 pops_id 为空的 .dlc 会撞成同一个 key。
            var states = playSet is null
                ? catalog.Select(static d => new PlaySetDlc(DlcCatalog.GetEffectiveDlcId(d), true)).ToList()
                : PlaySetEditor.MergeDlcs(
                    await AppServices.PlaySets
                        .GetPlaySetDlcsAsync(game, playSet.Id, cancellationToken)
                        .ConfigureAwait(true),
                    catalog);

            var stateById = states.ToDictionary(static s => s.DlcId, static s => s.Enabled, StringComparer.OrdinalIgnoreCase);

            // 缩略图是 OneTime 绑定，DDS 必须先转好 PNG 再建行，否则会一直停在占位图。
            await AppServices.ImageArt
                .PrewarmAsync(catalog.Select(static d => d.ThumbnailPath), cancellationToken)
                .ConfigureAwait(true);

            // CS2 没有 DLC 描述文件（DlcCatalog 对它已提前返回空），但 Steam 那边有完整清单：
            // 改用游戏目录里的 steam_api64.dll 现场问一次，把名字与拥有态都显示出来。
            // 注意不能像以前那样直接收尾返回空列表 —— 那样会把授权判定条一起藏掉，
            // 用户只会看到"空白页 + 不提供清单"，误以为自己的 DLC 丢了。
            if (catalog.Count == 0 && game.Format == LoadFileFormat.PlaysetConfigV1)
            {
                await LoadSteamDlcListAsync(game, cancellationToken).ConfigureAwait(true);
                return;
            }

            // V0 的 disabled_dlcs 写的是 .dlc 相对 dlcPath 的 POSIX 路径，任何 DLC 都能禁用；
            // V1/V1_1 的 disabledDLC 只能写 pops_id，pops_id 为空的 DLC 无法表达 ⇒ 开关置灰。
            var contentLoadFormat = game.Format is LoadFileFormat.ContentLoadV1 or LoadFileFormat.ContentLoadV1_1;

            // 行的顺序与拥有态都由 Core 的规划器算（纯函数，Checks 覆盖）；这里只做「条目 → 行」的映射。
            var catalogByPath = new Dictionary<string, DlcInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in catalog)
            {
                catalogByPath.TryAdd(info.FilePathRelativeToDlcPath, info);
            }

            var entries = DlcListPlanner
                .Create(
                    catalog.Select(static d => new DlcListDiskRow(d.SteamId, d.DisplayName, d.FilePathRelativeToDlcPath)),
                    report: null,
                    patchedDlc: _patchedDlc)
                .Entries;

            // EU5 的 DLC 缩略图是 500×500 方形 DDS（D009 是 128×128 DXT5），
            // 填进 208×117 的 16:9 图片框会裁掉上下。EU5 行改用 Steam 商店宽幅头图优先，
            // 本地方形图只作离线兜底，并按原比例完整显示。
            var isEu5 = game.Format == LoadFileFormat.PlaysetsJsonV1;

            Dlcs.Clear();
            foreach (var entry in entries)
            {
                if (!catalogByPath.TryGetValue(entry.FilePath, out var info))
                {
                    continue;
                }

                var enabled = !stateById.TryGetValue(DlcCatalog.GetEffectiveDlcId(info), out var saved) || saved;
                var canBeDisabled = !contentLoadFormat || DlcCatalog.CanBeDisabledInContentLoad(info);
                Dlcs.Add(new DlcRowViewModel(
                    info,
                    enabled,
                    canBeDisabled,
                    OnRowEnabledChanged,
                    preferSteamArtwork: isEu5,
                    hasSquareThumbnail: isEu5));
            }

            var lockedCount = Dlcs.Count(static d => !d.CanBeDisabled);

            IsLoaded = true;
            if (playSet is null)
            {
                SetStatus(Keys.DlcStatusNoActivePlaySet);
            }
            else
            {
                SetStatus(Keys.DlcStatusLoaded, Dlcs.Count, playSet.Name);
            }

            if (lockedCount > 0)
            {
                AppendStatusDetail(Keys.DlcStatusLockedSuffix, lockedCount);
            }

            // 先探授权（本地 DLL 调用，很快），再补简介（可能联网，慢）。
            await RefreshOwnershipAsync(force: false, cancellationToken).ConfigureAwait(true);

            // 简介是弱依赖：先显示列表，再补简介（失败不影响列表）。
            await FillDescriptionsAsync(cancellationToken).ConfigureAwait(true);

            // 头图同样是弱依赖：放后台跑，不阻塞列表显示。
            _ = FillArtworkAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DLC 页加载失败（gameId=" + game.GameId + "）", ex);
            SetStatus(Keys.DlcLoadFailed, ex.Message);
            IsLoaded = true;
            ResetOwnership();
        }
        finally
        {
            IsBusy = false;
            RaiseCounts();
        }
    }

    /// <summary>
    /// 探测 Steam 授权。判定口径照搬原版启动器：已拥有 = isSubscribedApp(id) || 可信 DisabledDLC 命中 || isDlcInstalled(id)；
    /// 探测对象是**游戏目录里那份 steam_api64.dll** —— 补丁生效时它本身就是 loader，
    /// 因此这里显示的拥有态就是游戏实际看到的授权（全绿 = 补丁确实用上了）。
    /// </summary>
    /// <param name="force">true 时丢掉服务层的 30 秒缓存，强制重新加载 DLL。</param>
    public async Task RefreshOwnershipAsync(bool force, CancellationToken cancellationToken)
    {
        var game = Game;
        if (game is null || !game.SupportsContentManagement)
        {
            ResetOwnership();
            return;
        }

        var report = await ProbeOwnershipAsync(force, cancellationToken).ConfigureAwait(true);
        if (report is null)
        {
            if (IsSteamDlcList)
            {
                // 重探失败时不能把旧清单留在屏幕上假装还有效：清空并给出失败原因。
                FillSteamRows(null);
                SetSteamStatus(null);
            }

            ResetOwnership();
        }
        else
        {
            if (IsSteamDlcList)
            {
                // CS2 的列表本身就是 Steam 给的：重探后按新清单重建行（数量、名字都可能变）。
                FillSteamRows(report);
                SetSteamStatus(report);
            }

            ApplyOwnership(report);
        }

        RaiseCounts();
        RaiseOwnership();

        // 「重新检测」会经 ApplyPlan 重建未拥有行（UnownedDlcs.Clear），新行没有头图路径；
        // 不补一次就会退回占位图标，直到重进页面。图片已在 data\dlc-artwork 里，命中不联网。
        if (force)
        {
            _ = FillArtworkAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 真正跑一次探测：<c>Inspect</c> 会拉起宿主子进程（<c>--steam-host probe</c>）去加载 steam_api64.dll 并调
    /// <c>SteamAPI_Init</c>，放到线程池，别卡 UI 线程。
    /// 失败只记日志并返回 null —— 授权判定是弱依赖，不该让整页加载失败。
    /// </summary>
    /// <param name="force">true 时丢掉服务层的 30 秒缓存，强制重新加载 DLL。</param>
    private async Task<SteamOwnershipReport?> ProbeOwnershipAsync(bool force, CancellationToken cancellationToken)
    {
        var game = Game;
        if (game is null || !game.SupportsContentManagement)
        {
            return null;
        }

        if (force)
        {
            AppServices.DlcOwnership.Invalidate();
        }

        IsCheckingOwnership = true;
        try
        {
            return await Task
                .Run(() => AppServices.DlcOwnership.Inspect(game, cancellationToken), cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DLC 授权探测失败（gameId=" + game.GameId + "）", ex);
            return null;
        }
        finally
        {
            IsCheckingOwnership = false;
            RaiseOwnership();
        }
    }

    /// <summary>
    /// CS2（<c>PlaysetConfigV1</c>）的加载路径：列表完全来自 Steam 授权报告。
    /// 这类游戏没有 <c>.dlc</c> 文件、播放集里也没有 DLC 条目，启用状态无处落库，
    /// 所以行上不显示开关，也不提供"全部启用/禁用/保存"。
    /// </summary>
    private async Task LoadSteamDlcListAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        SetSteamDlcList(true);
        Dlcs.Clear();

        var report = await ProbeOwnershipAsync(force: false, cancellationToken).ConfigureAwait(true);
        FillSteamRows(report);
        if (report is null)
        {
            ResetOwnership();
        }
        else
        {
            ApplyOwnership(report);
        }

        SetSteamStatus(report);
        IsLoaded = true;
        RaiseCounts();
        RaiseOwnership();

        // 简介是弱依赖：先把清单显示出来，再按 Steam appid 慢慢补（失败不影响列表）。
        await FillDescriptionsAsync(cancellationToken).ConfigureAwait(true);
        _ = FillArtworkAsync(cancellationToken);
    }

    /// <summary>按探测报告重建 CS2 的 DLC 行；报告不可用时清空。</summary>
    private void FillSteamRows(SteamOwnershipReport? report)
    {
        Dlcs.Clear();
        if (report is not { HasOwnership: true })
        {
            return;
        }

        // loader 在岗时 report.Dlcs 是 cream_api.ini 的大表（含其他游戏），不能拿来当本游戏的清单；
        // 有账号视图就用账号视图，没有就退回主清单（至少还能展示）。
        var source = report is { IsLoaderActive: true, Account: { HasOwnership: true } account }
            ? account.Dlcs
            : report.Dlcs;

        foreach (var info in SteamDlcListBuilder.FromOwnership(source))
        {
            // 没有 pops_id、也没有播放集条目 ⇒ 开关无处落地，整列隐藏，而不是给一个置灰的无效控件。
            Dlcs.Add(new DlcRowViewModel(
                info,
                isEnabled: true,
                canBeDisabled: false,
                onEnabledChanged: null,
                showsToggle: false));
        }
    }

    /// <summary>CS2 的状态栏文案：有清单 / Steam 说没有 DLC / 探测失败三种。</summary>
    private void SetSteamStatus(SteamOwnershipReport? report)
    {
        if (Dlcs.Count > 0)
        {
            SetStatus(Keys.DlcStatusSteamList, Dlcs.Count);
        }
        else if (report is { HasOwnership: true })
        {
            SetStatus(Keys.DlcStatusSteamEmpty);
        }
        else if (report is not null)
        {
            SetStatus(Keys.DlcStatusSteamUnavailable, DescribeOwnershipError(report));
        }
        else
        {
            SetStatus(Keys.DlcStatusSteamUnavailable, T(Keys.DlcOwnershipErrorNative));
        }
    }

    /// <summary>记下"列表来自 Steam"这个模式，并通知随它变化的计数文案与编辑能力。</summary>
    private void SetSteamDlcList(bool value)
    {
        if (_isSteamDlcList == value)
        {
            return;
        }

        _isSteamDlcList = value;
        OnPropertyChanged(nameof(IsSteamDlcList));
        OnPropertyChanged(nameof(CanEditDlcToggles));
        OnPropertyChanged(nameof(CountsText));
        RaiseSections();
    }

    private void ApplyOwnership(SteamOwnershipReport report)
    {
        _ownership = report;

        var candidates = Dlcs
            .Where(static d => !string.IsNullOrWhiteSpace(d.SteamId))
            .Select(static d => new DlcOwnershipCandidate(d.SteamId, d.DisplayName))
            .ToList();
        _ownershipVerdict = DlcOwnershipVerdict.Evaluate(report.PatchStatus, report, candidates);

        // 拥有态、未拥有清单与排序都由 Core 的规划器算（Checks 覆盖），这里只做映射。
        // 补丁清单与补丁态一起传：同一个「清单里有」在补丁生效时是「已打补丁」，
        // 被 Steam 更新冲掉时只能算「被覆盖」，不能混为一谈。
        _patchStatus = report.PatchStatus;
        var plan = DlcListPlanner.Create(
            Dlcs.Select(ToDiskRow),
            report,
            patchedDlc: _patchedDlc,
            patchStatus: report.PatchStatus);
        _plan = plan;
        ApplyPlan(plan);

        var accountText = report.Account is { } account
            ? "，账号视图 state=" + account.State + "、" + account.Dlcs.Count + " 个 DLC、" + account.OwnedAppIds.Count + " 个已拥有"
            : "，无账号视图";
        AppServices.Logger.Info(
            "DLC 授权探测（gameId=" + (Game?.GameId ?? "<null>") + "）：state=" + report.State
            + "，loader 在岗=" + report.IsLoaderActive
            + "，主探测 " + report.Dlcs.Count + " 个 DLC、" + report.OwnedAppIds.Count + " 个已拥有"
            + "，未拥有 " + plan.UnownedCount + " 个（" + plan.UnownedState + "）"
            + accountText
            + "，结论=" + (_ownershipVerdict?.Kind.ToString() ?? "<null>"));

        LogContentSummary();
    }

    private void ResetOwnership()
    {
        _ownership = null;
        _ownershipVerdict = null;

        // 报告没了也要把磁盘行的拥有态退回「未知」，并清掉上一份未拥有段。
        // 补丁态也一起归零：不知道补丁在不在岗时，宁可标「未知」也不能说「可覆盖」。
        _patchStatus = GamePatchStatus.Unsupported;
        var plan = DlcListPlanner.Create(
            Dlcs.Select(ToDiskRow),
            report: null,
            patchedDlc: null,
            patchStatus: GamePatchStatus.Unsupported);
        _plan = plan;
        ApplyPlan(plan);

        RaiseOwnership();
    }

    /// <summary>把规划结果映射成界面：磁盘行按路径回填拥有态，未拥有段按条目重建。</summary>
    private void ApplyPlan(DlcListPlan plan)
    {
        var ownedByPath = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
        var coverageByPath = new Dictionary<string, DlcPatchCoverage>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in plan.Entries)
        {
            ownedByPath.TryAdd(entry.FilePath, entry.Owned);
            coverageByPath.TryAdd(entry.FilePath, entry.Coverage);
        }

        // 内容维度与授权/补丁两个维度都无关：补丁只让游戏读到授权，不会把 DLC 的资源文件放到磁盘上。
        // 清单（InstalledDepots）读不到时返回 null，此时任何行都不会被判成「Steam 已装」。
        var steamInstalledDlcAppIds = ResolveSteamInstalledDlcAppIds();

        foreach (var row in Dlcs)
        {
            row.SetOwnership(ownedByPath.TryGetValue(row.FilePathRelativeToDlcPath, out var owned) ? owned : null);
            row.SetPatchCoverage(
                coverageByPath.TryGetValue(row.FilePathRelativeToDlcPath, out var coverage)
                    ? coverage
                    : DlcPatchCoverage.None,
                _patchStatus);
            row.SetContentState(DlcContentInspector.Classify(
                row.SteamId,
                row.FilePathRelativeToDlcPath,
                steamInstalledDlcAppIds));
        }

        UnownedDlcs.Clear();
        if (_isSteamDlcList)
        {
            // CS2 的清单本身就是 Steam 给的，全部是「已拥有」，不再分段。
            RaiseSections();
            return;
        }

        foreach (var entry in plan.Unowned)
        {
            var info = new DlcInfo(
                DisplayName: entry.DisplayName,
                PopsId: string.Empty,
                SteamId: entry.SteamId,
                FilePathRelativeToDlcPath: entry.FilePath,
                ThumbnailPath: string.Empty,
                Description: string.Empty,
                RawBytes: Array.Empty<byte>());

            // 未拥有行只读：没有开关（没有 pops_id、也写不进播放集），缩略图留空显示占位图。
            var row = new DlcRowViewModel(
                info,
                isEnabled: true,
                canBeDisabled: false,
                onEnabledChanged: null,
                showsToggle: false);
            row.SetOwnership(entry.Owned);
            row.SetPatchCoverage(entry.Coverage, _patchStatus);
            row.SetContentState(DlcContentInspector.Classify(
                entry.SteamId,
                entry.FilePath,
                steamInstalledDlcAppIds));
            UnownedDlcs.Add(row);
        }

        RaiseSections();
    }

    /// <summary>
    /// 读当前游戏的 <c>appmanifest_&lt;appid&gt;.acf</c>，取 <c>InstalledDepots</c> 里登记为 DLC 的 appid。
    /// <para>读不到（没有 appid、清单缺失、段缺失、解析失败）一律返回 null ＝「不知道」：
    /// 绝不能拿空集合冒充「一个都没装」，那会把 Steam 装好的 DLC 标成人工拷贝。</para>
    /// </summary>
    private IReadOnlySet<int>? ResolveSteamInstalledDlcAppIds()
    {
        var game = _game;
        var resolvedAppId = _ownership?.SteamAppId;
        if (game is null || resolvedAppId is not int appId || appId <= 0)
        {
            return null;
        }

        try
        {
            // 库根传 null：FindLibraryRoot 会从安装目录往上找 steamapps，游戏装在哪个库都能对上。
            var manifest = new AppManifestLookup(AppServices.FileSystem, AppServices.Logger)
                .Find(game, appId, libraryRoots: null);
            if (manifest is null)
            {
                AppServices.Logger.Warn("DLC 内容判定：读不到 appmanifest（appid=" + appId + "），只按磁盘文件判断");
                return null;
            }

            return manifest.KnowsDlcDepots ? manifest.InstalledDlcAppIds : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppServices.Logger.Warn("DLC 内容判定：读取 appmanifest 失败（appid=" + appId + "）：" + ex.Message);
            return null;
        }
    }

    /// <summary>日志里记一行内容来源汇总：「授权通了但 DLC 打不开」常见的真实原因是内容根本没落盘。</summary>
    private void LogContentSummary()
    {
        if (_ownership is null)
        {
            return;
        }

        var steam = 0;
        var files = 0;
        var missing = 0;
        var unknown = 0;
        foreach (var row in Dlcs.Concat(UnownedDlcs))
        {
            switch (row.ContentState)
            {
                case DlcContentState.SteamInstalled:
                    steam++;
                    break;
                case DlcContentState.FilesPresent:
                    files++;
                    break;
                case DlcContentState.Missing:
                    missing++;
                    break;
                default:
                    unknown++;
                    break;
            }
        }

        AppServices.Logger.Info(
            "DLC 内容（gameId=" + (_game?.GameId ?? "<null>") + "）：Steam 已装 " + steam
            + " / 仅本地文件 " + files
            + " / 未安装 " + missing
            + " / 未知 " + unknown);
    }

    /// <summary>磁盘行 → 规划器输入（只传规划要用的三个字段）。</summary>
    private static DlcListDiskRow ToDiskRow(DlcRowViewModel row) =>
        new(row.SteamId, row.DisplayName, row.FilePathRelativeToDlcPath);

    private void RaiseOwnership()
    {
        OnPropertyChanged(nameof(HasOwnershipStrip));
        OnPropertyChanged(nameof(CanRecheckOwnership));
        OnPropertyChanged(nameof(IsSteamDlcList));
        OnPropertyChanged(nameof(CanEditDlcToggles));
        OnPropertyChanged(nameof(OwnershipName));
        OnPropertyChanged(nameof(OwnershipHeadline));
        OnPropertyChanged(nameof(OwnershipDetail));
        OnPropertyChanged(nameof(HasOwnershipDetail));
        OnPropertyChanged(nameof(OwnershipKind));
        OnPropertyChanged(nameof(IsRecheckEnabled));
    }

    private string BuildOwnershipHeadline()
    {
        if (IsCheckingOwnership)
        {
            return T(Keys.DlcOwnershipChecking);
        }

        if (_ownership is not { } report)
        {
            return T(Keys.DlcOwnershipUnknown);
        }

        if (!report.HasOwnership)
        {
            return T(Keys.DlcOwnershipUnavailable, DescribeOwnershipError(report));
        }

        var verdict = _ownershipVerdict
            ?? DlcOwnershipVerdict.Evaluate(report.PatchStatus, report, null);

        var headline = verdict.Kind switch
        {
            DlcOwnershipVerdictKind.PatchedEffective => T(
                Keys.DlcOwnershipPatchedEffective,
                verdict.ProbeDlcCount,
                verdict.ProbeOwnedCount,
                verdict.DiskDlcCount),
            DlcOwnershipVerdictKind.PatchedIncomplete => T(
                Keys.DlcOwnershipPatchedIncomplete,
                verdict.DiskDlcCount,
                verdict.UnownedDiskDlcCount,
                verdict.ProbeDlcCount,
                verdict.ProbeOwnedCount),
            DlcOwnershipVerdictKind.VanillaAllOwned => T(
                Keys.DlcOwnershipVanillaAllOwned,
                verdict.DiskDlcCount,
                verdict.ProbeDlcCount,
                verdict.ProbeOwnedCount),
            DlcOwnershipVerdictKind.VanillaPartial => T(
                Keys.DlcOwnershipVanillaPartial,
                verdict.DiskDlcCount,
                verdict.UnownedDiskDlcCount,
                verdict.ProbeDlcCount,
                verdict.ProbeOwnedCount),
            _ => T(Keys.DlcOwnershipUnknown),
        };

        // 只判「游戏看到的授权」会让人把 loader 报的数字当 Steam 数字；loader 在岗时补一句账号真实拥有。
        return verdict.IsLoaderActive ? headline + " " + BuildAccountSentence(verdict) : headline;
    }

    /// <summary>loader 在岗时的第二句：账号本身拥有多少，或者为什么判断不了。</summary>
    private string BuildAccountSentence(DlcOwnershipVerdict verdict)
    {
        if (verdict.AccountDlcCount is int count && verdict.AccountOwnedCount is int owned)
        {
            return T(Keys.DlcOwnershipAccountOwned, owned, count);
        }

        var reason = verdict.AccountErrorKey is { Length: > 0 } key
            ? DescribeProbeError(key)
            : T(Keys.DlcOwnershipErrorNoBackup);
        return T(Keys.DlcOwnershipAccountUnknown, reason);
    }

    private string BuildOwnershipDetail()
    {
        if (IsCheckingOwnership || _ownership is not { } report)
        {
            return string.Empty;
        }

        var parts = new List<string>(8);
        if (!string.IsNullOrWhiteSpace(report.ApiFilePath))
        {
            parts.Add(T(Keys.DlcOwnershipDetailApi, report.ApiFilePath!));
        }

        if (report.SteamAppId is int appId)
        {
            parts.Add(T(Keys.DlcOwnershipDetailAppId, appId));
        }

        if (report.SteamId64 != 0)
        {
            parts.Add(T(Keys.DlcOwnershipDetailSteamId, report.SteamId64));
        }

        // 四种状态分开说，尤其「他人补丁」不能和「本工具打的补丁」混成一句。
        parts.Add(report.PatchStatus switch
        {
            GamePatchStatus.Patched => T(Keys.DlcOwnershipDetailPatchOn),
            GamePatchStatus.PatchedOther => T(Keys.DlcOwnershipDetailPatchOnByOther),
            GamePatchStatus.SteamOverwrote => T(Keys.DlcOwnershipDetailPatchOverwritten),
            _ => T(Keys.DlcOwnershipDetailPatchOff),
        });

        if (report.HasOwnership && report.SteamId64 != 0)
        {
            parts.Add(report.ManifestOwnerMatches
                ? T(Keys.DlcOwnershipDetailOwnerMatch)
                : T(Keys.DlcOwnershipDetailOwnerMismatch, report.ManifestLastOwner ?? "?"));
        }
        else if (!string.IsNullOrWhiteSpace(report.Detail))
        {
            parts.Add(FirstLine(report.Detail!));
        }

        var verdict = _ownershipVerdict
            ?? DlcOwnershipVerdict.Evaluate(report.PatchStatus, report, null);
        if (verdict.IsLoaderActive)
        {
            // 把「这个数字从哪来」写在脸上：loader 的表来自 cream_api.ini，不是 Steam 目录。
            parts.Add(T(Keys.DlcOwnershipDetailLoaderSource));
            if (verdict.ForeignDlcCount > 0 && verdict.AccountDlcCount is int accountCount)
            {
                parts.Add(T(
                    Keys.DlcOwnershipDetailForeignDlc,
                    verdict.ProbeDlcCount,
                    verdict.ForeignDlcCount,
                    accountCount));
            }
        }

        if (report.Account is { ApiFilePath: { Length: > 0 } accountApi })
        {
            parts.Add(T(Keys.DlcOwnershipDetailAccountApi, accountApi));
        }

        return string.Join(" · ", parts);
    }

    private string DescribeOwnershipError(SteamOwnershipReport report) => DescribeProbeError(report.ErrorKey);

    /// <summary>把探测错误键翻成人话；主报告与账号视图共用，避免两套说法。</summary>
    private string DescribeProbeError(string? errorKey) => errorKey switch
    {
        SteamProbeError.NoAppId => T(Keys.DlcOwnershipErrorNoAppId),
        SteamProbeError.NoDll => T(Keys.DlcOwnershipErrorNoDll),
        SteamProbeError.X86 => T(Keys.DlcOwnershipErrorX86),
        SteamProbeError.NotWindows => T(Keys.DlcOwnershipErrorNotWindows),
        SteamProbeError.Busy => T(Keys.DlcOwnershipErrorBusy),
        SteamProbeError.SteamNotRunning => T(Keys.DlcOwnershipErrorSteamNotRunning),
        SteamProbeError.InitFailed => T(Keys.DlcOwnershipErrorInitFailed),
        SteamProbeError.HostFailed => T(Keys.DlcOwnershipErrorHostFailed),
        SteamProbeError.BackupNotOriginal => T(Keys.DlcOwnershipErrorBackupNotOriginal),
        _ => T(Keys.DlcOwnershipErrorNative),
    };

    /// <summary>只取技术错误的第一行：证据句要短，完整异常已经写进日志。</summary>
    private static string FirstLine(string text)
    {
        var index = text.IndexOfAny(['\r', '\n']);
        var line = index >= 0 ? text[..index] : text;
        return line.Length <= 160 ? line.Trim() : line[..160].Trim() + "…";
    }

    /// <summary>
    /// 补全简介：V3 的 .dlc.json 自带 description.en 已随 DlcInfo 进来；
    /// CK3/Stellaris 的 .dlc 没有简介，这里按 Steam appid 查本地缓存（允许联网时再补缺失/过期项）。
    /// 失败只记 Warn —— 简介缺失不应该影响 DLC 列表与启用状态。
    /// </summary>
    private async Task FillDescriptionsAsync(CancellationToken cancellationToken)
    {
        var missing = Dlcs
            .Where(static d => !d.HasDescription && !string.IsNullOrWhiteSpace(d.SteamId))
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var previousStatus = CaptureStatus();
        SetStatus(Keys.DlcDescriptionsLoading, missing.Count);
        try
        {
            var descriptions = await AppServices.DlcDescriptions
                .GetDescriptionsAsync(
                    missing.Select(static d => d.SteamId).ToList(),
                    SteamStoreLanguage.FromUiLanguage(Localization.Localizer.Instance.Language),
                    AppServices.Settings.DlcDescriptionCacheDays,
                    AppServices.Settings.OnlineDlcDescriptionEnabled,
                    cancellationToken)
                .ConfigureAwait(true);

            var filled = 0;
            foreach (var row in missing)
            {
                if (descriptions.TryGetValue(row.SteamId, out var text) && !string.IsNullOrWhiteSpace(text))
                {
                    row.Description = text;
                    filled++;
                }
            }

            AppServices.Logger.Info("DLC 简介已就位：" + filled + "/" + missing.Count + " 个（gameId=" + (Game?.GameId ?? "<null>") + "）");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("DLC 简介获取失败（只影响简介显示）：" + ex.Message);
        }
        finally
        {
            // 不覆盖用户在这段时间里触发的新状态（例如刚点了某个开关）。
            if (string.Equals(_statusKey, Keys.DlcDescriptionsLoading, StringComparison.Ordinal))
            {
                RestoreStatus(previousStatus);
            }
        }
    }

    /// <summary>
    /// 补 DLC 头图：磁盘上的 .dlc 只带名字，未拥有的 DLC 也没有本地素材，所以按 Steam appid 取商店头图。
    /// EU5 的本地缩略图是方形，卡片框是 16:9，填满会裁掉上下；这些行也参与取图，取到后覆盖本地缩略图。
    /// 弱依赖：失败只记 Warn，占位图标照旧；不阻塞列表与切换游戏的取消。
    /// </summary>
    private async Task FillArtworkAsync(CancellationToken cancellationToken)
    {
        // 需要商店图的行由 DlcRowViewModel.NeedsSteamArtwork 判断：
        // 没有本地素材的行一直需要；EU5 即使有本地方形图也要（宽幅商店图优先）。
        // 两边按 SteamId 去重后一次请求，再把同一张图回填给所有命中该 appid 的行。
        var wanted = Dlcs
            .Concat(UnownedDlcs)
            .Where(static d => d.NeedsSteamArtwork)
            .ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        try
        {
            var ids = wanted
                .Select(static d => d.SteamId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var artwork = await AppServices.DlcArtwork
                .GetArtworkAsync(ids, cancellationToken)
                .ConfigureAwait(true);

            var filled = 0;
            foreach (var row in wanted)
            {
                if (artwork.TryGetValue(row.SteamId, out var path) && !string.IsNullOrWhiteSpace(path))
                {
                    row.SetArtworkPath(path);
                    filled++;
                }
            }

            AppServices.Logger.Info(
                "DLC 头图已就位：" + filled + "/" + wanted.Count + " 行（" + ids.Count + " 个 appid，gameId="
                + (Game?.GameId ?? "<null>") + "）");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 切游戏/离页：安静退出，不写状态。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("DLC 头图获取失败（只影响图片显示）：" + ex.Message);
        }
    }
    private void OnRowEnabledChanged(DlcRowViewModel row)
    {
        RaiseCounts();
        SetStatus(
            row.IsEnabled ? Keys.DlcStatusRowEnabled : Keys.DlcStatusRowDisabled,
            row.DisplayName);
    }

    /// <summary>全部启用 / 全部禁用。不可禁用的 DLC 会被跳过（开关已置灰）。</summary>
    public void SetAllEnabled(bool enabled)
    {
        if (!CanEditDlcToggles)
        {
            // Steam 清单没有可写的启用状态（CS2）：整组操作无从落地，直接忽略。
            return;
        }

        var skipped = 0;
        foreach (var row in Dlcs)
        {
            if (!enabled && !row.CanBeDisabled)
            {
                skipped++;
                continue;
            }

            row.IsEnabled = enabled;
        }

        RaiseCounts();
        SetStatus(enabled ? Keys.DlcStatusAllEnabled : Keys.DlcStatusAllDisabled);
        if (skipped > 0)
        {
            AppendStatusDetail(Keys.DlcStatusSkippedSuffix, skipped);
        }
    }

    /// <summary>
    /// 保存：写 playsets_dlcs（sqlite）+ 同步写加载文件（disabledDLC / disabled_dlcs）。
    /// 必须先有激活播放集，否则 DLC 状态无处落库。
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (Game is null)
        {
            SetStatus(Keys.DlcNoGameHint);
            return;
        }

        if (!CanEditDlcToggles)
        {
            // CS2 的 DLC 完全由 Steam 授权决定：播放集里没有 DLC 条目，写不了也没有意义。
            SetStatus(Keys.DlcSaveSteamManaged);
            return;
        }

        IsBusy = true;
        try
        {
            var playSet = await AppServices.PlaySets
                .GetActivePlaySetAsync(Game, cancellationToken)
                .ConfigureAwait(true);
            if (playSet is null)
            {
                SetStatus(Keys.DlcSaveNoActivePlaySet);
                return;
            }

            var catalog = await AppServices.Dlcs
                .DiscoverAsync(Game, cancellationToken)
                .ConfigureAwait(true);
            var dlcs = Dlcs.Select(static d => d.ToPlaySetDlc()).ToList();

            var savedMods = await AppServices.PlaySets
                .GetPlaySetModsAsync(Game, playSet.Id, cancellationToken)
                .ConfigureAwait(true);
            var modCatalog = await AppServices.Mods
                .DiscoverAllAsync(Game, cancellationToken)
                .ConfigureAwait(true);
            var mods = PlaySetEditor.MergeWithCatalog(savedMods, modCatalog);

            var result = await _editor
                .SaveAsync(Game, playSet.Id, mods, dlcs, catalog, cancellationToken)
                .ConfigureAwait(true);

            if (result.IsSuccess)
            {
                SetStatus(Keys.DlcStatusSaved, EnabledCount, TotalCount);
                AppServices.Logger.Info("DlcPage 保存成功（gameId=" + Game.GameId + "）");
            }
            else if (result.ErrorMessage is { Length: > 0 } errorMessage)
            {
                // 核心库返回的失败原因属于数据（已由 Core 决定内容），原样显示。
                SetExternalStatus(errorMessage);
            }
            else
            {
                SetStatus(Keys.DlcSaveFailedShort);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("保存 DLC 失败（gameId=" + Game.GameId + "）", ex);
            SetStatus(Keys.DlcSaveFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 语言切换：状态栏存的是已经拼好的文案（外部文本除外），按同一个键重算一遍；
    /// 游戏名与计数文案是计算属性，基类已统一通知，这里不用管。
    /// </summary>
    protected override void Relocalize()
    {
        RefreshStatus();
        RaiseOwnership();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(CountsText));
        OnPropertyChanged(nameof(HasOwnershipStrip));
        RaiseSections();
    }

    /// <summary>通知「已拥有 / 未拥有」分段相关的计算属性（计数变了、报告换了都要重发）。</summary>
    private void RaiseSections()
    {
        OnPropertyChanged(nameof(CanListUnowned));
        OnPropertyChanged(nameof(OwnedSectionTitle));
        OnPropertyChanged(nameof(HasOwnedSection));
        OnPropertyChanged(nameof(UnownedSectionTitle));
        OnPropertyChanged(nameof(HasUnownedSection));
        OnPropertyChanged(nameof(UnownedListName));
        OnPropertyChanged(nameof(UnownedNote));
        OnPropertyChanged(nameof(HasUnownedNote));
        OnPropertyChanged(nameof(UnownedPatchCount));
        OnPropertyChanged(nameof(UnownedPatchStatsText));
        OnPropertyChanged(nameof(HasUnownedPatchStats));
    }

    /// <summary>设置状态栏主句：同时记住键与参数，切语言时按新语言重算。</summary>
    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        _statusDetailKey = null;
        _statusDetailArgs = [];
        StatusMessage = T(key, args);
    }

    /// <summary>在主句后追加一句（例如"；N 个 DLC 被跳过"），随语言一起重算。</summary>
    private void AppendStatusDetail(string key, params object?[] args)
    {
        _statusDetailKey = key;
        _statusDetailArgs = args;
        StatusMessage += T(key, args);
    }

    /// <summary>状态栏显示外部文本（核心库返回的数据），切语言时原样保留。</summary>
    private void SetExternalStatus(string message)
    {
        _statusKey = null;
        _statusDetailKey = null;
        StatusMessage = message;
    }

    /// <summary>按当前语言重算状态栏（没有主句键时保持外部文本不动）。</summary>
    private void RefreshStatus()
    {
        if (_statusKey is null)
        {
            return;
        }

        var text = T(_statusKey, _statusArgs);
        if (_statusDetailKey is not null)
        {
            text += T(_statusDetailKey, _statusDetailArgs);
        }

        StatusMessage = text;
    }

    /// <summary>取当前状态栏的快照（补简介期间临时换成加载文案，结束后原样恢复）。</summary>
    private StatusSnapshot CaptureStatus() =>
        new(_statusKey, _statusArgs, _statusDetailKey, _statusDetailArgs, StatusMessage);

    /// <summary>恢复快照：有文案键时按当前语言重算，纯外部文本则原样写回。</summary>
    private void RestoreStatus(StatusSnapshot snapshot)
    {
        _statusKey = snapshot.Key;
        _statusArgs = snapshot.Args;
        _statusDetailKey = snapshot.DetailKey;
        _statusDetailArgs = snapshot.DetailArgs;

        if (snapshot.Key is null)
        {
            StatusMessage = snapshot.Text;
            return;
        }

        RefreshStatus();
    }

    /// <summary>状态栏快照：可重建的键/参数 + 当前已显示的文本（外部文本时靠它回退）。</summary>
    private readonly record struct StatusSnapshot(
        string? Key,
        object?[] Args,
        string? DetailKey,
        object?[] DetailArgs,
        string Text);
}
