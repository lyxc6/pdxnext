namespace PdxLauncher.App.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.ModStudio;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Steam;

/// <summary>「Mod 创建」页的数据服务：合并本地清单、创建描述符、发布到工坊。</summary>
public sealed class ModStudioService
{
    private readonly IPlaySetRepository _playSets;
    private readonly IModCatalog _mods;
    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly ModPublicationStore _publications;
    private readonly SteamHostClient _steamHost;
    private readonly SteamApiLocator _locator;
    private readonly string? _settingsPath;

    /// <summary>构造。</summary>
    public ModStudioService(
        IPlaySetRepository playSets,
        IModCatalog mods,
        IFileSystem fileSystem,
        IAppLogger logger,
        ModPublicationStore publications,
        SteamHostClient steamHost,
        SteamApiLocator locator,
        string? settingsPath)
    {
        _playSets = playSets ?? throw new ArgumentNullException(nameof(playSets));
        _mods = mods ?? throw new ArgumentNullException(nameof(mods));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _publications = publications ?? throw new ArgumentNullException(nameof(publications));
        _steamHost = steamHost ?? throw new ArgumentNullException(nameof(steamHost));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _settingsPath = settingsPath;
    }

    /// <summary>读当前游戏的本地 mod、描述符和发布记录。</summary>
    public async Task<IReadOnlyList<ModStudioEntry>> LoadAsync(
        GameInstallation? game,
        CancellationToken cancellationToken)
    {
        if (game is null)
        {
            return Array.Empty<ModStudioEntry>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ModLibraryEntry> known;
        try
        {
            known = await _playSets.GetModLibraryAsync(game, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Warn("Mod 创建页读取 Mod 库失败：" + exception.Message);
            known = Array.Empty<ModLibraryEntry>();
        }

        IReadOnlyList<PlaySetMod> catalog;
        try
        {
            catalog = await _mods.DiscoverAllAsync(game, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Warn("Mod 创建页扫描磁盘失败：" + exception.Message);
            catalog = Array.Empty<PlaySetMod>();
        }

        var local = LocalModInventory.Merge(game, known, catalog, _fileSystem);
        var publications = new Dictionary<string, ModPublication>(StringComparer.OrdinalIgnoreCase);
        foreach (var publication in _publications.List(game.GameId))
        {
            publications[Normalize(publication.ModPath)] = publication;
        }

        var result = new List<ModStudioEntry>(local.Count);
        foreach (var mod in local)
        {
            ModDescriptorInfo? descriptor = null;
            ModPublication? publication = null;
            if (!string.IsNullOrWhiteSpace(mod.DirPath))
            {
                ModDescriptorReader.TryRead(game, mod.DirPath, mod.Name, _fileSystem, out descriptor);
                publications.TryGetValue(Normalize(mod.DirPath!), out publication);
            }

            result.Add(new ModStudioEntry(mod, descriptor, publication));
        }

        return result;
    }

    /// <summary>在磁盘上创建一个本地 mod；成功时让 Mod 扫描缓存失效。</summary>
    public ModCreateResult Create(GameInstallation game, ModCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(request);

        var result = ModCreator.Create(game, request, _fileSystem);
        if (result.Ok)
        {
            if (_mods is IScanCacheInvalidator invalidatable)
            {
                invalidatable.InvalidateCache(game);
            }

            _logger.Info("已创建本地 mod：" + (result.DirPath ?? request.FolderName));
        }
        else
        {
            _logger.Warn("创建本地 mod 失败：" + result.ErrorCode + "（" + request.FolderName + "）");
        }

        return result;
    }

    /// <summary>把本地 mod 发布到 Steam 创意工坊，并保存工坊 id 映射。</summary>
    public async Task<ModPublishResult> PublishAsync(
        GameInstallation game,
        ModStudioEntry entry,
        ulong? publishedFileId,
        string title,
        string description,
        IReadOnlyList<string> tags,
        SteamWorkshopVisibility visibility,
        string? changeNote,
        string? previewPath,
        IReadOnlyList<int> contentDescriptors,
        IReadOnlyList<string> previewPaths,
        IReadOnlyList<string> previewVideoIds,
        IReadOnlyList<ModLocalizedText> localizations,
        Action<ModPublishProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(entry);

        if (!ModDescriptorRules.CanPublish(game.GameId))
        {
            return ModPublishResult.Fail("这款游戏没有可用的 Steam 创意工坊");
        }

        if (string.IsNullOrWhiteSpace(entry.Mod.DirPath) || !_fileSystem.DirectoryExists(entry.Mod.DirPath!))
        {
            return ModPublishResult.Fail("这个 mod 没有可上传的本地目录");
        }

        // 附加预览图在进宿主进程之前就拦掉：Steam 对单张图有 1 MB 硬限制，
        // 让它跑到上传中途才失败会白等一次完整的 mod 上传。
        foreach (var path in previewPaths)
        {
            if (!_fileSystem.FileExists(path))
            {
                return ModPublishResult.Fail("找不到预览图：" + path);
            }

            if (!ModPublishLimits.IsPreviewFileAcceptable(_fileSystem.GetFileSize(path), out var sizeError))
            {
                return ModPublishResult.Fail(sizeError + "：" + Path.GetFileName(path));
            }
        }

        var appId = WorkshopSourceChooser.ResolveAppId(game.SteamAppId, game.GameId);
        var backend = ModPublishBackend.TryCreate(
            _steamHost,
            _logger,
            _fileSystem,
            _locator,
            _settingsPath,
            game,
            appId,
            out var reason);
        if (backend is null)
        {
            return ModPublishResult.Fail(reason ?? "发布环境不完整");
        }

        var request = new ModPublishRequest(
            appId!.Value,
            publishedFileId,
            entry.Mod.DirPath!,
            previewPath,
            title,
            description,
            changeNote,
            tags,
            visibility,
            contentDescriptors,
            previewPaths,
            previewVideoIds,
            localizations);

        var result = await backend.PublishAsync(request, progress, cancellationToken).ConfigureAwait(false);

        var previous = entry.Publication;
        var publishedId = result.PublishedFileId ?? publishedFileId ?? previous?.PublishedFileId;
        var publication = new ModPublication(
            game.GameId,
            entry.Mod.DirPath!,
            publishedId,
            title,
            description,
            tags,
            visibility == SteamWorkshopVisibility.Public,
            previewPath,
            result.Ok ? null : result.Error,
            DateTimeOffset.UtcNow,
            visibility,
            contentDescriptors,
            previewPaths,
            previewVideoIds,
            localizations);

        if (publishedId is not null || previous is not null)
        {
            _publications.Save(publication);
        }

        if (result.Ok)
        {
            _logger.Info("已发布到 Steam 创意工坊：" + title + "（" + publishedId + "）");
        }
        else
        {
            _logger.Warn("发布到 Steam 创意工坊失败：" + result.Error);
        }

        return result;
    }

    private string Normalize(string path)
    {
        try
        {
            return _fileSystem.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            return path;
        }
    }
}
