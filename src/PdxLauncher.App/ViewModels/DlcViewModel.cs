namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Dlc;
using PdxLauncher.Core.Online;

/// <summary>
/// DLC 页的视图模型：列出当前游戏磁盘上真实的 DLC，允许逐项启用/禁用，
/// 保存时写 playsets_dlcs（sqlite）并同步重写加载文件。
/// 不含任何 Microsoft.UI.Xaml 类型。
/// </summary>
public sealed class DlcViewModel : LocalizedObservableObject
{
    private readonly PlaySetEditor _editor;

    private GameInstallation? _game;
    private bool _isBusy;
    private bool _isLoaded;
    private string _statusMessage = string.Empty;

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

    public string CountsText => T(Keys.DlcCountsText, TotalCount, EnabledCount);

    /// <summary>
    /// 加载：读取磁盘 DLC 目录 + 激活播放集的 playsets_dlcs 记录。
    /// 契约要求：没有激活播放集时全部 DLC 视为启用（与官方启动器一致）。
    /// </summary>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        Game = game;
        if (game is null)
        {
            Dlcs.Clear();
            IsLoaded = false;
            SetStatus(Keys.DlcNoGameHint);
            RaiseCounts();
            return;
        }

        if (!game.SupportsContentManagement)
        {
            // 仅启动的游戏：DLC 只由 Steam 授权控制，扫目录没有意义，也绝不写它的播放集库。
            Dlcs.Clear();
            IsLoaded = true;
            SetStatus(Keys.ShellSupportManagedByGame);
            RaiseCounts();
            return;
        }

        IsBusy = true;
        try
        {
            var catalog = await AppServices.Dlcs
                .DiscoverAsync(game, cancellationToken)
                .ConfigureAwait(true);
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

            // CS2 没有 DLC 描述文件（DlcCatalog 对它已提前返回空），这里给出"不提供 DLC 开关"
            // 的明确说明，而不是笼统的"共 0 个 DLC"，免得用户以为自己的 DLC 丢了。
            if (catalog.Count == 0 && game.Format == LoadFileFormat.PlaysetConfigV1)
            {
                Dlcs.Clear();
                IsLoaded = true;
                SetStatus(Keys.DlcEmptyNoManifest);
                RaiseCounts();
                return;
            }

            // V0 的 disabled_dlcs 写的是 .dlc 相对 dlcPath 的 POSIX 路径，任何 DLC 都能禁用；
            // V1/V1_1 的 disabledDLC 只能写 pops_id，pops_id 为空的 DLC 无法表达 ⇒ 开关置灰。
            var contentLoadFormat = game.Format is LoadFileFormat.ContentLoadV1 or LoadFileFormat.ContentLoadV1_1;

            Dlcs.Clear();
            foreach (var info in catalog.OrderBy(static d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                var enabled = !stateById.TryGetValue(DlcCatalog.GetEffectiveDlcId(info), out var saved) || saved;
                var canBeDisabled = !contentLoadFormat || DlcCatalog.CanBeDisabledInContentLoad(info);
                Dlcs.Add(new DlcRowViewModel(info, enabled, canBeDisabled, OnRowEnabledChanged));
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

            // 简介是弱依赖：先显示列表，再补简介（失败不影响列表）。
            await FillDescriptionsAsync(cancellationToken).ConfigureAwait(true);
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
        }
        finally
        {
            IsBusy = false;
            RaiseCounts();
        }
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
    protected override void Relocalize() => RefreshStatus();

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(CountsText));
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
