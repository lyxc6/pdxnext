using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.LoadFiles;

namespace PdxLauncher.Core.Playsets;

/// <summary>一条路径修复要做的事。</summary>
public enum ModPathRepairKind
{
    /// <summary>库里这一行完全没有路径，但磁盘上找得到对应目录，把路径补回去。</summary>
    FillFromDisk,

    /// <summary>目录名含非 ASCII，而游戏不吃这类路径，把目录改成 ASCII 名（路径跟着改）。</summary>
    RenameDirectory,
}

/// <summary>一条待确认的路径修复建议。</summary>
/// <param name="ModId">官方库 <c>mods.id</c>。</param>
/// <param name="Name">展示用名字。</param>
/// <param name="Kind">修复类型。</param>
/// <param name="CurrentPath">当前路径（可能为空）。</param>
/// <param name="SuggestedPath">建议改成什么；<c>null</c> 表示只提示、无法自动做。</param>
/// <param name="ReasonKey">提示文案键；<c>null</c> 表示没触犯任何规则。</param>
public sealed record ModPathRepair(
    Guid ModId,
    string Name,
    ModPathRepairKind Kind,
    string? CurrentPath,
    string? SuggestedPath,
    string? ReasonKey);

/// <summary>
/// 找出并修正「加载文件里写不出／游戏读不到」的 mod 路径。
/// </summary>
/// <remarks>
/// 两类真机问题各对应一种修复：
/// <list type="bullet">
/// <item>
/// 官方库里有行、但 <c>dirPath</c> / <c>archivePath</c> 都是 NULL（订阅时没落盘、或官方启动器自己丢了路径）：
/// 加载文件里直接少一行 ⇒ 游戏里等于没启用。修复 = 按工坊 id / 名字在磁盘上找到目录并补进库。
/// </item>
/// <item>
/// Victoria 3 不加载非 ASCII 路径的 mod（真机：<c>...\mod\更改虚空造物</c> 写进了
/// <c>content_load.json</c>，游戏就是不认）：修复 = 把目录改成 <c>mod_&lt;id 前 8 位&gt;</c> 这类 ASCII 名，
/// 再把库里的 <c>dirPath</c> 跟着改。
/// </item>
/// </list>
/// 上层目录非 ASCII（例如 Windows 用户名是中文）没法替用户改名，只出提示、不给建议值。
/// </remarks>
public sealed class ModPathRepairer
{
    private const int ShortIdLength = 8;
    private const int MaxRenameAttempts = 99;

    private readonly IFileSystem _fileSystem;
    private readonly IPlaySetRepository _playSets;
    private readonly IModCatalog _catalog;
    private readonly IAppLogger _logger;

    public ModPathRepairer(
        IFileSystem fileSystem,
        IPlaySetRepository playSets,
        IModCatalog catalog,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(playSets);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _playSets = playSets;
        _catalog = catalog;
        _logger = logger;
    }

    /// <summary>找出当前游戏下所有值得修的路径问题。</summary>
    public async Task<IReadOnlyList<ModPathRepair>> FindAsync(
        GameInstallation game,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        // EU5 playsets.json / CS2 playset_config.json 的 mod 不是「有路径的行」，没有这类问题。
        if (game.Format is LoadFileFormat.PlaysetsJsonV1 or LoadFileFormat.PlaysetConfigV1)
        {
            return Array.Empty<ModPathRepair>();
        }

        var library = await _playSets.GetModLibraryAsync(game, cancellationToken).ConfigureAwait(false);
        if (library.Count == 0)
        {
            return Array.Empty<ModPathRepair>();
        }

        IReadOnlyList<PlaySetMod> scanned;
        try
        {
            scanned = await _catalog.DiscoverAllAsync(game, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 扫描失败不该让「修复」入口报错，顶多少几条建议。
            _logger.Warn("修复 mod 路径：磁盘扫描失败，只能按库里的信息判断：" + ex.Message);
            scanned = Array.Empty<PlaySetMod>();
        }

        var repairs = new List<ModPathRepair>();
        var requiresAscii = GamePathRules.RequiresAsciiPaths(game.GameId);

        foreach (var entry in library)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hasDir = !string.IsNullOrWhiteSpace(entry.DirPath);
            var hasArchive = !string.IsNullOrWhiteSpace(entry.ArchivePath);

            if (!hasDir && !hasArchive)
            {
                var workshopDir = FindScannedDir(scanned, entry);
                repairs.Add(new ModPathRepair(
                    entry.ModId,
                    entry.Name,
                    ModPathRepairKind.FillFromDisk,
                    null,
                    workshopDir,
                    workshopDir is null ? ModLoadRules.WarnNoPath : null));
                continue;
            }

            if (!requiresAscii || entry.Origin != ModOrigin.Local || !hasDir)
            {
                continue;
            }

            var dirPath = entry.DirPath!;
            if (!ModLoadRules.ContainsNonAscii(ModLoadRules.LeafOf(dirPath)))
            {
                // 目录名本身没问题：要么完全没事，要么是上层目录（用户名）带非 ASCII，只能提示。
                if (ModLoadRules.ContainsNonAscii(ModLoadRules.ParentOf(dirPath)))
                {
                    repairs.Add(new ModPathRepair(
                        entry.ModId,
                        entry.Name,
                        ModPathRepairKind.RenameDirectory,
                        dirPath,
                        null,
                        ModLoadRules.WarnNonAsciiParent));
                }

                continue;
            }

            if (ModLoadRules.ContainsNonAscii(ModLoadRules.ParentOf(dirPath)))
            {
                // 父目录也脏：改名救不了，仍然只提示。
                repairs.Add(new ModPathRepair(
                    entry.ModId,
                    entry.Name,
                    ModPathRepairKind.RenameDirectory,
                    dirPath,
                    null,
                    ModLoadRules.WarnNonAsciiParent));
                continue;
            }

            if (!_fileSystem.DirectoryExists(dirPath))
            {
                repairs.Add(new ModPathRepair(
                    entry.ModId,
                    entry.Name,
                    ModPathRepairKind.RenameDirectory,
                    dirPath,
                    null,
                    ModLoadRules.WarnMissingPath));
                continue;
            }

            repairs.Add(new ModPathRepair(
                entry.ModId,
                entry.Name,
                ModPathRepairKind.RenameDirectory,
                dirPath,
                ChooseAsciiName(dirPath, entry.ModId),
                ModLoadRules.WarnNonAsciiPath));
        }

        return repairs;
    }

