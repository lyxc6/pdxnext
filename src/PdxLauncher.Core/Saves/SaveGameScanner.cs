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
public sealed class SaveGameScanner : ISaveGameLibrary
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
        ArgumentNullException.ThrowIfNull(gameIds);

        var ids = gameIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(KnownGames.CanonicalizeGameId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ids.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<SaveGameMeta>>([]);
        }

        return Task.Run(() => ScanCoreAsync(ids, cancellationToken), cancellationToken);
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
        _snapshot = Array.AsReadOnly(ordered);
        return _snapshot;
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
