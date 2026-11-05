namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Saves;

/// <summary>
/// 存档页的视图模型：按当前游戏按需扫描存档目录，把结果整理成只读列表。
/// <para>
/// 本片只读浏览，没有任何写文件的能力：不调用备份/恢复/删除服务，也不产生确认框。
/// 扫描在后台线程完成（<see cref="Task.Run(Func{Task}, CancellationToken)"/> 包住目录探测与扫描），
/// UI 线程不碰文件系统；失败只记 Warn 并按空状态展示，绝不让异常冒到页面。
/// </para>
/// <para>
/// 切游戏 / 连点刷新时用 <see cref="SaveScanRequestTracker"/> 做代次守卫：
/// 旧扫描即使跑到 <c>finally</c> 也不能关掉新扫描的忙碌态，更不能把上一款游戏的存档写进列表。
/// </para>
/// </summary>
public sealed class SavesViewModel : LocalizedObservableObject
{
    private readonly ISaveGameLibrary _library;
    private readonly IAppLogger _logger;
    private readonly SaveScanRequestTracker _requests = new();

    private GameInstallation? _game;
    private bool _isBusy;
    private SavesState _state = SavesState.NoGame;
    private DateTimeOffset? _lastScanAt;

    public SavesViewModel(ISaveGameLibrary library, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(logger);
        _library = library;
        _logger = logger;
    }

    /// <summary>当前游戏的存档行；切游戏或重新扫描时整体清空重建。</summary>
    public ObservableCollection<SaveGameRowViewModel> Saves { get; } = new();

    public GameInstallation? Game
    {
        get => _game;
        private set
        {
            if (SetProperty(ref _game, value))
            {
                OnPropertyChanged(nameof(GameTitle));
                OnPropertyChanged(nameof(CanRefresh));
            }
        }
    }

    public string GameTitle => Game?.DisplayName ?? T(Keys.SavesNoGame);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRefresh));
            }
        }
    }

    /// <summary>没有当前游戏或正在扫描时不能刷新（避免重复排队）。</summary>
    public bool CanRefresh => !IsBusy && Game is not null;

    public bool HasSaves => Saves.Count > 0;

    /// <summary>扫描中不显示空状态，避免进度环旁边闪一下「没有存档」。</summary>
    public bool ShowEmptyState => _state is not (SavesState.Scanning or SavesState.Ready);

    /// <summary>顶部扫描状态：扫描中 / 上次扫描时间 + 找到 N 个存档 / 失败 / 未选游戏。</summary>
    public string StatusText => _state switch
    {
        SavesState.Scanning => T(Keys.SavesStatusScanning),
        SavesState.NoGame => T(Keys.SavesStatusNoGame),
        SavesState.Failed => T(Keys.SavesStatusFailed),
        _ => _lastScanAt is { } scannedAt
            ? T(
                Keys.SavesStatusLastScan,
                scannedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
                Saves.Count)
            : string.Empty,
    };

    public string EmptyStateTitle => _state switch
    {
        SavesState.NoGame => T(Keys.SavesEmptyNoGameTitle),
        SavesState.NoDirectory => T(Keys.SavesEmptyNoDirectoryTitle),
        SavesState.Empty => T(Keys.SavesEmptyEmptyTitle),
        SavesState.Failed => T(Keys.SavesEmptyFailedTitle),
        _ => string.Empty,
    };

    public string EmptyStateMessage => _state switch
    {
        SavesState.NoGame => T(Keys.SavesEmptyNoGameMessage),
        SavesState.NoDirectory => T(Keys.SavesEmptyNoDirectoryMessage),
        SavesState.Empty => T(Keys.SavesEmptyEmptyMessage),
        SavesState.Failed => T(Keys.SavesEmptyFailedMessage),
        _ => string.Empty,
    };

    /// <summary>
    /// 扫描当前游戏的存档。切游戏时旧列表立即清空并进入 <see cref="SavesState.Scanning"/>，
    /// 避免异步扫描期间页面继续显示上一款游戏的存档或结论。
    /// </summary>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        var generation = _requests.Begin();

        Game = game;
        Saves.Clear();
        _lastScanAt = null;

        if (game is null)
        {
            IsBusy = false;
            _state = SavesState.NoGame;
            NotifyStateChanged();
            return;
        }

        var gameId = game.GameId;
        IsBusy = true;
        _state = SavesState.Scanning;
        NotifyStateChanged();

        try
        {
            // 目录探测（GetLocations 内部有 DirectoryExists）也是文件系统调用，
            // 必须和扫描一起待在线程池上，UI 线程只等结果。
            var locations = await Task.Run(
                async () =>
                {
                    var found = _library.GetLocations(gameId);
                    await _library.ScanAsync([gameId], cancellationToken).ConfigureAwait(false);
                    return found;
                },
                cancellationToken).ConfigureAwait(true);

            if (!_requests.IsCurrent(generation))
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            Apply(gameId, locations);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!_requests.IsCurrent(generation))
            {
                return;
            }

            // 扫描失败按空状态处理：不弹框、不崩页面，原因写日志。
            _logger.Warn("扫描存档失败 (" + gameId + "): " + ex.Message);
            Saves.Clear();
            _lastScanAt = DateTimeOffset.Now;
            _state = SavesState.Failed;
            NotifyStateChanged();
        }
        finally
        {
            if (_requests.IsCurrent(generation))
            {
                IsBusy = false;
                NotifyStateChanged();
            }
        }
    }

    private void Apply(string gameId, IReadOnlyList<SaveLocationInfo> locations)
    {
        // GetByGame 内部会归一 gameId，并且只读内存快照，不产生额外 IO。
        foreach (var meta in _library.GetByGame(gameId))
        {
            Saves.Add(new SaveGameRowViewModel(meta, Saves.Count));
        }

        _lastScanAt = DateTimeOffset.Now;

        var hasDirectory = locations.Any(static location => location.Exists);
        _state = !hasDirectory
            ? SavesState.NoDirectory
            : Saves.Count == 0
                ? SavesState.Empty
                : SavesState.Ready;

        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasSaves));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(CanRefresh));
    }

    private enum SavesState
    {
        NoGame,
        Scanning,
        NoDirectory,
        Empty,
        Failed,
        Ready,
    }
}