    /// <summary>
    /// 应用修复：先改名，再写库；写库失败尽力把目录移回去。
    /// </summary>
    /// <returns>实际修复的条数。</returns>
    public async Task<AppResult<int>> ApplyAsync(
        GameInstallation game,
        IReadOnlyList<ModPathRepair> repairs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(repairs);

        var actionable = repairs
            .Where(static repair => !string.IsNullOrWhiteSpace(repair.SuggestedPath))
            .ToList();
        if (actionable.Count == 0)
        {
            return AppResult<int>.Ok(0);
        }

        // 第一步：目录改名。先全做完再写库，避免「库指向新名、磁盘还是旧名」的半成品状态。
        var moved = new List<(string From, string To)>();
        foreach (var repair in actionable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (repair.Kind != ModPathRepairKind.RenameDirectory)
            {
                continue;
            }

            var from = repair.CurrentPath;
            var to = repair.SuggestedPath!;
            if (string.IsNullOrWhiteSpace(from) || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!_fileSystem.DirectoryExists(from))
            {
                continue;
            }

            try
            {
                _fileSystem.MoveDirectory(from, to);
                moved.Add((from, to));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Rollback(moved);
                return AppResult<int>.Fail(ErrorKind.Io, "rename:" + from + " -> " + to + ":" + ex.Message);
            }
        }

        // 第二步：把新路径写进库（官方库在就写官方库，不在就写自有库，由仓储路由器决定）。
        var fixedCount = 0;
        foreach (var repair in actionable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _playSets
                .UpdateModPathsAsync(game, repair.ModId, repair.SuggestedPath, null, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                Rollback(moved);
                _logger.Error("修复 mod 路径失败（modId=" + repair.ModId.ToString("N") + "）：" + result.ErrorMessage);
                return AppResult<int>.Fail(result.Kind, "update:" + repair.ModId.ToString("N") + ":" + result.ErrorMessage);
            }

            fixedCount++;
        }

        if (_catalog is IScanCacheInvalidator invalidator)
        {
            // 目录改名后扫描缓存里的路径全过期了，不清掉会在页面上看到旧路径。
            invalidator.InvalidateCache(game);
        }

        _logger.Info("修复 " + fixedCount + " 个 mod 路径（gameId=" + game.GameId + "）");
        return AppResult<int>.Ok(fixedCount);
    }

    private void Rollback(List<(string From, string To)> moved)
    {
        for (var i = moved.Count - 1; i >= 0; i--)
        {
            var (from, to) = moved[i];
            try
            {
                if (_fileSystem.DirectoryExists(to) && !_fileSystem.DirectoryExists(from))
                {
                    _fileSystem.MoveDirectory(to, from);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 回滚失败只能记下来人工处理：库没写成，目录却停在新名字上。
                _logger.Error("回滚 mod 目录改名失败：" + to + " -> " + from, ex);
            }
        }
    }

    private string? FindScannedDir(IReadOnlyList<PlaySetMod> scanned, ModLibraryEntry entry)
    {
        var workshopId = SteamWorkshop.GetWorkshopId(entry.SteamId, entry.GameRegistryId);
        if (!string.IsNullOrWhiteSpace(workshopId))
        {
            foreach (var candidate in scanned)
            {
                if (candidate.DirPath is null)
                {
                    continue;
                }

                var candidateId = SteamWorkshop.GetWorkshopId(candidate.SteamId, candidate.GameRegistryId);
                if (string.Equals(candidateId, workshopId, StringComparison.Ordinal))
                {
                    return _fileSystem.DirectoryExists(candidate.DirPath) ? candidate.DirPath : null;
                }
            }
        }

        // 没有工坊 id（或扫描里没对上）：退回按名字找本地目录。
        foreach (var candidate in scanned)
        {
            if (candidate.DirPath is null)
            {
                continue;
            }

            if (string.Equals(candidate.Name, entry.Name, StringComparison.OrdinalIgnoreCase)
                && _fileSystem.DirectoryExists(candidate.DirPath))
            {
                return candidate.DirPath;
            }
        }

        return null;
    }

    /// <summary>在同一个父目录下挑一个不撞名的 ASCII 目录名。</summary>
    private string ChooseAsciiName(string dirPath, Guid modId)
    {
        var parent = ModLoadRules.ParentOf(dirPath);
        var baseName = "mod_" + modId.ToString("N")[..ShortIdLength];
        var candidate = Combine(parent, baseName);
        if (!_fileSystem.DirectoryExists(candidate))
        {
            return candidate;
        }

        for (var i = 2; i <= MaxRenameAttempts; i++)
        {
            candidate = Combine(parent, baseName + "_" + i);
            if (!_fileSystem.DirectoryExists(candidate))
            {
                return candidate;
            }
        }

        // 极端情况：同名目录太多，退回带完整 id 的名字（几乎不可能撞）。
        return Combine(parent, "mod_" + modId.ToString("N"));
    }

    private string Combine(string parent, string name) =>
        string.IsNullOrEmpty(parent) ? name : _fileSystem.Combine(parent, name);
}
