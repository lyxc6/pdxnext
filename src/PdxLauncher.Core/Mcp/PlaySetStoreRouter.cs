namespace PdxLauncher.Core.Mcp;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Playsets.Json;
using PdxLauncher.Core.Playsets.PlaysetConfig;
using PdxLauncher.Core.Storage;

/// <summary>
/// MCP 侧与启动器页面一致的播放集仓储路由；按游戏格式分派，官方库缺席时用自有库。
/// </summary>
public sealed class PlaySetStoreRouter : IPlaySetRepository
{
    private readonly IPlaySetRepository _sqlite;
    private readonly IPlaySetRepository _playsetsJson;
    private readonly IPlaySetRepository _playsetConfig;
    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly IPlaySetRepository? _own;
    private readonly LauncherDbProjector? _projector;

    public PlaySetStoreRouter(
        IFileSystem fileSystem,
        IAppLogger logger,
        IClock clock,
        PdxDatabase database,
        IModCatalog mods,
        IDlcCatalog dlcs,
        ModThumbnailResolver? thumbnails = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        thumbnails ??= new ModThumbnailResolver(fileSystem, logger);
        _sqlite = new PlaySetRepository(fileSystem, logger, clock, thumbnails);
        _playsetsJson = new PlaysetsJsonRepository(fileSystem, logger, mods, dlcs);
        _playsetConfig = new PlaysetConfigRepository(fileSystem, logger, clock);
        _own = new OwnDatabasePlaySetRepository(database, logger, clock);
        _projector = new LauncherDbProjector(
            fileSystem,
            (PlaySetRepository)_sqlite,
            (OwnDatabasePlaySetRepository)_own,
            database,
            logger);
    }

    private IPlaySetRepository For(GameInstallation game)
    {
        if (game.Format == LoadFileFormat.PlaysetsJsonV1)
        {
            return _playsetsJson;
        }

        if (game.Format == LoadFileFormat.PlaysetConfigV1)
        {
            return _playsetConfig;
        }

        if (_fileSystem.FileExists(PlaySetRepository.GetDatabasePath(game)))
        {
            return _sqlite;
        }

        if (_own is not null)
        {
            return _own;
        }

        return _sqlite;
    }

    private async Task<T> ReadAsync<T>(
        GameInstallation game,
        Func<IPlaySetRepository, Task<T>> body,
        CancellationToken cancellationToken)
    {
        if (_projector is not null
            && game.Format is not (LoadFileFormat.PlaysetsJsonV1 or LoadFileFormat.PlaysetConfigV1))
        {
            await _projector.PrepareForReadAsync(game, cancellationToken).ConfigureAwait(false);
        }

        return await body(For(game)).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<PlaySet>> GetPlaySetsAsync(GameInstallation game, CancellationToken cancellationToken) =>
        ReadAsync(game, store => store.GetPlaySetsAsync(game, cancellationToken), cancellationToken);

    public Task<PlaySet?> GetActivePlaySetAsync(GameInstallation game, CancellationToken cancellationToken) =>
        ReadAsync(game, store => store.GetActivePlaySetAsync(game, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<PlaySetMod>> GetPlaySetModsAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken) =>
        ReadAsync(game, store => store.GetPlaySetModsAsync(game, playSetId, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<ModLibraryEntry>> GetModLibraryAsync(GameInstallation game, CancellationToken cancellationToken) =>
        ReadAsync(game, store => store.GetModLibraryAsync(game, cancellationToken), cancellationToken);

    public Task<AppResult<int>> RemoveWorkshopModAsync(GameInstallation game, string workshopId, CancellationToken cancellationToken) =>
        For(game).RemoveWorkshopModAsync(game, workshopId, cancellationToken);

    public Task<IReadOnlyList<PlaySetDlc>> GetPlaySetDlcsAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
        ReadAsync(game, store => store.GetPlaySetDlcsAsync(game, playSetId, cancellationToken), cancellationToken);

    public Task<AppResult<bool>> UpdateModPathsAsync(GameInstallation game, Guid modId, string? dirPath, string? archivePath, CancellationToken cancellationToken) =>
        For(game).UpdateModPathsAsync(game, modId, dirPath, archivePath, cancellationToken);

    public Task<AppResult<bool>> UpdateModRegistryIdAsync(GameInstallation game, Guid modId, string? gameRegistryId, CancellationToken cancellationToken) =>
        For(game).UpdateModRegistryIdAsync(game, modId, gameRegistryId, cancellationToken);

    public Task<AppResult<bool>> AdoptLocalModAsync(GameInstallation game, Guid oldModId, string? oldDirPath, string? oldArchivePath, PlaySetMod localMod, CancellationToken cancellationToken) =>
        For(game).AdoptLocalModAsync(game, oldModId, oldDirPath, oldArchivePath, localMod, cancellationToken);

    public Task<AppResult<bool>> RevertLocalModAdoptionAsync(GameInstallation game, Guid newModId, Guid oldModId, string? oldDirPath, string? oldArchivePath, CancellationToken cancellationToken, string? newDirPath = null, string? newArchivePath = null) =>
        For(game).RevertLocalModAdoptionAsync(game, newModId, oldModId, oldDirPath, oldArchivePath, cancellationToken, newDirPath, newArchivePath);

    public Task<AppResult<bool>> SavePlaySetModsAsync(GameInstallation game, Guid playSetId, IReadOnlyList<PlaySetMod> mods, CancellationToken cancellationToken) =>
        For(game).SavePlaySetModsAsync(game, playSetId, mods, cancellationToken);

    public Task<AppResult<bool>> SavePlaySetDlcsAsync(GameInstallation game, Guid playSetId, IReadOnlyList<PlaySetDlc> dlcs, CancellationToken cancellationToken) =>
        For(game).SavePlaySetDlcsAsync(game, playSetId, dlcs, cancellationToken);

    public Task<AppResult<Guid>> CreatePlaySetAsync(GameInstallation game, string name, CancellationToken cancellationToken) =>
        For(game).CreatePlaySetAsync(game, name, cancellationToken);

    public Task<AppResult<bool>> SetActivePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
        For(game).SetActivePlaySetAsync(game, playSetId, cancellationToken);

    public Task<AppResult<bool>> RenamePlaySetAsync(GameInstallation game, Guid playSetId, string newName, CancellationToken cancellationToken) =>
        For(game).RenamePlaySetAsync(game, playSetId, newName, cancellationToken);

    public Task<AppResult<bool>> DeletePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
        For(game).DeletePlaySetAsync(game, playSetId, cancellationToken);
}
