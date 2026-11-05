namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
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
///   <item>第二段最多 4 条并行解析，已解析好的连续前缀在「满一批 / 距上次回填 ≥50 ms / 后台完成」时
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

    /// <summary>自检用：已成功回填细节的行数（解析失败不计入）。</summary>
    internal int ProcessedCount => _processed;

    /// <summary>自检用：第一段得到的行数。</summary>
    internal int TotalCount => _total;

    /// <summary>扫描/补细节期间不显示空状态，避免进度环旁边闪一下「没有存档」。</summary>
    public bool ShowEmptyState => _state is not (SavesState.Scanning or SavesState.Reading or SavesState.Ready);

    /// <summary>顶部扫描状态：扫描中 / 正在读取 n / N / 上次扫描时间 + 找到 N 个存档 / 失败 / 未选游戏。</summary>
    public string StatusText => _state switch
    {
        SavesState.Scanning => T(Keys.SavesStatusScanning),
        // 0 行时没有「读取 0 / 0」这种无意义进度；第一段没铺出任何行前保持扫描文案。
        SavesState.Reading when _total > 0 => T(Keys.SavesStatusReading, _processed, _total),
        SavesState.Reading => T(Keys.SavesStatusScanning),
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
            // 细节阶段取消：不能把半成品骨架留在页面上。清空行与进度，
            // IsBusy 由 finally 统一关闭；页面已卸载时属于正常收尾。
            _requests.TryCommit(
                generation,
                () =>
                {
                    Saves.Clear();
                    _processed = 0;
                    _total = 0;
                    _state = SavesState.Scanning;
                    NotifyStateChanged();
                });

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
    /// 第二段：最多 4 条并行解析；已解析好的<b>连续前缀</b>在满一批、距上次回填 ≥50 ms
    /// 或后台已完成时回填。回填只改已有行对象（<see cref="SaveGameRowViewModel.ApplyDetail"/>），
    /// 不触发 <see cref="ObservableCollection{T}.Clear"/>，也不重新 Add 行；
    /// 解析失败的行留在骨架态，进度只统计成功回填的行。
    /// </summary>
    private async Task LoadDetailsAsync(
        int generation,
        IReadOnlyList<SaveFileEntry> files,
        CancellationToken cancellationToken)
    {
        var count = files.Count;
        var results = new SaveGameMeta?[count];
        var finished = new bool[count];

        // 每完成一条释放一次；最后再释放一次表示生产者退出。容量多加 1，避免取消时超限。
        var signal = new SemaphoreSlim(0, count + 1);

        var producer = Task.Run(
            async () =>
            {
                try
                {
                    await Parallel.ForEachAsync(
                        Enumerable.Range(0, count),
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = SaveGameScanner.MaxDegreeOfParallelism,
                            CancellationToken = cancellationToken,
                        },
                        (index, token) =>
                        {
                            token.ThrowIfCancellationRequested();
                            try
                            {
                                results[index] = _library.ParseFile(files[index]);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                // 解析器本身承诺不抛；这里兜底，避免一条坏档把整页打成失败空态。
                                _logger.Warn("解析存档细节失败，保留粗略行：" + files[index].FilePath + "：" + ex.Message);
                            }
                            finally
                            {
                                Volatile.Write(ref finished[index], true);
                                signal.Release();
                            }

                            return ValueTask.CompletedTask;
                        }).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 取消由消费者抛出 OCE 收尾；生产者不再向上抛，避免未观察异常。
                }
                catch (Exception ex)
                {
                    _logger.Warn("解析存档细节批次失败：" + ex.Message);
                }
                finally
                {
                    signal.Release();
                }
            },
            CancellationToken.None);

        var next = 0;
        var sinceFlush = Stopwatch.StartNew();

        while (next < count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pending = CountFinishedPrefix(finished, next);
            if (pending > 0
                && SaveScanBatching.ShouldFlush(pending, sinceFlush.Elapsed, producer.IsCompleted))
            {
                if (!ApplyDetailPrefix(generation, results, next, pending))
                {
                    return;
                }

                next += pending;
                sinceFlush.Restart();
                continue;
            }

            if (producer.IsCompleted)
            {
                // 生产者的每条结果都写了 finished；走到这里说明剩下的行不会再有结果。
                await producer.ConfigureAwait(true);
                break;
            }

            await signal
                .WaitAsync(
                    TimeSpan.FromMilliseconds(SaveScanBatching.DefaultFlushIntervalMilliseconds),
                    cancellationToken)
                .ConfigureAwait(true);
        }

        await producer.ConfigureAwait(true);

        _requests.TryCommit(
            generation,
            () =>
            {
                _state = SavesState.Ready;
                _lastScanAt = DateTimeOffset.Now;
                NotifyStateChanged();
            });
    }

    /// <summary>从 <paramref name="start"/> 起有多少条连续完成的结果。</summary>
    private static int CountFinishedPrefix(bool[] finished, int start)
    {
        var length = 0;
        while (start + length < finished.Length && Volatile.Read(ref finished[start + length]))
        {
            length++;
        }

        return length;
    }

    /// <summary>把一段连续结果回填到对应行；代次已过期时整批丢弃并返回 false。</summary>
    private bool ApplyDetailPrefix(int generation, SaveGameMeta?[] results, int start, int count)
    {
        return _requests.TryCommit(
            generation,
            () =>
            {
                var applied = 0;
                for (var i = 0; i < count; i++)
                {
                    var index = start + i;
                    if (index >= Saves.Count || results[index] is not { } meta)
                    {
                        continue;
                    }

                    if (Saves[index].ApplyDetail(meta))
                    {
                        applied++;
                    }
                }

                // 只有真正解析成功并回填的行才计入进度；失败行不会让 n / N 停住。
                _processed += applied;
                NotifyStateChanged();
            });
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


