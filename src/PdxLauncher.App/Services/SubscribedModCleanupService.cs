namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Steam;

/// <summary>
/// Steam 工坊退订后的本机清理：只删
/// <c>&lt;library&gt;\steamapps\workshop\content\&lt;appid&gt;\&lt;workshopId&gt;</c>
/// 这一处内容，以及播放集仓储里同一个工坊 id 的 mod 行。
/// </summary>
public sealed class SubscribedModCleanupService : ISubscribedModCleanup
{
    private readonly IFileSystem _fileSystem;
    private readonly IPlaySetRepository _playSets;
    private readonly SteamLibraryLocator _libraries;
    private readonly IScanCacheInvalidator? _scanInvalidator;
    private readonly WorkshopStateCache _states;
    private readonly IAppLogger _logger;

    public SubscribedModCleanupService(
        IFileSystem fileSystem,
        IPlaySetRepository playSets,
        SteamLibraryLocator libraries,
        IScanCacheInvalidator? scanInvalidator,
        WorkshopStateCache states,
        IAppLogger logger)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _playSets = playSets ?? throw new ArgumentNullException(nameof(playSets));
        _libraries = libraries ?? throw new ArgumentNullException(nameof(libraries));
        _scanInvalidator = scanInvalidator;
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SubscribedModCleanupResult> CleanupAsync(
        int appId,
        string workshopId,
        CancellationToken cancellationToken)
    {
        var id = workshopId?.Trim() ?? string.Empty;
        if (appId <= 0 || !IsWorkshopId(id))
        {
            return SubscribedModCleanupResult.Warning("workshop-id-invalid");
        }

        var game = AppServices.CurrentGame;
        if (game is null)
        {
            return SubscribedModCleanupResult.Warning("no-current-game");
        }

        var contentDeleted = false;
        string? contentError = null;
        try
        {
            contentDeleted = await Task.Run(
                () => DeleteWorkshopContent(appId, id),
                cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            contentError = ex.Message;
            _logger.Warn("删除已退订工坊内容失败（id=" + id + "）：" + ex.Message);
        }

        var repository = await _playSets
            .RemoveWorkshopModAsync(game, id, cancellationToken)
            .ConfigureAwait(true);

        _states.Invalidate(appId);
        _scanInvalidator?.InvalidateCache(game);

        if (contentError is not null)
        {
            return SubscribedModCleanupResult.Warning(
                "workshop-content-delete-failed:" + contentError,
                repository.IsSuccess ? repository.Value : 0);
        }

        if (!repository.IsSuccess)
        {
            return SubscribedModCleanupResult.Warning(
                "mod-library-row-delete-failed:" + (repository.ErrorMessage ?? "unknown"),
                contentDeleted ? 0 : -1);
        }

        return new SubscribedModCleanupResult(contentDeleted, repository.Value, null);
    }

    /// <summary>
    /// 在所有 Steam 库里删除精确匹配的内容目录；返回 true 表示目标不存在或已删除。
    /// </summary>
    private bool DeleteWorkshopContent(int appId, string workshopId)
    {
        var found = false;
        var deleted = true;
        foreach (var library in _libraries.FindLibraries())
        {
            var content = _fileSystem.Combine(
                library,
                "steamapps",
                "workshop",
                "content",
                appId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                workshopId);

            if (!_fileSystem.DirectoryExists(content))
            {
                continue;
            }

            found = true;
            _fileSystem.Delete(content);
            if (_fileSystem.DirectoryExists(content))
            {
                deleted = false;
            }
        }

        if (found)
        {
            _logger.Info("已删除退订工坊内容：appid=" + appId + "，id=" + workshopId);
        }

        return found ? deleted : true;
    }

    private static bool IsWorkshopId(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
