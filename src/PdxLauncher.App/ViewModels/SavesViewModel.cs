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
    private readonly ISaveGameBackupService _backups;
    private readonly IAppLogger _logger;
    private readonly SaveScanRequestTracker _requests = new();

    private GameInstallation? _game;
    private bool _scanBusy;
    private int _actionCount;
    private int _backupLoadVersion;
    private SavesState _state = SavesState.NoGame;
    private DateTimeOffset? _lastScanAt;
    private int _processed;
    private int _total;
    private SaveBackupUsage _usage = SaveBackupUsage.Empty;
    private string _noticeText = string.Empty;
    private bool _noticeIsError;

    public SavesViewModel(ISaveGameFileIndex library, ISaveGameBackupService backups, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(logger);
        _library = library;
        _backups = backups;
        _logger = logger;
    }

    /// <summary>当前游戏的存档行；切游戏或重新扫描时整体清空，第二段只原位补细节。</summary>
    public ObservableCollection<SaveGameRowViewModel> Saves { get; } = new();

    /// <summary>当前游戏的备份条目（按服务返回的时间降序）；切游戏/刷新时整体重建。</summary>
    public ObservableCollection<SaveBackupRowViewModel> Backups { get; } = new();

    public GameInstallation? Game
    {
        get => _game;
        private set
        {
            if (SetProperty(ref _game, value))
            {
                OnPropertyChanged(nameof(GameTitle));
                OnPropertyChanged(nameof(CanRefresh));
                OnPropertyChanged(nameof(HasGame));
                OnPropertyChanged(nameof(ShowBackupEmptyState));
                OnPropertyChanged(nameof(IsExperimentalGame));
            }
        }
    }

    public string GameTitle => Game?.DisplayName ?? T(Keys.SavesNoGame);

    /// <summary>
    /// 元数据解析是启发式的游戏（V3 命名档、EU5 部分档、CS2 的 .cok JSON 元数据）。
    /// 这些游戏的日期/名称可能不准确，存档页在页头统一提示一次；CK3 / Stellaris 不提示。
    /// </summary>
    private static readonly HashSet<string> ExperimentalGameIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "victoria3",
        "eu5",
        "cities_skylines_2",
    };

    /// <summary>当前游戏是否属于「元数据启发式解析」的游戏；控制页头提示。</summary>
    public bool IsExperimentalGame =>
        Game is { } game && ExperimentalGameIds.Contains(game.GameId);

    /// <summary>页头提示的标签（文案键 <c>SavesExperimental</c>）。</summary>
    public string ExperimentalLabelText => T(Keys.SavesExperimental);

    /// <summary>页头提示的说明（该游戏存档格式没有公开规范，日期与名称可能不准确）。</summary>
    public string ExperimentalNoticeText => T(Keys.SavesExperimentalTooltip);

    /// <summary>
    /// 扫描或写操作（备份/恢复/删除）期间为 true。写操作也计入忙碌态，
    /// 以免用户在文件操作尚未落地时刷新页面或切换游戏，导致旧结果覆盖新页面。
    /// </summary>
    public bool IsBusy => _scanBusy || Volatile.Read(ref _actionCount) > 0;

    /// <summary>没有当前游戏、正在扫描或正在写文件时不能刷新（避免重复排队/跨代次覆盖）。</summary>
    public bool CanRefresh => !IsBusy && Game is not null;

    /// <summary>未选游戏时整块备份段都隐藏（包括空状态与用量）。</summary>
    public bool HasGame => Game is not null;

    public bool HasSaves => Saves.Count > 0;

    public bool HasBackups => Backups.Count > 0;

    /// <summary>有游戏但还没有任何备份时显示「还没有备份」；扫描期间不闪。</summary>
    public bool ShowBackupEmptyState => HasGame && !HasBackups;

    /// <summary>
    /// 备份摘要：总量（跨游戏）/ 上限 · 当前游戏的条目数。
    /// 详细列表、恢复、删除与配额调整都在「备份」子页。
    /// </summary>
    public string BackupUsageText => T(
        Keys.SavesBackupUsage,
        SizeFormatter.Format(_usage.TotalBytes),
        SizeFormatter.Format(_backups.Policy.MaxTotalBytes),
        Backups.Count);

    /// <summary>页内提示（成功还是失败由 <see cref="ShowErrorNotice"/> 决定）；InfoBar 关闭时清空。</summary>
    public string NoticeText => _noticeText;

    public bool HasNotice => _noticeText.Length > 0;

    public bool ShowSuccessNotice => HasNotice && !_noticeIsError;

    public bool ShowErrorNotice => HasNotice && _noticeIsError;

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
        Backups.Clear();
        _usage = SaveBackupUsage.Empty;
        _lastScanAt = null;
        _processed = 0;
        _total = 0;

        if (game is null)
        {
            SetScanBusy(false);
            _state = SavesState.NoGame;
            NotifyStateChanged();
            return;
        }

        var gameId = game.GameId;
        SetScanBusy(true);
        _state = SavesState.Scanning;
        NotifyStateChanged();

        try
        {
            // 备份段与存档扫描互不依赖：先读 sidecar（只枚举几十个小文件），失败只记日志，
            // 不能让「备份目录不可读」把整页打成失败空态。
            await LoadBackupsAsync(game, cancellationToken).ConfigureAwait(true);

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
                    SetScanBusy(false);
                    NotifyStateChanged();
                });
        }
    }

    // ------------------------------------------------------------ 写操作（备份 / 恢复 / 删除）

    /// <summary>
    /// 备份一条存档。文件复制与哈希都在服务端线程池上完成；本方法只负责忙碌态、提示与刷新摘要。
    /// 超出配额时由服务按当前策略自动清理最旧备份；清理份数随成功结果返回并写入提示。
    /// </summary>
    public async Task BackupAsync(SaveGameRowViewModel row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (Game is not { } game || IsBusy || row.IsBusy)
        {
            return;
        }

        var generation = _requests.Current;
        BeginAction();
        row.SetBusy(true);
        try
        {
            var result = await _backups
                .BackupAsync(game.GameId, row.FilePath, row.Detail, cancellationToken)
                .ConfigureAwait(true);

            if (result.IsSuccess)
            {
                var name = result.Entry?.OriginalFileName ?? row.FileName;
                if (!TryCommitAction(
                        generation,
                        game,
                        () => SetNotice(
                            result.AutoPrunedEntries > 0
                                ? T(Keys.SavesBackupAutoPruned, name, result.AutoPrunedEntries)
                                : T(Keys.SavesNoticeBackedUp, name),
                            isError: false)))
                {
                    return;
                }

                await LoadBackupsAsync(game, cancellationToken).ConfigureAwait(true);
                return;
            }

            if (!TryCommitAction(
                    generation,
                    game,
                    () => SetNotice(MapBackupFailure(result), isError: true)))
            {
                return;
            }

            if (result.Status == SaveBackupStatus.SourceMissing)
            {
                await LoadAsync(game, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TryCommitAction(
                generation,
                game,
                () =>
                {
                    _logger.Warn("备份存档失败：" + row.FilePath + "：" + ex.Message);
                    SetNotice(ex.Message, isError: true);
                });
        }
        finally
        {
            row.SetBusy(false);
            EndAction();
        }
    }

    /// <summary>
    /// 删除真实存档：服务只把它移入 <c>trash\&lt;game&gt;</c>，绝不直接删文件。
    /// 成功后行立即消失并重扫（磁盘仍为真源）；已被外部删除时提示并刷新，不抛出。
    /// </summary>
    public async Task DeleteSaveAsync(SaveGameRowViewModel row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (Game is not { } game || IsBusy || row.IsBusy)
        {
            return;
        }

        var generation = _requests.Current;
        BeginAction();
        row.SetBusy(true);
        try
        {
            var result = await _backups
                .DeleteSaveAsync(game.GameId, row.FilePath, cancellationToken)
                .ConfigureAwait(true);

            if (result.IsSuccess)
            {
                if (!TryCommitAction(
                        generation,
                        game,
                        () =>
                        {
                            Saves.Remove(row);
                            NotifyStateChanged();
                            SetNotice(
                                T(Keys.SavesNoticeDeletedSave, result.TrashedPath ?? row.FilePath),
                                isError: false);
                        }))
                {
                    return;
                }

                await LoadAsync(game, cancellationToken).ConfigureAwait(true);
                return;
            }

            if (!TryCommitAction(
                    generation,
                    game,
                    () => SetNotice(MapDeleteFailure(result), isError: true)))
            {
                return;
            }

            if (result.Status == SaveDeleteStatus.NotFound)
            {
                await LoadAsync(game, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TryCommitAction(
                generation,
                game,
                () =>
                {
                    _logger.Warn("删除存档失败：" + row.FilePath + "：" + ex.Message);
                    SetNotice(ex.Message, isError: true);
                });
        }
        finally
        {
            row.SetBusy(false);
            EndAction();
        }
    }

    /// <summary>InfoBar 关闭时清空页内提示。</summary>
    public void DismissNotice()
    {
        if (_noticeText.Length == 0)
        {
            return;
        }

        _noticeText = string.Empty;
        _noticeIsError = false;
        NotifyNoticeChanged();
    }

    /// <summary>
    /// 失败文案映射：配额与「目标已不存在」这两类有稳定的用户可执行动作，用本地化键；
    /// 其余（IO、校验失败等）直接显示服务返回的 Message，避免把具体原因吞成一句「失败」。
    /// </summary>
    private static string MapBackupFailure(SaveBackupResult result) => result.Status switch
    {
        SaveBackupStatus.QuotaExceeded => T(Keys.SavesBackupQuotaFull),
        SaveBackupStatus.SourceMissing => T(Keys.SavesNoticeSourceMissing),
        _ => result.Message,
    };

    private static string MapDeleteFailure(SaveDeleteResult result) => result.Status switch
    {
        SaveDeleteStatus.NotFound => T(Keys.SavesNoticeSourceMissing),
        _ => result.Message,
    };

    /// <summary>
    /// 重新读取当前游戏的备份列表与用量；失败只记日志，不动存档列表。
    /// 读盘期间可能已经切游戏或又发起了一次刷新，因此写回前必须同时校验游戏身份、
    /// 扫描代次和本次备份读取序号，旧结果一律丢弃，不能覆盖新游戏的备份段。
    /// </summary>
    private async Task LoadBackupsAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        var gameId = game.GameId;
        var generation = _requests.Current;
        var version = ++_backupLoadVersion;

        try
        {
            var entries = await _backups.ListBackupsAsync(gameId, cancellationToken).ConfigureAwait(true);
            var usage = await _backups.GetUsageAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryCommitBackup(
                    generation,
                    game,
                    version,
                    () =>
                    {
                        Backups.Clear();
                        for (var i = 0; i < entries.Count; i++)
                        {
                            Backups.Add(new SaveBackupRowViewModel(entries[i], i));
                        }

                        _usage = usage;
                    }))
            {
                return;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!TryCommitBackup(
                    generation,
                    game,
                    version,
                    () =>
                    {
                        // 备份目录不可读不应把整页打成失败：清空备份段并保留日志。
                        _logger.Warn("读取存档备份失败 (" + gameId + ")：" + ex.Message);
                        Backups.Clear();
                        _usage = SaveBackupUsage.Empty;
                    }))
            {
                return;
            }
        }
        finally
        {
            TryCommitBackup(generation, game, version, NotifyBackupStateChanged);
        }
    }

    /// <summary>
    /// 写操作的提交口：代次仍是当前、且页面仍停在同一款游戏时才执行 UI 写入。
    /// 操作期间用户切游戏/刷新后，完成回调不能把旧游戏的行、备份或提示写进新页面。
    /// </summary>
    private bool TryCommitAction(int generation, GameInstallation game, Action commit)
    {
        var applied = false;
        _requests.TryCommit(
            generation,
            () =>
            {
                if (ReferenceEquals(Game, game))
                {
                    commit();
                    applied = true;
                }
            });
        return applied;
    }

    private bool TryCommitBackup(
        int generation,
        GameInstallation game,
        int version,
        Action commit)
    {
        var applied = false;
        _requests.TryCommit(
            generation,
            () =>
            {
                if (version == _backupLoadVersion && ReferenceEquals(Game, game))
                {
                    commit();
                    applied = true;
                }
            });
        return applied;
    }

    private void SetNotice(string text, bool isError)
    {
        _noticeText = text ?? string.Empty;
        _noticeIsError = isError && _noticeText.Length > 0;
        NotifyNoticeChanged();
    }

    private void NotifyNoticeChanged()
    {
        OnPropertyChanged(nameof(NoticeText));
        OnPropertyChanged(nameof(HasNotice));
        OnPropertyChanged(nameof(ShowSuccessNotice));
        OnPropertyChanged(nameof(ShowErrorNotice));
    }

    private void SetScanBusy(bool value)
    {
        if (_scanBusy == value)
        {
            return;
        }

        _scanBusy = value;
        NotifyBusyChanged();
    }

    private void BeginAction()
    {
        _actionCount++;
        NotifyBusyChanged();
    }

    private void EndAction()
    {
        if (_actionCount > 0)
        {
            _actionCount--;
        }

        NotifyBusyChanged();
    }

    private void NotifyBusyChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanRefresh));
        NotifyBackupStateChanged();
    }

    private void NotifyBackupStateChanged()
    {
        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(ShowBackupEmptyState));
        OnPropertyChanged(nameof(BackupUsageText));

        // 备份按钮只受忙碌态约束；配额由服务按策略自动清理。
        ApplyRowAvailability();
    }

    /// <summary>把全局忙碌态下发给所有行；行自身的 IsBusy 仍由各自操作维护。</summary>
    private void ApplyRowAvailability()
    {
        var actionAllowed = !IsBusy;
        foreach (var row in Saves)
        {
            row.SetBackupAllowed(actionAllowed);
            row.SetActionAllowed(actionAllowed);
        }

        foreach (var row in Backups)
        {
            row.SetActionAllowed(actionAllowed);
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
        OnPropertyChanged(nameof(HasGame));
        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(ShowBackupEmptyState));
        OnPropertyChanged(nameof(BackupUsageText));

        // 扫描/写操作期间整页忙碌：备份、删除按钮都随之下发禁用。
        ApplyRowAvailability();
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


