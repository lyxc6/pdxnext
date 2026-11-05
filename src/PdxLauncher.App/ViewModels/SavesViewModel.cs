namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Saves;

/// <summary>
/// 存档页的视图模型：两段式加载，先出列表、再补细节。
/// <list type="number">
///   <item>第一段 <see cref="ISaveGameFileIndex.ScanFilesAsync"/> 只枚举文件（路径/名字/大小/文件时间），
///   在后台线程完成，结果一次性铺进 <see cref="Saves"/>；</item>
///   <item>第二段按 <see cref="SaveScanBatching.DefaultBatchSize"/> 条一批解析细节，
///   通过 <see cref="SaveGameRowViewModel.ApplyDetail"/> 原位回填，<b>不</b>清空/重建集合，滚动位置不丢。</item>
/// </list>
/// <para>
/// 目录探测与枚举都在 <see cref="ISaveGameFileIndex"/> 内部线程池上，UI 线程不碰文件系统；
/// 切游戏 / 连点刷新时用 <see cref="SaveScanRequestTracker"/> 做代次守卫：
/// 旧扫描即使跑到 <c>finally</c> 也不能关掉新扫描的忙碌态，更不能把上一款游戏的存档写进列表。
/// </para>
/// </summary>
public sealed class SavesViewModel : LocalizedObservableObject
{
    private readonly ISaveGameFileIndex _library;
    private readonly IAppLogger _logger;
    private readonly SaveScanRequestTracker _requests = new();

    private GameInstallation? _game;
    private bool _isBusy;
    private SavesState _state = SavesState.NoGame;
    private DateTimeOffset? _lastScanAt;
    private int _processed;
    private int _total;

    public SavesViewModel(ISaveGameFileIndex library, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(logger);
        _library = library;
        _logger = logger;
    }

    /// <summary>当前游戏的存档行；切游戏或重新扫描时整体清空，第二段只原位补细节。</summary>
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

    /// <summary>扫描/补细节期间不显示空状态，避免进度环旁边闪一下「没有存档」。</summary>
    public bool ShowEmptyState => _state is not (SavesState.Scanning or SavesState.Reading or SavesState.Ready);

    /// <summary>顶部扫描状态：扫描中 / 正在读取 n / N / 上次扫描时间 + 找到 N 个存档 / 失败 / 未选游戏。</summary>
    public string StatusText => _state switch
    {
        SavesState.Scanning => T(Keys.SavesStatusScanning),
        SavesState.Reading => T(Keys.SavesStatusReading, _processed, _total),
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
    /// 两段式加载当前游戏的存档。切游戏时旧列表立即清空并进入 <see cref="SavesState.Scanning"/>，
    /// 避免异步扫描期间页面继续显示上一款游戏的存档或结论。
    /// </summary>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        var generation = _requests.Begin();

        Game = game;
        Saves.Clear();
        _lastScanAt = null;
        _processed = 0;
        _total = 0;

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
            // 第一段：枚举文件（内部 Task.Run，UI 线程零 IO）。不解析任何内容。
            var index = await _library.ScanFilesAsync([gameId], cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            if (!_requests.TryCommit(generation, () => ApplyFiles(index)))
            {
                // 旧代次（已切游戏 / 又点了一次刷新）：结果整批丢弃，绝不写进列表。
                return;
            }

            if (index.Files.Count == 0)
            {
                return;
            }

            await LoadDetailsAsync(generation, index.Files, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _requests.TryCommit(
                generation,
                () =>
                {
                    // 扫描失败按空状态处理：不弹框、不崩页面，原因写日志。
                    _logger.Warn("扫描存档失败 (" + gameId + "): " + ex.Message);
                    Saves.Clear();
                    _lastScanAt = DateTimeOffset.Now;
                    _state = SavesState.Failed;
                    NotifyStateChanged();
                });
        }
        finally
        {
            _requests.TryCommit(
                generation,
                () =>
                {
                    IsBusy = false;
                    NotifyStateChanged();
                });
        }
    }

    /// <summary>
    /// 第一段落盘：一次性铺粗略行并进入 <see cref="SavesState.Reading"/>。
    /// 此时列表已经可滚动，细节还在后台补。
    /// </summary>
    private void ApplyFiles(SaveFileScanResult index)
    {
        foreach (var file in index.Files)
        {
            Saves.Add(new SaveGameRowViewModel(file, Saves.Count));
        }

        _processed = 0;
        _total = index.Files.Count;

        var hasDirectory = index.Locations.Any(static location => location.Exists);
        if (!hasDirectory)
        {
            _lastScanAt = DateTimeOffset.Now;
            _state = SavesState.NoDirectory;
        }
        else if (Saves.Count == 0)
        {
            _lastScanAt = DateTimeOffset.Now;
            _state = SavesState.Empty;
        }
        else
        {
            _state = SavesState.Reading;
        }

        NotifyStateChanged();
    }

    /// <summary>
    /// 第二段：按批解析并按 <see cref="SaveFileBatch{T}.StartIndex"/> 原位回填。
    /// 每批开始前检查取消与代次，切游戏后旧任务立即停；每批只改已有行对象的属性，
    /// 不触发 <see cref="ObservableCollection{T}.Clear"/>，也不重新 Add 行。
    /// </summary>
    private async Task LoadDetailsAsync(
        int generation,
        IReadOnlyList<SaveFileEntry> files,
        CancellationToken cancellationToken)
    {
        foreach (var batch in SaveScanBatching.Split(files))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metas = await Task.Run(
                () => ParseBatch(batch.Items, cancellationToken),
                cancellationToken).ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();

            var start = batch.StartIndex;
            if (!_requests.TryCommit(
                    generation,
                    () =>
                    {
                        for (var i = 0; i < metas.Length; i++)
                        {
                            if (metas[i] is { } meta && start + i < Saves.Count)
                            {
                                Saves[start + i].ApplyDetail(meta);
                            }
                        }

                        _processed += metas.Length;
                        NotifyStateChanged();
                    }))
            {
                return;
            }
        }

        _requests.TryCommit(
            generation,
            () =>
            {
                _state = SavesState.Ready;
                _lastScanAt = DateTimeOffset.Now;
                NotifyStateChanged();
            });
    }

    /// <summary>一批内并行解析（上限 4，与全量扫描一致），失败的单条留空等下一轮刷新。</summary>
    private SaveGameMeta?[] ParseBatch(IReadOnlyList<SaveFileEntry> batch, CancellationToken cancellationToken)
    {
        var results = new SaveGameMeta?[batch.Count];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = SaveGameScanner.MaxDegreeOfParallelism,
            CancellationToken = cancellationToken,
        };

        Parallel.For(
            0,
            batch.Count,
            options,
            i =>
            {
                try
                {
                    results[i] = _library.ParseFile(batch[i]);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // 解析器本身承诺不抛；这里兜底，避免一条坏档把整页打成失败空态。
                    _logger.Warn("解析存档细节失败，保留粗略行：" + batch[i].FilePath + "：" + ex.Message);
                }
            });

        return results;
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
        Reading,
        NoDirectory,
        Empty,
        Failed,
        Ready,
    }
}


