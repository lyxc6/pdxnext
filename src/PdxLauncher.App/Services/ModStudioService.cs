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
            _logger.Warn("发布被拦下：游戏没有 Steam 创意工坊（game=" + game.GameId + "）");
            return ModPublishResult.Fail("这款游戏没有可用的 Steam 创意工坊");
        }

        if (string.IsNullOrWhiteSpace(entry.Mod.DirPath) || !_fileSystem.DirectoryExists(entry.Mod.DirPath!))
        {
            _logger.Warn("发布被拦下：本地目录不存在（" + entry.Mod.Name + "，" + (entry.Mod.DirPath ?? "无目录") + "）");
            return ModPublishResult.Fail("这个 mod 没有可上传的本地目录");
        }

        // 附加预览图在进宿主进程之前就拦掉：Steam 对单张图有 1 MB 硬限制，
        // 让它跑到上传中途才失败会白等一次完整的 mod 上传。
        foreach (var path in previewPaths)
        {
            if (!_fileSystem.FileExists(path))
            {
                _logger.Warn("发布被拦下：找不到预览图 " + path);
                return ModPublishResult.Fail("找不到预览图：" + path);
            }

            if (!ModPublishLimits.IsPreviewFileAcceptable(_fileSystem.GetFileSize(path), out var sizeError))
            {
                _logger.Warn("发布被拦下：预览图不合规（" + path + "）：" + sizeError);
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
            _logger.Warn("发布被拦下：发布环境不完整（appid=" + (appId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?") + "）：" + (reason ?? "未知原因"));
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

    /// <summary>
    /// 把一个**已经在 Steam 上发布过**的工坊条目绑定到本地 mod。
    /// <para>
    /// 存在的理由：条目处于隐藏 / 仅好友 / 私密 / 未列出时，匿名 Web API 只回 <c>EResult 9</c>，
    /// 启动器既列不出来也查不到，作者手上明明有 id 却只能看到「未发布」。
    /// 绑定不要求读到详情：Steam 没开、没登录、缺 dll 时依然允许绑定，只把可见性留成「未核实」。
    /// </para>
    /// </summary>
    /// <param name="game">当前游戏。</param>
    /// <param name="entry">要绑定的本地 mod。</param>
    /// <param name="publishedFileId">工坊条目 id。</param>
    /// <param name="verifyVisibility">是否尝试用 Steam 读回真实标题与可见性。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>绑定结果；失败原因在 <see cref="ModBindResult.Error"/>。</returns>
    public async Task<ModBindResult> BindWorkshopItemAsync(
        GameInstallation game,
        ModStudioEntry entry,
        ulong publishedFileId,
        bool verifyVisibility,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(entry);

        if (publishedFileId == 0)
        {
            _logger.Warn("绑定工坊条目被拒：id 为 0（" + entry.Mod.Name + "）");
            return ModBindResult.Fail("创意工坊条目 id 必须是正整数");
        }

        if (string.IsNullOrWhiteSpace(entry.Mod.DirPath) || !_fileSystem.DirectoryExists(entry.Mod.DirPath!))
        {
            _logger.Warn("绑定工坊条目被拒：本地目录不存在（" + entry.Mod.Name + "，" + (entry.Mod.DirPath ?? "无目录") + "）");
            return ModBindResult.Fail("这个 mod 没有本地目录，不能绑定工坊条目");
        }

        string? steamTitle = null;
        int? steamVisibility = null;
        string? verifyError = null;

        if (verifyVisibility)
        {
            var (detail, error) = await TryReadOwnItemAsync(game, publishedFileId, cancellationToken).ConfigureAwait(false);
            if (detail is null)
            {
                verifyError = error ?? "没能从 Steam 读回条目详情";
                _logger.Warn("绑定工坊条目时未核实可见性（item=" + publishedFileId + "）：" + verifyError);
            }
            else
            {
                steamTitle = string.IsNullOrWhiteSpace(detail.Title) ? null : detail.Title;
                steamVisibility = detail.Visibility;
            }
        }

        var previous = entry.Publication;
        var title = steamTitle
            ?? (string.IsNullOrWhiteSpace(previous?.Title) ? null : previous!.Title)
            ?? (string.IsNullOrWhiteSpace(entry.Descriptor?.Name) ? null : entry.Descriptor!.Name)
            ?? entry.Mod.Name;

        var publication = new ModPublication(
            game.GameId,
            entry.Mod.DirPath!,
            publishedFileId,
            title,
            previous?.Description ?? string.Empty,
            previous?.Tags ?? Array.Empty<string>(),
            previous?.Public ?? true,
            previous?.PreviewPath,
            null,
            DateTimeOffset.UtcNow,
            previous?.Visibility,
            previous?.ContentDescriptors,
            previous?.PreviewPaths,
            previous?.PreviewVideoIds,
            previous?.Localizations,
            steamVisibility);

        if (!_publications.Save(publication))
        {
            _logger.Warn("绑定工坊条目时写入记录失败（item=" + publishedFileId + "，" + entry.Mod.DirPath + "）");
            return ModBindResult.Fail("没能写入发布记录");
        }

        _logger.Info("已绑定创意工坊条目：" + title + "（" + publishedFileId + "）" +
            (steamVisibility is { } v ? "，Steam 可见性=" + v.ToString(System.Globalization.CultureInfo.InvariantCulture) : "，可见性未核实"));

        return ModBindResult.Bound(publication, steamVisibility is not null, verifyError);
    }

    /// <summary>列当前账号在本游戏下自己发布的工坊条目；失败时返回原因（不抛）。</summary>
    /// <param name="game">当前游戏。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>条目 id 列表与失败原因。</returns>
    public async Task<(SteamHostMineResult? Result, string? Error)> ReadOwnItemsAsync(
        GameInstallation game,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var (apiPath, appId, error) = ResolveMineContext(game);
        if (apiPath is null || appId is null)
        {
            _logger.Warn("列出我的工坊条目失败：" + (error ?? "发布环境不完整"));
            return (null, error ?? "发布环境不完整");
        }

        try
        {
            var result = await _steamHost.ReadMineAsync(apiPath, appId.Value, cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                _logger.Warn("列出我的工坊条目失败：" + result.Error);
            }

            return (result, result.Ok ? null : result.Error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Warn("列出我的工坊条目异常：" + exception.Message);
            return (null, exception.Message);
        }
    }

    private async Task<(SteamHostMineItemResult? Detail, string? Error)> TryReadOwnItemAsync(
        GameInstallation game,
        ulong publishedFileId,
        CancellationToken cancellationToken)
    {
        var (apiPath, appId, error) = ResolveMineContext(game);
        if (apiPath is null || appId is null)
        {
            return (null, error ?? "发布环境不完整");
        }

        try
        {
            var detail = await _steamHost.ReadMineItemAsync(apiPath, appId.Value, publishedFileId, cancellationToken).ConfigureAwait(false);
            return detail.Ok ? (detail, null) : (null, detail.Error ?? "读取工坊条目失败");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return (null, exception.Message);
        }
    }

    /// <summary>找齐「读自己条目」需要的 dll 与 appid；缺失时给出中文原因。</summary>
    private (string? ApiPath, int? AppId, string? Error) ResolveMineContext(GameInstallation game)
    {
        var appId = WorkshopSourceChooser.ResolveAppId(game.SteamAppId, game.GameId);
        if (appId is not { } id || id <= 0)
        {
            return (null, null, "不知道这款游戏的 Steam appid");
        }

        if (!_steamHost.IsHostAvailable)
        {
            return (null, null, "找不到可用的 Steam 宿主程序：" + _steamHost.HostPath);
        }

        var apiPath = _locator.Resolve(_settingsPath, AppContext.BaseDirectory, game)
            ?? SteamClientLocator.FindSteamApiDll();
        return string.IsNullOrWhiteSpace(apiPath)
            ? (null, null, "找不到可用的 steam_api64.dll（游戏目录、设置里手填的路径与 Steam 安装目录都没有）")
            : (apiPath, id, null);
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
