using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets.Json;

namespace PdxLauncher.App.Services;

/// <summary>
/// 按游戏的存档格式把 <see cref="IPlaySetRepository"/> 路由到具体实现：
/// <see cref="LoadFileFormat.PlaysetsJsonV1"/>（EU5）走 playsets.json，
/// <see cref="LoadFileFormat.PlaysetConfigV1"/>（CS2）走 playset_config.json，
/// 其余仍走 launcher-v2.sqlite。
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

    /// <summary>
    /// 创建路由。
    /// </summary>
    /// <param name="sqlite">launcher-v2.sqlite 实现（EU4/CK3/Vic3 等）。</param>
    /// <param name="playsetsJson">playsets.json 实现（EU5）。</param>
    /// <param name="playsetConfig">playset_config.json 实现（CS2）。</param>
    public PlaySetRepositoryRouter(
        IPlaySetRepository sqlite,
        IPlaySetRepository playsetsJson,
        IPlaySetRepository playsetConfig)
    {
        ArgumentNullException.ThrowIfNull(sqlite);
        ArgumentNullException.ThrowIfNull(playsetsJson);
        ArgumentNullException.ThrowIfNull(playsetConfig);

        _sqlite = sqlite;
        _playsetsJson = playsetsJson;
        _playsetConfig = playsetConfig;
    }

    /// <summary>按格式挑实现。</summary>
    public IPlaySetRepository For(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Format switch
        {
            LoadFileFormat.PlaysetsJsonV1 => _playsetsJson,
            LoadFileFormat.PlaysetConfigV1 => _playsetConfig,
            _ => _sqlite,
        };
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySet>> GetPlaySetsAsync(GameInstallation game, CancellationToken cancellationToken) =>
        For(game).GetPlaySetsAsync(game, cancellationToken);

    /// <inheritdoc />
    public Task<PlaySet?> GetActivePlaySetAsync(GameInstallation game, CancellationToken cancellationToken) =>
        For(game).GetActivePlaySetAsync(game, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySetMod>> GetPlaySetModsAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken) =>
        For(game).GetPlaySetModsAsync(game, playSetId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ModLibraryEntry>> GetModLibraryAsync(
        GameInstallation game,
        CancellationToken cancellationToken) =>
        For(game).GetModLibraryAsync(game, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySetDlc>> GetPlaySetDlcsAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken) =>
        For(game).GetPlaySetDlcsAsync(game, playSetId, cancellationToken);

    /// <inheritdoc />
    public Task<AppResult<bool>> SavePlaySetModsAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetMod> mods,
        CancellationToken cancellationToken) =>
        For(game).SavePlaySetModsAsync(game, playSetId, mods, cancellationToken);

    /// <inheritdoc />
    public Task<AppResult<bool>> SavePlaySetDlcsAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetDlc> dlcs,
        CancellationToken cancellationToken) =>
        For(game).SavePlaySetDlcsAsync(game, playSetId, dlcs, cancellationToken);

    /// <inheritdoc />
    public Task<AppResult<Guid>> CreatePlaySetAsync(GameInstallation game, string name, CancellationToken cancellationToken) =>
        For(game).CreatePlaySetAsync(game, name, cancellationToken);

    /// <inheritdoc />
    public Task<AppResult<bool>> SetActivePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
        For(game).SetActivePlaySetAsync(game, playSetId, cancellationToken);

    /// <inheritdoc />
    public Task<AppResult<bool>> RenamePlaySetAsync(
        GameInstallation game,
        Guid playSetId,
        string newName,
        CancellationToken cancellationToken) =>
        For(game).RenamePlaySetAsync(game, playSetId, newName, cancellationToken);

    /// <inheritdoc />
    public Task<AppResult<bool>> DeletePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
        For(game).DeletePlaySetAsync(game, playSetId, cancellationToken);
}
