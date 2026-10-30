using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Playsets.Json;
using PdxLauncher.Core.Playsets.PlaysetConfig;

namespace PdxLauncher.App.Services;

/// <summary>
/// 按游戏的存档格式把 <see cref="IPlaySetRepository"/> 路由到具体实现：
/// <see cref="LoadFileFormat.PlaysetsJsonV1"/>（EU5）走 playsets.json，
/// <see cref="LoadFileFormat.PlaysetConfigV1"/>（CS2）走 playset_config.json，
/// 其余走 sqlite——官方 <c>launcher-v2.sqlite</c> 在就用它，不在就用自有库。
/// <para>
/// 这样上层（ModLibraryService / PlaySetEditor / ModsViewModel）一行都不用判格式，
/// 判定只发生在这里一处；新增格式时只需要在这里加一个分支。
/// </para>
/// </summary>
public sealed class PlaySetRepositoryRouter : IPlaySetRepository
{
    private readonly IPlaySetRepository _sqlite;
    private readonly IPlaySetRepository _playsetsJson;
    private readonly IPlaySetRepository _playsetConfig;
    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly IPlaySetRepository? _own;
    private readonly LauncherDbProjector? _projector;

    /// <summary>
    /// 创建路由。
    /// </summary>
    /// <param name="sqlite">launcher-v2.sqlite 实现（EU4/CK3/Vic3 等）。</param>
    /// <param name="playsetsJson">playsets.json 实现（EU5）。</param>
    /// <param name="playsetConfig">playset_config.json 实现（CS2）。</param>
    /// <param name="fileSystem">用于判定「官方是否换了存储文件」。</param>
    /// <param name="logger">兜底命中时留一条 Warn。</param>
    /// <param name="own">自有库实现；官方库不存在时接管 sqlite 格式的游戏。null 时退化为旧行为。</param>
    /// <param name="projector">官方库 ↔ 自有库搬运器；null 时不做同步。</param>
    public PlaySetRepositoryRouter(
        IPlaySetRepository sqlite,
        IPlaySetRepository playsetsJson,
        IPlaySetRepository playsetConfig,
        IFileSystem fileSystem,
        IAppLogger logger,
        IPlaySetRepository? own = null,
        LauncherDbProjector? projector = null)
    {
        ArgumentNullException.ThrowIfNull(sqlite);
        ArgumentNullException.ThrowIfNull(playsetsJson);
        ArgumentNullException.ThrowIfNull(playsetConfig);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        _sqlite = sqlite;
        _playsetsJson = playsetsJson;
        _playsetConfig = playsetConfig;
        _fileSystem = fileSystem;
        _logger = logger;
        _own = own;
        _projector = projector;
    }

    /// <summary>按格式挑实现。</summary>
    public IPlaySetRepository For(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Format switch
        {
            LoadFileFormat.PlaysetsJsonV1 => _playsetsJson,
            LoadFileFormat.PlaysetConfigV1 => _playsetConfig,
            _ => ResolveSqliteOrFallback(game),
        };
    }

    /// <summary>
    /// 官方把某个 sqlite 游戏迁到新存储格式时，不能让播放集页静默空白：
    /// 数据库文件不存在、而官方 JSON 文档在，就临时按 JSON 读，并留一条 Warn 方便日后定位。
    /// 数据库存在但缺表仍走 sqlite（读空 + Warn），不静默改判格式——那会把「空库」误当成「换格式」。
    /// 官方库与官方 JSON 都不在时用自有库：用户没装官方启动器也要能管播放集。
    /// </summary>
    private IPlaySetRepository ResolveSqliteOrFallback(GameInstallation game)
    {
        if (_fileSystem.FileExists(PlaySetRepository.GetDatabasePath(game)))
        {
            return _sqlite;
        }

        var playsetsJsonPath = Path.Combine(game.GameDataDir, PlaysetsJsonDocument.FileName);
        if (_fileSystem.FileExists(playsetsJsonPath))
        {
            _logger.Warn("未找到 launcher-v2.sqlite，但存在 " + playsetsJsonPath
                + "，按 playsets.json 兜底读取（gameId=" + game.GameId + "）");
            return _playsetsJson;
        }

        var playsetConfigPath = PlaysetConfigPaths.GetConfigPath(game);
        if (_fileSystem.FileExists(playsetConfigPath))
        {
            _logger.Warn("未找到 launcher-v2.sqlite，但存在 " + playsetConfigPath
                + "，按 playset_config.json 兜底读取（gameId=" + game.GameId + "）");
            return _playsetConfig;
        }

        if (_own is not null)
        {
            _logger.Info("未找到 launcher-v2.sqlite，改用自有库（gameId=" + game.GameId + "）");
            return _own;
        }

        return _sqlite;
    }

