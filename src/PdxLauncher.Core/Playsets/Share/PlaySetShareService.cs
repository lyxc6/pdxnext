namespace PdxLauncher.Core.Playsets.Share;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>一次导出的结果。</summary>
/// <param name="FilePath">实际写入的文件路径。</param>
/// <param name="ExportedMods">写进分享文件的 mod 数。</param>
/// <param name="LocalExcluded">按官方规则被排除的本地 mod 数。</param>
/// <param name="UnsupportedNames">没有平台 id、无法表达的 mod 名。</param>
public sealed record PlaySetShareExportResult(
    string FilePath,
    int ExportedMods,
    int LocalExcluded,
    IReadOnlyList<string> UnsupportedNames);

/// <summary>一次导入的结果。</summary>
/// <param name="PlaySetId">导入到的播放集 id。</param>
/// <param name="PlaySetName">导入到的播放集名（重名时可能加了序号）。</param>
/// <param name="Merged">true = 合并进已有播放集；false = 新建播放集。</param>
/// <param name="ImportedMods">播放集里来自这份分享文件的 mod 数。</param>
/// <param name="AddedMods">本次真正新加进播放集的 mod 数（合并时排除已在集内的）。</param>
/// <param name="Missing">本地没安装的条目（可一键订阅 Steam mod / 等下载完再导入）。</param>
public sealed record PlaySetShareImportResult(
    Guid PlaySetId,
    string PlaySetName,
    bool Merged,
    int ImportedMods,
    int AddedMods,
    IReadOnlyList<PlaySetShareMissingMod> Missing);

/// <summary>导入前的预览：文件属于哪款游戏、叫什么名字、本地是否已有同名播放集。</summary>
/// <param name="Game">文件里的游戏 id。</param>
/// <param name="Name">文件里的播放集名。</param>
/// <param name="Mods">mod 条目数。</param>
/// <param name="ExistingPlaySetId">本地同名播放集的 id；没有同名时为 null。</param>
/// <param name="ExistingPlaySetName">本地同名播放集的实际名字。</param>
public sealed record PlaySetSharePeek(
    string Game,
    string Name,
    int Mods,
    Guid? ExistingPlaySetId,
    string? ExistingPlaySetName);

/// <summary>
/// 官方「分享播放集 / 导入播放集」的 Core 实现：读写与官方逐字段一致的 JSON，
/// 导入时按本地 Mod 库解析，同名文件再次导入可合并补齐。
/// <para>只依赖 <see cref="IPlaySetRepository"/> 与 <see cref="IFileSystem"/>，不碰任何 UI 类型。</para>
/// </summary>
public sealed class PlaySetShareService
{
    /// <summary>分享文件扩展名（带点），与官方保存对话框一致。</summary>
    public const string FileExtension = PlaySetShareDocument.FileExtension;

    private readonly IPlaySetRepository _playSets;
    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;

    public PlaySetShareService(IPlaySetRepository playSets, IFileSystem fileSystem, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(playSets);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        _playSets = playSets;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// 把播放集导出成官方分享文件。目标目录不存在时自动创建；写入走原子替换。
    /// </summary>
    public async Task<AppResult<PlaySetShareExportResult>> ExportAsync(
        GameInstallation game,
        Guid playSetId,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return AppResult<PlaySetShareExportResult>.Fail(ErrorKind.Invalid, "share-path-empty");
        }

        var playSets = await _playSets.GetPlaySetsAsync(game, cancellationToken).ConfigureAwait(false);
        var playSet = playSets.FirstOrDefault(candidate => candidate.Id == playSetId);
        if (playSet is null)
        {
            return AppResult<PlaySetShareExportResult>.Fail(
                ErrorKind.NotFound,
                "playSet-not-found:" + playSetId.ToString("D"));
        }

        var mods = await _playSets.GetPlaySetModsAsync(game, playSetId, cancellationToken).ConfigureAwait(false);
        var plan = PlaySetShareMapper.BuildExport(game, playSet, mods);

        try
        {
            var directory = _fileSystem.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _fileSystem.EnsureDirectory(directory);
            }

            _fileSystem.WriteAllTextAtomic(destinationPath, plan.Document.ToJson());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("导出播放集失败（gameId=" + game.GameId + "，path=" + destinationPath + "）", ex);
            return AppResult<PlaySetShareExportResult>.Fail(ErrorKind.Io, ex.Message);
        }

