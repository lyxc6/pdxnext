namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Saves;

/// <summary>
/// 「备份」子页的视图模型：只管理当前游戏的备份（列表 / 恢复 / 覆盖恢复 / 删除 / 清理 / 用量）。
/// <para>
/// 页面不缓存，每次进入按传入的游戏重新读一遍；读盘与写操作期间 <see cref="IsBusy"/> 为 true，
/// 行按钮随之禁用，避免确认后静默 no-op。代次守卫复用 <see cref="SaveScanRequestTracker"/>：
/// 旧游戏/旧刷新返回的结果不能覆盖新页面的列表。
/// </para>
/// </summary>
public sealed class SaveBackupsViewModel : LocalizedObservableObject
{
    private readonly ISaveGameBackupService _backups;
    private readonly IAppLogger _logger;
    private readonly SaveScanRequestTracker _requests = new();

    private GameInstallation? _game;
    private bool _loadBusy;
    private int _actionCount;
    private SaveBackupUsage _usage = SaveBackupUsage.Empty;
    private string _noticeText = string.Empty;
    private bool _noticeIsError;

    public SaveBackupsViewModel(ISaveGameBackupService backups, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(logger);
        _backups = backups;
        _logger = logger;
    }

    /// <summary>当前游戏的备份条目（按服务返回的创建时间降序）。</summary>
    public ObservableCollection<SaveBackupRowViewModel> Backups { get; } = new();