    /// <summary>这次操作打的是不是官方库（只有官方库才需要「写后镜像进自有库」）。</summary>
    private bool TargetsOfficial(GameInstallation game, IPlaySetRepository store) =>
        ReferenceEquals(store, _sqlite) && _fileSystem.FileExists(PlaySetRepository.GetDatabasePath(game));

    private async Task<T> ReadAsync<T>(
        GameInstallation game,
        Func<IPlaySetRepository, Task<T>> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(body);

        // 读之前对齐一次两边：自有库有未投射改动就推回官方库，官方库变新了就先抄进自有库。
        if (_projector is not null
            && game.Format is not (LoadFileFormat.PlaysetsJsonV1 or LoadFileFormat.PlaysetConfigV1))
        {
            await _projector.PrepareForReadAsync(game, cancellationToken).ConfigureAwait(false);
        }

        return await body(For(game)).ConfigureAwait(false);
    }

    private async Task<AppResult<T>> WriteAsync<T>(
        GameInstallation game,
        Func<IPlaySetRepository, Task<AppResult<T>>> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(body);

        var store = For(game);
        var result = await body(store).ConfigureAwait(false);
        if (!result.IsSuccess || _projector is null)
        {
            return result;
        }

        if (TargetsOfficial(game, store))
        {
            // 官方库写成功了：把结果抄进自有库，官方库哪天不在也不丢。
            await _projector.MirrorFromOfficialAsync(game, cancellationToken).ConfigureAwait(false);
        }
        else if (ReferenceEquals(store, _own))
        {
            // 自有库写成功了：官方库在就立刻投射，不在就记下待投射。
            await _projector.SyncAfterOwnWriteAsync(game, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySet>> GetPlaySetsAsync(GameInstallation game, CancellationToken cancellationToken) =>
        ReadAsync(game, store => store.GetPlaySetsAsync(game, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<PlaySet?> GetActivePlaySetAsync(GameInstallation game, CancellationToken cancellationToken) =>
        ReadAsync(game, store => store.GetActivePlaySetAsync(game, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySetMod>> GetPlaySetModsAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken) =>
        ReadAsync(game, store => store.GetPlaySetModsAsync(game, playSetId, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ModLibraryEntry>> GetModLibraryAsync(
        GameInstallation game,
        CancellationToken cancellationToken) =>
        ReadAsync(game, store => store.GetModLibraryAsync(game, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySetDlc>> GetPlaySetDlcsAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken) =>
        ReadAsync(game, store => store.GetPlaySetDlcsAsync(game, playSetId, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<AppResult<bool>> UpdateModPathsAsync(
        GameInstallation game,
        Guid modId,
        string? dirPath,
        string? archivePath,
        CancellationToken cancellationToken)
        => WriteAsync(
            game,
            store => store.UpdateModPathsAsync(game, modId, dirPath, archivePath, cancellationToken),
            cancellationToken);

    public Task<AppResult<bool>> SavePlaySetModsAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetMod> mods,
        CancellationToken cancellationToken) =>
        WriteAsync(game, store => store.SavePlaySetModsAsync(game, playSetId, mods, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<AppResult<bool>> SavePlaySetDlcsAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetDlc> dlcs,
        CancellationToken cancellationToken) =>
        WriteAsync(game, store => store.SavePlaySetDlcsAsync(game, playSetId, dlcs, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<AppResult<Guid>> CreatePlaySetAsync(GameInstallation game, string name, CancellationToken cancellationToken) =>
        WriteAsync(game, store => store.CreatePlaySetAsync(game, name, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<AppResult<bool>> SetActivePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
        WriteAsync(game, store => store.SetActivePlaySetAsync(game, playSetId, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<AppResult<bool>> RenamePlaySetAsync(
        GameInstallation game,
        Guid playSetId,
        string newName,
        CancellationToken cancellationToken) =>
        WriteAsync(game, store => store.RenamePlaySetAsync(game, playSetId, newName, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<AppResult<bool>> DeletePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
        WriteAsync(game, store => store.DeletePlaySetAsync(game, playSetId, cancellationToken), cancellationToken);
}
