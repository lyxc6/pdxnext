namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>快捷添加到当前激活播放集的结果类别。</summary>
public enum QuickAddToActivePlaySetStatus
{
    Added,
    AlreadyInPlaySet,
    Unregistered,
    NoGame,
    NoActivePlaySet,
    Unsupported,
    SaveFailed,
}

/// <summary>一次快捷添加的结果。</summary>
/// <param name="Status">结果类别，界面据此选择文案。</param>
/// <param name="PlaySetName">目标播放集名称；没有目标时为 null。</param>
/// <param name="ErrorDetail">保存失败时的错误码；其余情况为 null。</param>
public sealed record QuickAddToActivePlaySetResult(
    QuickAddToActivePlaySetStatus Status,
    string? PlaySetName = null,
    string? ErrorDetail = null)
{
    /// <summary>是否真的把一个 mod 加进了激活播放集。</summary>
    public bool IsSuccess => Status == QuickAddToActivePlaySetStatus.Added;

    /// <summary>保存 / 写加载文件是否失败（界面统一报失败）。</summary>
    public bool IsFailure => Status == QuickAddToActivePlaySetStatus.SaveFailed;
}

/// <summary>
/// Mod 库行右键「添加到当前播放集」的业务逻辑：只改当前激活播放集，追加到末尾并默认启用。
/// <para>
/// 依赖全部通过构造注入，UI 只传游戏和 <see cref="ModLibraryEntry"/>；这样无游戏、未登记、
/// 重复、保存失败与并发点击都能脱离 XAML 自检。并发通过 SemaphoreSlim 串行，避免两次调用
/// 都基于同一份旧列表而丢掉其中一次添加。
/// </para>
/// </summary>
public sealed class QuickAddToActivePlaySetService
{
    private readonly IPlaySetRepository _playSets;
    private readonly ILoadFileWriter _loadFiles;
    private readonly IDlcCatalog _dlcs;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public QuickAddToActivePlaySetService(
        IPlaySetRepository playSets,
        ILoadFileWriter loadFiles,
        IDlcCatalog dlcs,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(playSets);
        ArgumentNullException.ThrowIfNull(loadFiles);
        ArgumentNullException.ThrowIfNull(dlcs);
        ArgumentNullException.ThrowIfNull(logger);
        _playSets = playSets;
        _loadFiles = loadFiles;
        _dlcs = dlcs;
        _logger = logger;
    }

    /// <summary>把 <paramref name="entry"/> 追加到 <paramref name="game"/> 当前激活播放集末尾。</summary>
    public async Task<QuickAddToActivePlaySetResult> AddAsync(
        GameInstallation? game,
        ModLibraryEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (game is null)
        {
            return new QuickAddToActivePlaySetResult(QuickAddToActivePlaySetStatus.NoGame);
        }

        if (!game.SupportsContentManagement)
        {
            return new QuickAddToActivePlaySetResult(QuickAddToActivePlaySetStatus.Unsupported);
        }

        if (entry.IsUnregistered)
        {
            return new QuickAddToActivePlaySetResult(QuickAddToActivePlaySetStatus.Unregistered);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var active = await _playSets
                .GetActivePlaySetAsync(game, cancellationToken)
                .ConfigureAwait(false);
            if (active is null)
            {
                return new QuickAddToActivePlaySetResult(QuickAddToActivePlaySetStatus.NoActivePlaySet);
            }

            var mods = await _playSets
                .GetPlaySetModsAsync(game, active.Id, cancellationToken)
                .ConfigureAwait(false);
            if (mods.Any(mod => mod.ModId == entry.ModId))
            {
                return new QuickAddToActivePlaySetResult(
                    QuickAddToActivePlaySetStatus.AlreadyInPlaySet,
                    active.Name);
            }

            var appended = mods.OrderBy(static mod => mod.Position).ToList();
            appended.Add(ToPlaySetMod(entry, appended.Count));
            var ordered = PlaySetEditor.Renumber(appended);

            var savedDlcs = await _playSets
                .GetPlaySetDlcsAsync(game, active.Id, cancellationToken)
                .ConfigureAwait(false);
            var dlcCatalog = await _dlcs
                .DiscoverAsync(game, cancellationToken)
                .ConfigureAwait(false);
            var dlcs = PlaySetEditor.MergeDlcs(savedDlcs, dlcCatalog);

            var editor = new PlaySetEditor(_playSets, _loadFiles, _logger);
            var save = await editor
                .SaveAsync(game, active.Id, ordered, dlcs, dlcCatalog, cancellationToken)
                .ConfigureAwait(false);
            if (!save.IsSuccess)
            {
                _logger.Warn("快速添加 mod 到激活播放集失败（gameId=" + game.GameId + "）：" + save.ErrorMessage);
                return new QuickAddToActivePlaySetResult(
                    QuickAddToActivePlaySetStatus.SaveFailed,
                    active.Name,
                    save.ErrorMessage);
            }

            return new QuickAddToActivePlaySetResult(QuickAddToActivePlaySetStatus.Added, active.Name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("快速添加 mod 到激活播放集异常（gameId=" + game.GameId + "）", ex);
            return new QuickAddToActivePlaySetResult(
                QuickAddToActivePlaySetStatus.SaveFailed,
                null,
                ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>与「添加模组」页一致的构造口径：路径可空转空串，位置由调用方给，默认启用。</summary>
    internal static PlaySetMod ToPlaySetMod(ModLibraryEntry entry, int position) => new(
        entry.ModId,
        entry.Name,
        entry.DirPath ?? string.Empty,
        entry.ArchivePath,
        entry.GameRegistryId,
        entry.ThumbnailPath,
        entry.RequiredVersion,
        entry.Status,
        true,
        position,
        entry.SteamId,
        entry.Source,
        entry.ModVersion,
        entry.PdxId);
}