    public GameInstallation? Game
    {
        get => _game;
        private set
        {
            if (SetProperty(ref _game, value))
            {
                OnPropertyChanged(nameof(GameTitle));
                OnPropertyChanged(nameof(HasGame));
                OnPropertyChanged(nameof(CanRefresh));
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(CanPrune));
            }
        }
    }

    public string GameTitle => Game?.DisplayName ?? T(Keys.SavesNoGame);

    public bool HasGame => Game is not null;

    public bool HasBackups => Backups.Count > 0;

    /// <summary>有游戏且没有备份时显示空状态。</summary>
    public bool ShowEmptyState => HasGame && !HasBackups;

    /// <summary>读列表或写操作期间为 true；期间所有行按钮与清理入口都禁用。</summary>
    public bool IsBusy => _loadBusy || Volatile.Read(ref _actionCount) > 0;

    public bool CanRefresh => !IsBusy && HasGame;

    public bool CanPrune => HasGame && !IsBusy && HasBackups;

    /// <summary>显式清理时保留的最新备份数；只有用户点「清理旧备份」才会批量删除。</summary>
    public const int PruneKeepNewest = 10;

    /// <summary>当前配额上限（字节）；随设置变化，用于页面上的 GB 输入框。</summary>
    public long QuotaBytes => _backups.Policy.MaxTotalBytes;

    /// <summary>当前游戏的条目数上限（服务策略）。</summary>
    public int MaxEntriesPerGame => _backups.Policy.MaxEntriesPerGame;

    /// <summary>用量行：总量（跨游戏）/ 上限 · 当前游戏条目数。</summary>
    public string BackupUsageText => T(
        Keys.SavesBackupUsage,
        SizeFormatter.Format(_usage.TotalBytes),
        SizeFormatter.Format(_backups.Policy.MaxTotalBytes),
        Backups.Count);

    /// <summary>页内提示（成功还是失败由 <see cref="ShowErrorNotice"/> 决定）。</summary>
    public string NoticeText => _noticeText;

    public bool HasNotice => _noticeText.Length > 0;

    public bool ShowSuccessNotice => HasNotice && !_noticeIsError;

    public bool ShowErrorNotice => HasNotice && _noticeIsError;

    /// <summary>读取当前游戏的备份列表与全局用量。</summary>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        var generation = _requests.Begin();

        Game = game;
        Backups.Clear();
        _usage = SaveBackupUsage.Empty;
        NotifyBackupStateChanged();

        if (game is null)
        {
            SetLoadBusy(false);
            return;
        }

        SetLoadBusy(true);
        try
        {
            var entries = await _backups.ListBackupsAsync(game.GameId, cancellationToken).ConfigureAwait(true);
            var usage = await _backups.GetUsageAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            _requests.TryCommit(
                generation,
                () =>
                {
                    if (!ReferenceEquals(Game, game))
                    {
                        return;
                    }

                    Backups.Clear();
                    for (var i = 0; i < entries.Count; i++)
                    {
                        Backups.Add(new SaveBackupRowViewModel(entries[i], i));
                    }

                    _usage = usage;
                    NotifyBackupStateChanged();
                });
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
                    if (!ReferenceEquals(Game, game))
                    {
                        return;
                    }

                    // 备份目录不可读只清空列表并记日志，不把整页打成错误。
                    _logger.Warn("读取存档备份失败 (" + game.GameId + ")：" + ex.Message);
                    Backups.Clear();
                    _usage = SaveBackupUsage.Empty;
                    NotifyBackupStateChanged();
                });
        }
        finally
        {
            _requests.TryCommit(generation, () => SetLoadBusy(false));
        }
    }

    /// <summary>只重读用量（配额变化后调用），不动列表。</summary>
    public async Task RefreshUsageAsync(CancellationToken cancellationToken)
    {
        if (!HasGame)
        {
            return;
        }

        try
        {
            _usage = await _backups.GetUsageAsync(cancellationToken).ConfigureAwait(true);
            NotifyBackupStateChanged();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("读取存档备份用量失败：" + ex.Message);
        }
    }

    /// <summary>恢复备份。<paramref name="overwrite"/> 为 true 时先把同名存档移入回收站再写回原路径。</summary>
    public async Task RestoreAsync(
        SaveBackupRowViewModel row,
        bool overwrite,
        CancellationToken cancellationToken)
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
                .RestoreAsync(row.Entry, overwrite, cancellationToken)
                .ConfigureAwait(true);

            if (result.IsSuccess)
            {
                var restoredName = result.RestoredPath is { } restoredPath
                    ? Path.GetFileName(restoredPath)
                    : row.FileName;
                TryCommitAction(
                    generation,
                    game,
                    () => SetNotice(T(Keys.SavesNoticeRestored, restoredName), isError: false));
                return;
            }

            TryCommitAction(generation, game, () => SetNotice(result.Message, isError: true));
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
                    _logger.Warn("恢复备份失败：" + row.FilePath + "：" + ex.Message);
                    SetNotice(ex.Message, isError: true);
                });
        }
        finally
        {
            row.SetBusy(false);
            EndAction();
        }
    }

    /// <summary>删除单个备份（<c>.bak</c> + sidecar）。</summary>
    public async Task DeleteBackupAsync(SaveBackupRowViewModel row, CancellationToken cancellationToken)
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
                .DeleteBackupAsync(row.Entry, cancellationToken)
                .ConfigureAwait(true);

            if (result.IsSuccess)
            {
                if (!TryCommitAction(
                        generation,
                        game,
                        () =>
                        {
                            Backups.Remove(row);
                            NotifyBackupStateChanged();
                            SetNotice(T(Keys.SavesNoticeDeletedBackup, row.FileName), isError: false);
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
                    _logger.Warn("删除备份失败：" + row.FilePath + "：" + ex.Message);
                    SetNotice(ex.Message, isError: true);
                });
        }
        finally
        {
            row.SetBusy(false);
            EndAction();
        }
    }

    /// <summary>清理旧备份：保留最新 <see cref="PruneKeepNewest"/> 份，其余（含孤儿备份）交给服务删除。</summary>
    public async Task PruneAsync(CancellationToken cancellationToken)
    {
        if (Game is not { } game || IsBusy)
        {
            return;
        }

        var generation = _requests.Current;
        BeginAction();
        try
        {
            var result = await _backups
                .PruneAsync(game.GameId, PruneKeepNewest, cancellationToken)
                .ConfigureAwait(true);

            if (result.IsSuccess)
            {
                if (!TryCommitAction(
                        generation,
                        game,
                        () => SetNotice(T(Keys.SavesNoticePruned, result.DeletedEntries), isError: false)))
                {
                    return;
                }
            }
            else if (!TryCommitAction(
                         generation,
                         game,
                         () => SetNotice(result.Message, isError: true)))
            {
                return;
            }

            // 部分失败也会改动磁盘，必须按服务返回的剩余条目为准重读。
            await LoadAsync(game, cancellationToken).ConfigureAwait(true);
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
                    _logger.Warn("清理旧备份失败 (" + game.GameId + ")：" + ex.Message);
                    SetNotice(ex.Message, isError: true);
                });
        }
        finally
        {
            EndAction();
        }
    }

    /// <summary>配额变更后刷新用量与上限文案。</summary>
    public void NotifyQuotaChanged()
    {
        OnPropertyChanged(nameof(QuotaBytes));
        OnPropertyChanged(nameof(MaxEntriesPerGame));
        OnPropertyChanged(nameof(BackupUsageText));
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

    private static string MapDeleteFailure(SaveDeleteResult result) => result.Status switch
    {
        SaveDeleteStatus.NotFound => T(Keys.SavesNoticeSourceMissing),
        _ => result.Message,
    };

    private void NotifyBackupStateChanged()
    {
        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(BackupUsageText));
        OnPropertyChanged(nameof(CanPrune));
    }

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

    private void SetLoadBusy(bool value)
    {
        if (_loadBusy == value)
        {
            return;
        }

        _loadBusy = value;
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
        OnPropertyChanged(nameof(CanPrune));

        var allowed = !IsBusy;
        foreach (var row in Backups)
        {
            row.SetActionAllowed(allowed);
        }
    }
}
