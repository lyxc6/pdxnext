namespace PdxLauncher.Core.Saves;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Games;

/// <summary>
/// 存档扫描器：一次递归枚举 + 固定并行度 + 稳定排序。
/// <para>
/// 单次扫描最多收集 <see cref="DefaultMaxFiles"/> 个候选文件；达到上限即停止枚举并记日志，
/// 避免把「几万个文件」的目录拖成全盘遍历。解析交给 <see cref="SavContainerReader"/>，
/// 单文件失败只降级该条结果。
/// </para>
/// </summary>
public sealed class SaveGameScanner : ISaveGameLibrary, ISaveGameFileIndex
{
    /// <summary>解析并行度。</summary>
    public const int MaxDegreeOfParallelism = 4;

    /// <summary>单次扫描的候选文件上限。</summary>
    public const int DefaultMaxFiles = 2000;

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly SaveLocationCatalog _locations;
    private readonly SavContainerReader _reader;
    private readonly int _maxFiles;

    private IReadOnlyList<SaveGameMeta> _snapshot = [];

    /// <summary>创建扫描器。</summary>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="logger">日志。</param>
    /// <param name="locations">存档落点表；null 时使用系统默认路径。</param>
    /// <param name="reader">头解析器；null 时新建。</param>
    /// <param name="maxFiles">候选上限；仅自检需要缩小。</param>
    public SaveGameScanner(
        IFileSystem fileSystem,
        IAppLogger logger,
        SaveLocationCatalog? locations = null,
        SavContainerReader? reader = null,
        int maxFiles = DefaultMaxFiles)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        if (maxFiles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFiles), maxFiles, "候选上限必须大于 0。");
        }

        _fileSystem = fileSystem;
        _logger = logger;
        _locations = locations ?? new SaveLocationCatalog(fileSystem);
        _reader = reader ?? new SavContainerReader(fileSystem, logger);
        _maxFiles = maxFiles;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SaveGameMeta>> ScanAsync(
        IEnumerable<string> gameIds,
        CancellationToken cancellationToken)
    {
        var ids = NormalizeGameIds(gameIds);
        if (ids.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<SaveGameMeta>>([]);
        }

        return Task.Run(() => ScanCoreAsync(ids, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public Task<SaveFileScanResult> ScanFilesAsync(
        IEnumerable<string> gameIds,
        CancellationToken cancellationToken)
    {
        var ids = NormalizeGameIds(gameIds);
        if (ids.Length == 0)
        {
            return Task.FromResult(new SaveFileScanResult([], [], false));
        }

        return Task.Run(() => ScanFilesCore(ids, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public SaveGameMeta ParseFile(SaveFileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // 复用与全量扫描完全相同的解析器；把第一段已经取到的大小/时间传进去，
        // 避免解析阶段再对同一文件重复 stat。
        return _reader.Parse(entry.GameId, entry.FilePath, entry.Container, entry.FileSize, entry.LastWriteUtc);
    }

    /// <inheritdoc />
    public IReadOnlyList<SaveGameMeta> GetAll() => _snapshot;

    /// <inheritdoc />
    public IReadOnlyList<SaveGameMeta> GetByGame(string gameId)
    {
        var canonical = KnownGames.CanonicalizeGameId(gameId);
        if (canonical.Length == 0)
        {
            return [];
        }

        return _snapshot
            .Where(meta => string.Equals(meta.GameId, canonical, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<SaveLocationInfo> GetLocations(string gameId) => _locations.GetLocations(gameId);

    private async Task<IReadOnlyList<SaveGameMeta>> ScanCoreAsync(
        IReadOnlyList<string> gameIds,
        CancellationToken cancellationToken)
    {
        var candidates = new List<Candidate>();
        var truncated = false;

        foreach (var gameId in gameIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var location in _locations.Resolve(gameId))
            {
                Collect(location, location.Directory, 0, candidates, ref truncated, cancellationToken);
                if (truncated)
                {
                    break;
                }
            }

            if (truncated)
            {
                break;
            }
        }

        if (truncated)
        {
            _logger.Warn($"存档扫描达到候选上限 {_maxFiles}，结果已截断。");
        }

        var results = new SaveGameMeta[candidates.Count];
        if (candidates.Count > 0)
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = cancellationToken,
            };
            await Parallel.ForEachAsync(
                Enumerable.Range(0, candidates.Count),
                options,
                (index, _) =>
                {
                    var candidate = candidates[index];
                    results[index] = _reader.Parse(candidate.GameId, candidate.FilePath, candidate.Container);
                    return ValueTask.CompletedTask;
                }).ConfigureAwait(false);
        }

        var ordered = results
            .OrderBy(static meta => meta.GameId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static meta => meta.FileName, StringComparer.Ordinal)
            .ThenBy(static meta => meta.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // 按 gameId 合并：本次扫描到的游戏整体替换（扫描为空也要清掉旧结果），
        // 未参与本次扫描的游戏继续保留上一次结果。
        var scanned = new HashSet<string>(gameIds, StringComparer.OrdinalIgnoreCase);
        var merged = _snapshot
            .Where(meta => !scanned.Contains(meta.GameId))
            .Concat(ordered)
            .OrderBy(static meta => meta.GameId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static meta => meta.FileName, StringComparer.Ordinal)
            .ThenBy(static meta => meta.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _snapshot = Array.AsReadOnly(merged);
        return _snapshot;
    }

    /// <summary>
    /// 第一段：只递归枚举候选文件并读取大小/文件时间，不解压、不打开内容。
    /// 候选收集规则与 <see cref="ScanCoreAsync"/> 共用 <see cref="Collect"/>，深度/扩展名/上限完全一致。
    /// </summary>
    private SaveFileScanResult ScanFilesCore(
        IReadOnlyList<string> gameIds,
        CancellationToken cancellationToken)
    {
        var locations = new List<SaveLocationInfo>();
        var candidates = new List<Candidate>();
        var truncated = false;

        foreach (var gameId in gameIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            locations.AddRange(_locations.GetLocations(gameId));

            foreach (var location in _locations.Resolve(gameId))
            {
                Collect(location, location.Directory, 0, candidates, ref truncated, cancellationToken);
                if (truncated)
                {
                    break;
                }
            }

            if (truncated)
            {
                break;
            }
        }

        if (truncated)
        {
            _logger.Warn($"存档枚举达到候选上限 {_maxFiles}，列表已截断。");
        }

        var files = new List<SaveFileEntry>(candidates.Count);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = SaveFileEntry.TryCreate(
                candidate.GameId,
                candidate.FilePath,
                _fileSystem.GetFileSize(candidate.FilePath),
                ReadLastWriteUtc(candidate.FilePath),
                candidate.Container);
            if (entry is not null)
            {
                files.Add(entry);
            }
        }

        var ordered = files
            .OrderBy(static file => file.GameId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static file => file.FileName, StringComparer.Ordinal)
            .ThenBy(static file => file.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SaveFileScanResult(Array.AsReadOnly(ordered), locations, truncated);
    }

    private static string[] NormalizeGameIds(IEnumerable<string> gameIds)
    {
        ArgumentNullException.ThrowIfNull(gameIds);

        return gameIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(KnownGames.CanonicalizeGameId)
            .Where(static id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 与 <see cref="SavContainerReader"/> 相同的时间来源（<c>File.GetLastWriteTimeUtc</c>）；
    /// 属性读不到时退回 <see cref="DateTimeOffset.MinValue"/>，让行里显示回退日期而不是崩。
    /// </summary>
    private static DateTimeOffset ReadLastWriteUtc(string filePath)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(filePath));
        }
        catch (Exception)
        {
            return DateTimeOffset.MinValue;
        }
    }

    private void Collect(
        SaveLocation location,
        string directory,
        int depth,
        List<Candidate> candidates,
        ref bool truncated,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (candidates.Count >= _maxFiles)
        {
            truncated = true;
            return;
        }

        if (!_fileSystem.DirectoryExists(directory))
        {
            return;
        }

        foreach (var file in _fileSystem.EnumerateFiles(directory, "*", false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidates.Count >= _maxFiles)
            {
                truncated = true;
                break;
            }

            if (!MatchesExtension(file, location.Extensions))
            {
                continue;
            }

            candidates.Add(new Candidate(location.GameId, file, location.Container));
        }

        if (truncated || depth >= location.MaxDepth)
        {
            return;
        }

        foreach (var child in _fileSystem.EnumerateDirectories(directory))
        {
            if (candidates.Count >= _maxFiles)
            {
                truncated = true;
                return;
            }

            Collect(location, child, depth + 1, candidates, ref truncated, cancellationToken);
            if (truncated)
            {
                return;
            }
        }
    }

    private static bool MatchesExtension(string filePath, IReadOnlyList<string> extensions)
    {
        var extension = Path.GetExtension(filePath);
        foreach (var allowed in extensions)
        {
            if (string.Equals(extension, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct Candidate(string GameId, string FilePath, SaveContainer Container);
}