        _logger.Info("已导出播放集「" + playSet.Name + "」（gameId=" + game.GameId
            + "，mod=" + plan.Document.Mods.Count
            + "，本地排除=" + plan.LocalExcluded
            + "，无法表达=" + plan.UnsupportedMods.Count + "）");

        return AppResult<PlaySetShareExportResult>.Ok(new PlaySetShareExportResult(
            destinationPath,
            plan.Document.Mods.Count,
            plan.LocalExcluded,
            plan.UnsupportedMods.Select(static mod => mod.Name).ToArray()));
    }

    /// <summary>
    /// 只解析文件、不写库：界面用它判断是否要先问「合并到同名播放集还是新建副本」。
    /// </summary>
    public async Task<AppResult<PlaySetSharePeek>> PeekAsync(
        GameInstallation game,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        var read = ReadDocument(game, sourcePath);
        if (read.Document is null)
        {
            return AppResult<PlaySetSharePeek>.Fail(read.Kind, read.ErrorCode ?? PlaySetShareDocument.ErrorInvalidJson);
        }

        var playSets = await _playSets.GetPlaySetsAsync(game, cancellationToken).ConfigureAwait(false);
        var existing = playSets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, read.Document.Name, StringComparison.OrdinalIgnoreCase));

        return AppResult<PlaySetSharePeek>.Ok(new PlaySetSharePeek(
            read.Document.Game,
            read.Document.Name,
            read.Document.Mods.Count,
            existing?.Id,
            existing?.Name));
    }

    /// <summary>
    /// 从官方分享文件导入。
    /// <para>
    /// <paramref name="mergeIntoPlaySetId"/> 非空时合并进该播放集（保留集内已有条目，
    /// 同 id 的条目只更新启用状态），用于「先订阅、Steam 下载完再导入同一份文件补齐」的二次流程；
    /// 为空时新建播放集，重名自动追加序号。
    /// </para>
    /// </summary>
    public async Task<AppResult<PlaySetShareImportResult>> ImportAsync(
        GameInstallation game,
        string sourcePath,
        Guid? mergeIntoPlaySetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        var read = ReadDocument(game, sourcePath);
        if (read.Document is null)
        {
            return AppResult<PlaySetShareImportResult>.Fail(read.Kind, read.ErrorCode ?? PlaySetShareDocument.ErrorInvalidJson);
        }

        var document = read.Document;
        var library = await _playSets.GetModLibraryAsync(game, cancellationToken).ConfigureAwait(false);
        var resolution = PlaySetShareMapper.Resolve(game, document, library);
        if (resolution.DuplicateIds.Count > 0)
        {
            return AppResult<PlaySetShareImportResult>.Fail(
                ErrorKind.Invalid,
                "share-duplicate-mods:" + string.Join(", ", resolution.DuplicateIds));
        }

        var playSets = await _playSets.GetPlaySetsAsync(game, cancellationToken).ConfigureAwait(false);

        Guid playSetId;
        string playSetName;
        var merged = false;
        var addedMods = resolution.Resolved.Count;
        IReadOnlyList<PlaySetMod> modsToSave = resolution.Resolved;

        if (mergeIntoPlaySetId is { } targetId)
        {
            var target = playSets.FirstOrDefault(candidate => candidate.Id == targetId);
            if (target is null)
            {
                return AppResult<PlaySetShareImportResult>.Fail(
                    ErrorKind.NotFound,
                    "playSet-not-found:" + targetId.ToString("D"));
            }

            merged = true;
            playSetId = target.Id;
            playSetName = target.Name;

            var existing = await _playSets
                .GetPlaySetModsAsync(game, targetId, cancellationToken)
                .ConfigureAwait(false);
            modsToSave = MergeMods(existing, resolution.Resolved, out addedMods);
        }
        else
        {
            playSetName = PlaySetShareMapper.EnsureUniqueName(
                document.Name,
                playSets.Select(static candidate => candidate.Name));

            var created = await _playSets
                .CreatePlaySetAsync(game, playSetName, cancellationToken)
                .ConfigureAwait(false);
            if (!created.IsSuccess)
            {
                return AppResult<PlaySetShareImportResult>.Fail(
                    created.Kind,
                    created.ErrorMessage ?? "share-create-failed");
            }

            playSetId = created.Value;
        }

        // 新建空播放集时也要写一次，保证仓储的 updatedOn 等元数据与 UI 一致；
        // 合并且没有任何新条目时可以跳过（避免无意义的整表重写）。
        if (modsToSave.Count > 0 || !merged)
        {
            var saved = await _playSets
                .SavePlaySetModsAsync(game, playSetId, modsToSave, cancellationToken)
                .ConfigureAwait(false);
            if (!saved.IsSuccess)
            {
                // 新建流程里保存失败会把刚建的播放集留在列表里（名字还可能占了「X (2)」），
                // 尽量回滚；回滚失败只记日志，不让错误码变形。
                if (!merged)
                {
                    var cleanup = await _playSets
                        .DeletePlaySetAsync(game, playSetId, cancellationToken)
                        .ConfigureAwait(false);
                    if (!cleanup.IsSuccess)
                    {
                        _logger.Warn("导入失败后回滚播放集失败（playSetId=" + playSetId.ToString("N") + "）："
                            + (cleanup.ErrorMessage ?? cleanup.Kind.ToString()));
                    }
                }

                return AppResult<PlaySetShareImportResult>.Fail(
                    saved.Kind,
                    saved.ErrorMessage ?? "share-save-failed");
            }
        }

        _logger.Info("已导入播放集「" + playSetName + "」（gameId=" + game.GameId
            + "，合并=" + merged
            + "，解析=" + resolution.Resolved.Count
            + "，缺失=" + resolution.Missing.Count + "）");

        return AppResult<PlaySetShareImportResult>.Ok(new PlaySetShareImportResult(
            playSetId,
            playSetName,
            merged,
            resolution.Resolved.Count,
            addedMods,
            resolution.Missing));
    }

    private sealed record DocumentRead(PlaySetShareDocument? Document, string? ErrorCode, ErrorKind Kind);

    /// <summary>读文件 → 解析 → 校验游戏 id。失败时返回稳定错误码，不抛给 UI。</summary>
    private DocumentRead ReadDocument(GameInstallation game, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !_fileSystem.FileExists(sourcePath))
        {
            return new DocumentRead(null, "share-file-not-found:" + sourcePath, ErrorKind.NotFound);
        }

        string text;
        try
        {
            text = _fileSystem.ReadAllText(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("读取播放集分享文件失败（path=" + sourcePath + "）", ex);
            return new DocumentRead(null, ex.Message, ErrorKind.Io);
        }

        var document = PlaySetShareDocument.TryParse(text, out var parseError);
        if (document is null)
        {
            return new DocumentRead(null, parseError ?? PlaySetShareDocument.ErrorInvalidJson, ErrorKind.Invalid);
        }

        if (!PlaySetShareMapper.MatchesGame(game, document))
        {
            return new DocumentRead(null, "share-wrong-game:" + document.Game, ErrorKind.Invalid);
        }

        return new DocumentRead(document, null, ErrorKind.Unknown);
    }

    /// <summary>
    /// 合并条目：分享文件里的顺序优先；文件里没有、但播放集里已有的条目排在末尾并保留原启用状态。
    /// 同 id 条目以分享文件的启用状态为准。
    /// </summary>
    private static IReadOnlyList<PlaySetMod> MergeMods(
        IReadOnlyList<PlaySetMod> existing,
        IReadOnlyList<PlaySetMod> imported,
        out int addedMods)
    {
        var existingById = new Dictionary<Guid, PlaySetMod>(existing.Count);
        foreach (var mod in existing)
        {
            existingById[mod.ModId] = mod;
        }

        var merged = new List<PlaySetMod>(existing.Count + imported.Count);
        var used = new HashSet<Guid>();
        var position = 0;
        addedMods = 0;

        foreach (var mod in imported)
        {
            if (existingById.TryGetValue(mod.ModId, out var current))
            {
                used.Add(mod.ModId);
                merged.Add(current with { Enabled = mod.Enabled, Position = position++ });
                continue;
            }

            addedMods++;
            merged.Add(mod with { Position = position++ });
        }

        foreach (var mod in existing.OrderBy(static mod => mod.Position))
        {
            if (used.Add(mod.ModId))
            {
                merged.Add(mod with { Position = position++ });
            }
        }

        return merged;
    }
}
