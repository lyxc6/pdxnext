namespace PdxLauncher.Core.Saves;

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.Infrastructure;

/// <summary>
/// 存档备份库实现：备份、恢复、删除备份、安全删除真实存档、用量查询与显式清理。
/// <para>
/// 备份清单不写进 sqlite（不进 Core 的 <c>JsonDocumentKeys</c> 体系），而是与正文同目录的
/// <c>.bak.json</c> sidecar；这样备份目录本身可移植，删除<c>.bak</c> 不会留下孤儿记录。
/// </para>
/// <para>
/// 所有写入默认只落在 <see cref="BackupRootDirectory"/> 内；例外只有两处用户显式动作：
/// 恢复到原存档目录、删除真实存档时移入回收站。
/// </para>
/// </summary>
public sealed class SaveBackupService : ISaveGameBackupService
{
    internal const int SidecarSchemaVersion = 1;

    private const int CopyBufferBytes = 64 * 1024;
    private const int MaxConflictAttempts = 1000;
    private const string BackupSuccessLog = "存档备份完成";
    private const string BackupCancelledLog = "存档备份被取消";
    private const string BackupFailedLog = "存档备份失败";
    private const string RestoreSuccessLog = "存档恢复完成";
    private const string TrashLog = "存档已移入回收站";
    private const string MissingSidecarLog = "存档备份缺少 sidecar";

    private static readonly JsonSerializerOptions SidecarWriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions SidecarReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly IClock _clock;
    private readonly string _dataRoot;
    private readonly string _backupRoot;
    private readonly long _maxTotalBytes;
    private readonly int _maxEntriesPerGame;

    /// <param name="fileSystem">文件系统抽象（自检会传临时目录实现）。</param>
    /// <param name="logger">日志；关键路径用中文短语，便于 <c>grep</c>。</param>
    /// <param name="dataRoot">数据根覆盖；默认 <see cref="PdxDataRoot.Directory"/>。</param>
    /// <param name="clock">时钟覆盖；默认 <see cref="SystemClock.Instance"/>。</param>
    /// <param name="maxTotalBytes">备份总字节配额，默认 2 GiB。</param>
    /// <param name="maxEntriesPerGame">单游戏条目数配额，默认 200。</param>
    public SaveBackupService(
        IFileSystem fileSystem,
        IAppLogger logger,
        string? dataRoot = null,
        IClock? clock = null,
        long maxTotalBytes = SaveBackupPaths.DefaultMaxTotalBytes,
        int maxEntriesPerGame = SaveBackupPaths.DefaultMaxEntriesPerGame)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTotalBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntriesPerGame);

        _fileSystem = fileSystem;
        _logger = logger;
        _clock = clock ?? SystemClock.Instance;
        _dataRoot = string.IsNullOrWhiteSpace(dataRoot) ? PdxDataRoot.Directory : dataRoot!;
        _backupRoot = SaveBackupPaths.Root(_dataRoot);
        _maxTotalBytes = maxTotalBytes;
        _maxEntriesPerGame = maxEntriesPerGame;
    }

    /// <inheritdoc />
    public string BackupRootDirectory => _backupRoot;

    /// <inheritdoc />
    public async Task<SaveBackupResult> BackupAsync(
        string gameId,
        string sourcePath,
        SaveGameMeta? meta,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var canonicalGameId = KnownGames.CanonicalizeGameId(gameId);
        if (canonicalGameId.Length == 0)
        {
            return SaveBackupResult.Invalid("备份失败：gameId 为空。");
        }

        var fullSourcePath = NormalizePath(sourcePath);
        if (fullSourcePath is null)
        {
            return SaveBackupResult.Invalid("备份失败：源存档路径不合法。");
        }

        if (!_fileSystem.FileExists(fullSourcePath))
        {
            return SaveBackupResult.SourceMissing("备份失败：源存档不存在：" + fullSourcePath);
        }

        var snapshot = await MeasureAsync(cancellationToken).ConfigureAwait(false);
        var usage = snapshot.ToUsage();
        var sourceBytes = _fileSystem.GetFileSize(fullSourcePath);
        var gameFolder = SaveBackupPaths.SafeGameFolderName(canonicalGameId);

        if (snapshot.TotalBytes + sourceBytes > _maxTotalBytes)
        {
            var message = $"备份失败：备份总量将超过配额（现有 {snapshot.TotalBytes} 字节 + 本次 {sourceBytes} 字节，上限 {_maxTotalBytes} 字节）。";
            _logger.Warn(BackupFailedLog + "：配额超限，" + fullSourcePath);
            return SaveBackupResult.Quota(message, usage);
        }

        if (snapshot.CountFor(gameFolder) + 1 > _maxEntriesPerGame)
        {
            var message = $"备份失败：游戏 {canonicalGameId} 的备份条目将超过配额（现有 {snapshot.CountFor(gameFolder)} 个，上限 {_maxEntriesPerGame} 个）。";
            _logger.Warn(BackupFailedLog + "：条目数超限，" + canonicalGameId);
            return SaveBackupResult.Quota(message, usage);
        }

        var gameDirectory = SaveBackupPaths.GameDirectory(_dataRoot, canonicalGameId);
        try
        {
            _fileSystem.EnsureDirectory(gameDirectory);
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            var message = "备份失败：无法创建备份目录（" + DescribeIoError(exception) + "）。";
            _logger.Warn(BackupFailedLog + "：" + message);
            return SaveBackupResult.Failed(message);
        }

        var originalFileName = _fileSystem.GetFileName(fullSourcePath);
        var createdUtc = _clock.UtcNow;
        var localTime = createdUtc.ToLocalTime();
        var backupPath = FindFreeBackupPath(gameDirectory, localTime, originalFileName, cancellationToken);
        if (backupPath is null)
        {
            var message = "备份失败：同一时间戳下的备份文件名冲突过多（" + gameDirectory + "）。";
            _logger.Warn(BackupFailedLog + "：" + message);
            return SaveBackupResult.Failed(message);
        }

        var sidecarPath = SaveBackupPaths.SidecarPath(backupPath);
        var partialPath = backupPath + SaveBackupPaths.PartialExtension;

        try
        {
            var copy = await CopyAndHashAsync(fullSourcePath, partialPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _fileSystem.Move(partialPath, backupPath, overwrite: false);

            var sidecar = new SaveBackupSidecar(
                SidecarSchemaVersion,
                fullSourcePath,
                originalFileName,
                copy.Bytes,
                copy.Sha256,
                canonicalGameId,
                meta?.GameVersion,
                meta?.GameDate,
                meta?.SaveName,
                createdUtc);
            await WriteSidecarAsync(sidecarPath, sidecar, cancellationToken).ConfigureAwait(false);

            var entry = new SaveBackupEntry(
                backupPath,
                sidecarPath,
                fullSourcePath,
                originalFileName,
                copy.Bytes,
                copy.Sha256,
                canonicalGameId,
                meta?.GameVersion,
                meta?.GameDate,
                createdUtc);
            _logger.Info($"{BackupSuccessLog}：{fullSourcePath} → {backupPath}（{copy.Bytes} 字节，sha256 {ShortHash(copy.Sha256)}）");
            return SaveBackupResult.Success(entry);
        }
        catch (OperationCanceledException)
        {
            Cleanup(partialPath, backupPath, sidecarPath);
            _logger.Warn(BackupCancelledLog + "：" + fullSourcePath);
            throw;
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            var remaining = Cleanup(partialPath, backupPath, sidecarPath);
            var message = "备份失败：无法读取或写入存档（" + DescribeIoError(exception) + "）。";
            _logger.Warn(BackupFailedLog + "：" + message);
            return SaveBackupResult.Failed(message, remaining);
        }
    }

    /// <inheritdoc />
    public async Task<SaveRestoreResult> RestoreAsync(
        SaveBackupEntry entry,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(entry.BackupPath))
        {
            return SaveRestoreResult.Invalid("恢复失败：备份路径为空。");
        }

        var backupPath = entry.BackupPath;
        if (!_fileSystem.FileExists(backupPath))
        {
            return new SaveRestoreResult(
                SaveRestoreStatus.BackupMissing,
                "恢复失败：备份正文不存在：" + backupPath,
                null,
                null);
        }

        var sidecarPath = string.IsNullOrWhiteSpace(entry.SidecarPath)
            ? SaveBackupPaths.SidecarPath(backupPath)
            : entry.SidecarPath;
        if (!_fileSystem.FileExists(sidecarPath))
        {
            return new SaveRestoreResult(
                SaveRestoreStatus.SidecarMissing,
                "恢复失败：sidecar 不存在：" + sidecarPath,
                null,
                null);
        }

        SaveBackupSidecar sidecar;
        try
        {
            sidecar = ReadSidecar(sidecarPath);
        }
        catch (Exception exception) when (IsSidecarFailure(exception))
        {
            // JSON 解析器原文（如 "'t' is an invalid start of a property name..."）只进日志；
            // 面向用户只说文件坏了，别把解析器内部细节糊到 UI 上。
            _logger.Warn(BackupFailedLog + "：sidecar 无法解析 " + sidecarPath + "（" + exception.Message + "）");
            return SaveRestoreResult.Failed("恢复失败：备份信息文件已损坏，无法读取。");
        }

        if (sidecar.SizeBytes != _fileSystem.GetFileSize(backupPath))
        {
            var message = $"恢复失败：备份大小与 sidecar 不一致（sidecar 记录 {sidecar.SizeBytes} 字节）。";
            _logger.Warn(BackupFailedLog + "：" + message);
            return new SaveRestoreResult(SaveRestoreStatus.SizeMismatch, message, null, null);
        }

        if (entry.SizeBytes != sidecar.SizeBytes ||
            !string.Equals(entry.Sha256, sidecar.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(entry.OriginalPath, sidecar.OriginalPath, StringComparison.OrdinalIgnoreCase))
        {
            var message = "恢复失败：备份条目与 sidecar 不一致（条目已过期，请重新列出备份）。";
            _logger.Warn(BackupFailedLog + "：" + message);
            return SaveRestoreResult.Invalid(message);
        }

        if (!Path.IsPathRooted(sidecar.OriginalPath))
        {
            return SaveRestoreResult.Invalid("恢复失败：sidecar 记录的原始存档路径不是绝对路径。");
        }

        string originalPath;
        try
        {
            originalPath = _fileSystem.GetFullPath(sidecar.OriginalPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return SaveRestoreResult.Invalid("恢复失败：原始存档路径不合法。");
        }

        string actualHash;
        try
        {
            actualHash = await ComputeSha256Async(backupPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            var message = "恢复失败：无法读取备份正文（" + DescribeIoError(exception) + "）。";
            _logger.Warn(BackupFailedLog + "：" + message);
            return SaveRestoreResult.Failed(message);
        }

        if (!string.Equals(actualHash, sidecar.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            var message = "恢复失败：SHA-256 校验不一致，未写入任何文件。";
            _logger.Warn(BackupFailedLog + "：" + message);
            return new SaveRestoreResult(SaveRestoreStatus.HashMismatch, message, null, null);
        }

        var targetDirectory = _fileSystem.GetDirectoryName(originalPath);
        if (string.IsNullOrEmpty(targetDirectory))
        {
            return SaveRestoreResult.Invalid("恢复失败：无法确定原始存档目录。");
        }

        try
        {
            _fileSystem.EnsureDirectory(targetDirectory);
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            var message = "恢复失败：无法创建目标目录（" + DescribeIoError(exception) + "）。";
            _logger.Warn(BackupFailedLog + "：" + message);
            return SaveRestoreResult.Failed(message);
        }

        string? trashPath = null;
        var canonicalGameId = KnownGames.CanonicalizeGameId(sidecar.GameId);
        if (overwrite && _fileSystem.FileExists(originalPath))
        {
            var move = await MoveExistingSaveToTrashAsync(originalPath, canonicalGameId, cancellationToken).ConfigureAwait(false);
            if (!move.Success)
            {
                var message = "恢复失败：无法把现有存档移入回收站，未写入备份（" + move.Message + "）。";
                _logger.Warn(BackupFailedLog + "：" + message);
                return SaveRestoreResult.Failed(message, move.RemainingPaths);
            }

            trashPath = move.TrashPath;
        }

        var finalTarget = overwrite
            ? originalPath
            : FindFreeRestorePath(targetDirectory, sidecar.OriginalFileName, cancellationToken);
        if (finalTarget is null)
        {
            var message = "恢复失败：恢复目标文件名冲突过多（" + targetDirectory + "）。";
            _logger.Warn(BackupFailedLog + "：" + message);
            return SaveRestoreResult.Failed(message, remaining: null, trashPath: trashPath);
        }

        var partialRestorePath = finalTarget + SaveBackupPaths.RestorePartialExtension;
        try
        {
            var copy = await CopyAndHashAsync(backupPath, partialRestorePath, cancellationToken).ConfigureAwait(false);
            if (copy.Bytes != sidecar.SizeBytes ||
                !string.Equals(copy.Sha256, sidecar.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Cleanup(partialRestorePath);
                var message = "恢复失败：写入备份时校验不一致，已清理半截文件。";
                _logger.Warn(BackupFailedLog + "：" + message);
                return new SaveRestoreResult(SaveRestoreStatus.HashMismatch, message, null, trashPath);
            }

            _fileSystem.Move(partialRestorePath, finalTarget, overwrite);
            _logger.Info($"{RestoreSuccessLog}：{backupPath} → {finalTarget}");
            return SaveRestoreResult.Success(finalTarget, trashPath);
        }
        catch (OperationCanceledException)
        {
            Cleanup(partialRestorePath);
            throw;
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            var remaining = Cleanup(partialRestorePath);
            var message = "恢复失败：无法写入目标存档（" + DescribeIoError(exception) + "）。";
            _logger.Warn(BackupFailedLog + "：" + message);
            return SaveRestoreResult.Failed(message, remaining, trashPath);
        }
    }

    /// <inheritdoc />
    public Task<SaveDeleteResult> DeleteBackupAsync(SaveBackupEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => DeleteBackupCore(entry, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SaveDeleteResult> DeleteSaveAsync(
        string gameId,
        string savePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var canonicalGameId = KnownGames.CanonicalizeGameId(gameId);
        if (canonicalGameId.Length == 0)
        {
            return SaveDeleteResult.Invalid("删除存档失败：gameId 为空。");
        }

        var fullSavePath = NormalizePath(savePath);
        if (fullSavePath is null)
        {
            return SaveDeleteResult.Invalid("删除存档失败：存档路径不合法。");
        }

        if (!_fileSystem.FileExists(fullSavePath))
        {
            return SaveDeleteResult.NotFound("删除存档失败：存档不存在：" + fullSavePath);
        }

        var move = await MoveExistingSaveToTrashAsync(fullSavePath, canonicalGameId, cancellationToken).ConfigureAwait(false);
        if (!move.Success)
        {
            var message = "删除存档失败：" + move.Message;
            _logger.Warn(BackupFailedLog + "：" + message);
            return SaveDeleteResult.Failed(
                message,
                _fileSystem.FileExists(fullSavePath),
                move.RemainingPaths,
                move.TrashPath);
        }

        _logger.Info($"{TrashLog}：{fullSavePath} → {move.TrashPath}");
        return SaveDeleteResult.Ok("存档已移入回收站：" + move.TrashPath, move.TrashPath);
    }

    /// <inheritdoc />
    public Task<SaveBackupUsage> GetUsageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => MeasureBackups(cancellationToken).ToUsage(), cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SaveBackupEntry>> ListBackupsAsync(string gameId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var canonicalGameId = KnownGames.CanonicalizeGameId(gameId);
        if (canonicalGameId.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<SaveBackupEntry>>(Array.Empty<SaveBackupEntry>());
        }

        return Task.Run<IReadOnlyList<SaveBackupEntry>>(
            () => ListBackupsCore(canonicalGameId, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SaveBackupPruneResult> PruneAsync(
        string gameId,
        int keepNewest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (keepNewest < 0)
        {
            return new SaveBackupPruneResult(
                SaveBackupStatus.InvalidArgument,
                "清理失败：keepNewest 不能为负数。",
                0,
                0,
                SaveBackupUsage.Empty);
        }

        var canonicalGameId = KnownGames.CanonicalizeGameId(gameId);
        if (canonicalGameId.Length == 0)
        {
            return new SaveBackupPruneResult(
                SaveBackupStatus.InvalidArgument,
                "清理失败：gameId 为空。",
                0,
                0,
                SaveBackupUsage.Empty);
        }

        var backups = await Task.Run(() => EnumerateBackupsWithTimestamps(canonicalGameId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        var victims = backups.Skip(keepNewest).ToList();
        var deleted = 0;
        var failed = 0;
        var remaining = new List<string>();
        foreach (var victim in victims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await DeleteBackupAsync(victim, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                deleted++;
            }
            else
            {
                failed++;
                remaining.AddRange(result.RemainingPaths);
            }
        }

        var usage = await GetUsageAsync(cancellationToken).ConfigureAwait(false);
        var status = failed == 0 ? SaveBackupStatus.Ok : SaveBackupStatus.Failed;
        var message = failed == 0
            ? $"已清理 {deleted} 个备份。"
            : $"清理完成：删除 {deleted} 个，失败 {failed} 个。";
        return new SaveBackupPruneResult(status, message, deleted, failed, usage)
        {
            RemainingPaths = remaining,
        };
    }

    private string? FindFreeBackupPath(
        string gameDirectory,
        DateTimeOffset localTime,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        for (var suffix = 1; suffix < MaxConflictAttempts; suffix++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidateName = suffix == 1
                ? originalFileName
                : SaveBackupPaths.WithConflictSuffix(originalFileName, suffix);
            var candidatePath = Path.Combine(gameDirectory, SaveBackupPaths.BackupFileName(localTime, candidateName));
            if (!_fileSystem.FileExists(candidatePath) &&
                !_fileSystem.FileExists(SaveBackupPaths.SidecarPath(candidatePath)))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private string? FindFreeRestorePath(
        string targetDirectory,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        var localTime = _clock.UtcNow.ToLocalTime();
        var restoreName = SaveBackupPaths.RestoreFileName(originalFileName, localTime);
        for (var suffix = 1; suffix < MaxConflictAttempts; suffix++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidateName = suffix == 1
                ? restoreName
                : SaveBackupPaths.WithConflictSuffix(restoreName, suffix, SaveBackupPaths.MaxRestoreNameBytes);
            var candidatePath = Path.Combine(targetDirectory, candidateName);
            if (!_fileSystem.FileExists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private async Task<TrashMoveResult> MoveExistingSaveToTrashAsync(
        string sourcePath,
        string canonicalGameId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var trashDirectory = SaveBackupPaths.TrashDirectory(_dataRoot, canonicalGameId);
        string trashPath;
        try
        {
            _fileSystem.EnsureDirectory(trashDirectory);
            trashPath = FindFreeTrashPath(trashDirectory, _fileSystem.GetFileName(sourcePath), cancellationToken);
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            return new TrashMoveResult(
                false,
                null,
                "无法创建回收站目录（" + DescribeIoError(exception) + "）",
                Array.Empty<string>());
        }

        try
        {
            _fileSystem.Move(sourcePath, trashPath, overwrite: false);
            return new TrashMoveResult(true, trashPath, "已移入回收站。", Array.Empty<string>());
        }
        catch (IOException)
        {
            // File.Move 原生支持跨卷（内部 copy+delete），但目标被占用、只读属性、底层实现不支持
            // 等情况仍会抛 IOException；只有这时才退回「复制 → 校验 → 删源」，并保留源文件直到校验通过。
        }
        catch (UnauthorizedAccessException exception)
        {
            return new TrashMoveResult(
                false,
                null,
                "无法移动存档（" + DescribeIoError(exception) + "）",
                ExistingPaths(sourcePath));
        }

        try
        {
            var copy = await CopyAndHashAsync(sourcePath, trashPath, cancellationToken).ConfigureAwait(false);
            var trashHash = await ComputeSha256Async(trashPath, cancellationToken).ConfigureAwait(false);
            if (copy.Bytes != _fileSystem.GetFileSize(trashPath) ||
                !string.Equals(trashHash, copy.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                var remaining = Cleanup(trashPath);
                var present = new List<string>();
                if (_fileSystem.FileExists(sourcePath))
                {
                    present.Add(sourcePath);
                }

                present.AddRange(remaining);
                return new TrashMoveResult(
                    false,
                    null,
                    "跨卷复制校验失败，未删除源存档",
                    present);
            }

            _fileSystem.Delete(sourcePath);
            if (_fileSystem.FileExists(sourcePath))
            {
                return new TrashMoveResult(
                    false,
                    trashPath,
                    "回收站副本已写入，但源存档删除失败",
                    ExistingPaths(sourcePath, trashPath));
            }

            return new TrashMoveResult(true, trashPath, "已移入回收站。", Array.Empty<string>());
        }
        catch (OperationCanceledException)
        {
            Cleanup(trashPath);
            throw;
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            return new TrashMoveResult(
                false,
                _fileSystem.FileExists(trashPath) ? trashPath : null,
                "无法移动存档（" + DescribeIoError(exception) + "）",
                ExistingPaths(sourcePath, trashPath));
        }
    }

    private string FindFreeTrashPath(string trashDirectory, string originalFileName, CancellationToken cancellationToken)
    {
        var localTime = _clock.UtcNow.ToLocalTime();
        for (var suffix = 1; suffix < MaxConflictAttempts; suffix++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidateName = suffix == 1
                ? originalFileName
                : SaveBackupPaths.WithConflictSuffix(originalFileName, suffix);
            var candidatePath = Path.Combine(trashDirectory, SaveBackupPaths.TrashFileName(localTime, candidateName));
            if (!_fileSystem.FileExists(candidatePath))
            {
                return candidatePath;
            }
        }

        throw new IOException("回收站文件名冲突过多：" + trashDirectory);
    }

    private SaveDeleteResult DeleteBackupCore(SaveBackupEntry entry, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entry.BackupPath))
        {
            return SaveDeleteResult.Invalid("删除备份失败：备份路径为空。");
        }

        var sidecarPath = string.IsNullOrWhiteSpace(entry.SidecarPath)
            ? SaveBackupPaths.SidecarPath(entry.BackupPath)
            : entry.SidecarPath;
        var backupExists = _fileSystem.FileExists(entry.BackupPath);
        var sidecarExists = _fileSystem.FileExists(sidecarPath);
        if (!backupExists && !sidecarExists)
        {
            return SaveDeleteResult.NotFound("删除备份失败：备份与 sidecar 都不存在：" + entry.BackupPath);
        }

        var remaining = new List<string>();
        var backupRemoved = !backupExists;
        if (backupExists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _fileSystem.Delete(entry.BackupPath);
                backupRemoved = true;
            }
            catch (Exception exception) when (IsIoFailure(exception))
            {
                remaining.Add(entry.BackupPath);
                _logger.Warn(BackupFailedLog + "：无法删除备份正文 " + entry.BackupPath + "（" + exception.Message + "）");
            }
        }

        // 顺序很重要：先删正文、成功后再删 sidecar。若正文被占用删不掉而 sidecar 先消失，
        // ListBackups 会因为缺 sidecar 漏掉这条备份，UI 里就再也看不到这个孤儿了。
        if (backupRemoved && sidecarExists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _fileSystem.Delete(sidecarPath);
            }
            catch (Exception exception) when (IsIoFailure(exception))
            {
                remaining.Add(sidecarPath);
                _logger.Warn(BackupFailedLog + "：无法删除 sidecar " + sidecarPath + "（" + exception.Message + "）");
            }
        }

        var stillPresent = _fileSystem.FileExists(entry.BackupPath);
        if (remaining.Count > 0)
        {
            var message = $"删除备份失败：仍有 {remaining.Count} 个文件残留：{string.Join("、", remaining)}";
            _logger.Warn(BackupFailedLog + "：" + message);
            return SaveDeleteResult.Failed(message, stillPresent, remaining);
        }

        _logger.Info("已删除存档备份：" + entry.BackupPath);
        return SaveDeleteResult.Ok("备份已删除：" + entry.BackupPath);
    }

    private IReadOnlyList<SaveBackupEntry> ListBackupsCore(string canonicalGameId, CancellationToken cancellationToken)
    {
        var entries = new List<SaveBackupEntry>();
        var gameDirectory = SaveBackupPaths.GameDirectory(_dataRoot, canonicalGameId);
        if (!_fileSystem.DirectoryExists(gameDirectory))
        {
            return entries;
        }

        foreach (var backupPath in EnumerateBackupFiles(gameDirectory, cancellationToken))
        {
            var sidecarPath = SaveBackupPaths.SidecarPath(backupPath);
            if (!_fileSystem.FileExists(sidecarPath))
            {
                // 缺 sidecar 的 .bak 仍然列出（降级条目，sha 为空）：否则用量统计算上了它、
                // ListBackups 却看不到，UI 里永远删不掉这个孤儿，只能等显式 Prune。
                _logger.Warn(MissingSidecarLog + "，按降级条目列出：" + backupPath);
                entries.Add(CreateDegradedEntry(backupPath, sidecarPath, canonicalGameId));
                continue;
            }

            try
            {
                entries.Add(ToEntry(backupPath, sidecarPath, ReadSidecar(sidecarPath)));
            }
            catch (Exception exception) when (IsSidecarFailure(exception))
            {
                _logger.Warn(BackupFailedLog + "：sidecar 无法解析，按降级条目列出 " + sidecarPath + "（" + exception.Message + "）");
                entries.Add(CreateDegradedEntry(backupPath, sidecarPath, canonicalGameId));
            }
        }

        entries.Sort(static (left, right) =>
        {
            var byTime = right.CreatedUtc.CompareTo(left.CreatedUtc);
            return byTime != 0 ? byTime : string.CompareOrdinal(left.BackupPath, right.BackupPath);
        });
        return entries;
    }

    private IReadOnlyList<SaveBackupEntry> EnumerateBackupsWithTimestamps(
        string canonicalGameId,
        CancellationToken cancellationToken)
    {
        var entries = new List<SaveBackupEntry>();
        var gameDirectory = SaveBackupPaths.GameDirectory(_dataRoot, canonicalGameId);
        if (!_fileSystem.DirectoryExists(gameDirectory))
        {
            return entries;
        }

        foreach (var backupPath in EnumerateBackupFiles(gameDirectory, cancellationToken))
        {
            var sidecarPath = SaveBackupPaths.SidecarPath(backupPath);
            var createdUtc = _clock.UtcNow;
            try
            {
                if (_fileSystem.FileExists(sidecarPath))
                {
                    createdUtc = ReadSidecar(sidecarPath).CreatedUtc;
                }
                else
                {
                    createdUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(backupPath), TimeSpan.Zero);
                }
            }
            catch (Exception exception) when (IsSidecarFailure(exception))
            {
                createdUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(backupPath), TimeSpan.Zero);
            }

            entries.Add(new SaveBackupEntry(
                backupPath,
                sidecarPath,
                string.Empty,
                _fileSystem.GetFileName(backupPath),
                _fileSystem.GetFileSize(backupPath),
                string.Empty,
                canonicalGameId,
                null,
                null,
                createdUtc));
        }

        entries.Sort(static (left, right) =>
        {
            var byTime = right.CreatedUtc.CompareTo(left.CreatedUtc);
            return byTime != 0 ? byTime : string.CompareOrdinal(left.BackupPath, right.BackupPath);
        });
        return entries;
    }

    private IEnumerable<string> EnumerateBackupFiles(string gameDirectory, CancellationToken cancellationToken)
    {
        foreach (var path in _fileSystem.EnumerateFiles(gameDirectory, "*" + SaveBackupPaths.BackupExtension, recursive: false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return path;
        }
    }

    private static SaveBackupEntry ToEntry(string backupPath, string sidecarPath, SaveBackupSidecar sidecar) =>
        new(
            backupPath,
            sidecarPath,
            sidecar.OriginalPath,
            sidecar.OriginalFileName,
            sidecar.SizeBytes,
            sidecar.Sha256.ToLowerInvariant(),
            sidecar.GameId,
            sidecar.GameVersion,
            sidecar.GameDate,
            sidecar.CreatedUtc);

    /// <summary>
    /// sidecar 缺失或损坏时的降级条目：仍能列出、删除，但 sha 为空所以不能恢复。
    /// 这样「用量统计」与「列表」的条目集合保持一致，不会出现看不见却占空间的备份。
    /// </summary>
    private SaveBackupEntry CreateDegradedEntry(string backupPath, string sidecarPath, string canonicalGameId) =>
        new(
            backupPath,
            sidecarPath,
            string.Empty,
            _fileSystem.GetFileName(backupPath),
            _fileSystem.GetFileSize(backupPath),
            string.Empty,
            canonicalGameId,
            null,
            null,
            new DateTimeOffset(File.GetLastWriteTimeUtc(backupPath), TimeSpan.Zero));

    private SaveBackupSidecar ReadSidecar(string sidecarPath)
    {
        var json = _fileSystem.ReadAllText(sidecarPath);
        var sidecar = JsonSerializer.Deserialize<SaveBackupSidecar>(json, SidecarReadOptions);
        if (sidecar is null)
        {
            throw new FormatException("sidecar 内容为空。");
        }

        if (sidecar.SchemaVersion <= 0)
        {
            throw new FormatException("sidecar 缺少 schemaVersion。");
        }

        if (string.IsNullOrWhiteSpace(sidecar.OriginalPath) ||
            string.IsNullOrWhiteSpace(sidecar.OriginalFileName) ||
            string.IsNullOrWhiteSpace(sidecar.GameId))
        {
            throw new FormatException("sidecar 缺少原始路径、文件名或 gameId。");
        }

        if (sidecar.SizeBytes < 0)
        {
            throw new FormatException("sidecar 的 sizeBytes 不合法。");
        }

        if (!IsSha256Hex(sidecar.Sha256))
        {
            throw new FormatException("sidecar 的 sha256 不是 64 位十六进制。");
        }

        if (sidecar.CreatedUtc == default)
        {
            throw new FormatException("sidecar 缺少 createdUtc。");
        }

        return sidecar;
    }

    private Task WriteSidecarAsync(
        string sidecarPath,
        SaveBackupSidecar sidecar,
        CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                var json = JsonSerializer.Serialize(sidecar, SidecarWriteOptions);
                // 单条 sidecar 只有几百字节，交给 IFileSystem 的原子写（内部临时文件 + Move）即可；
                // 正文才需要流式异步复制。放在 Task.Run 里避免同步 IO 阻塞调用方线程。
                _fileSystem.WriteAllTextAtomic(sidecarPath, json);
            },
            cancellationToken);

    private Task<BackupSnapshot> MeasureAsync(CancellationToken cancellationToken) =>
        Task.Run(() => MeasureBackups(cancellationToken), cancellationToken);

    private BackupSnapshot MeasureBackups(CancellationToken cancellationToken)
    {
        var perGame = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        var entryCount = 0;
        if (!_fileSystem.DirectoryExists(_backupRoot))
        {
            return new BackupSnapshot(0, 0, perGame);
        }

        try
        {
            foreach (var directory in _fileSystem.EnumerateDirectories(_backupRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var gameFolder = _fileSystem.GetFileName(directory);
                if (string.Equals(gameFolder, SaveBackupPaths.TrashFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var gameEntries = 0;
                foreach (var path in _fileSystem.EnumerateFiles(
                             directory,
                             "*" + SaveBackupPaths.BackupExtension,
                             recursive: true))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    totalBytes += _fileSystem.GetFileSize(path);
                    gameEntries++;
                    entryCount++;
                }

                perGame[gameFolder] = gameEntries;
            }
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            _logger.Warn("统计备份用量时遇到 IO 错误（" + exception.Message + "），已返回部分统计。");
        }

        return new BackupSnapshot(totalBytes, entryCount, perGame);
    }

    private async Task<CopyOutcome> CopyAndHashAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        return await Task.Run(async () =>
        {
            var buffer = new byte[CopyBufferBytes];
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var source = OpenForRead(sourcePath);
            await using var destination = OpenForWrite(destinationPath);
            long totalBytes = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                totalBytes += read;
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            return new CopyOutcome(totalBytes, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            var buffer = new byte[CopyBufferBytes];
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var stream = OpenForRead(path);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
            }

            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }, cancellationToken);

    private static FileStream OpenForRead(string path) => new(
        path,
        new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            BufferSize = CopyBufferBytes,
        });

    private static FileStream OpenForWrite(string path) => new(
        path,
        new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            BufferSize = CopyBufferBytes,
        });

    private IReadOnlyList<string> Cleanup(params string[] paths)
    {
        var remaining = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            TryDelete(path);
            if (_fileSystem.FileExists(path))
            {
                remaining.Add(path);
            }
        }

        return remaining;
    }

    private void TryDelete(string path)
    {
        try
        {
            _fileSystem.Delete(path);
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            // 清理半截文件失败时保留现场，由调用方通过 RemainingPaths 报告。
        }
    }

    private IReadOnlyList<string> ExistingPaths(params string[] paths)
    {
        var existing = new List<string>();
        foreach (var path in paths)
        {
            if (!string.IsNullOrEmpty(path) && _fileSystem.FileExists(path))
            {
                existing.Add(path);
            }
        }

        return existing;
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(static character => Uri.IsHexDigit(character));

    private static string ShortHash(string sha256) =>
        sha256.Length > 12 ? sha256[..12] : sha256;

    private static string DescribeIoError(Exception exception) =>
        exception.Message + "（Win32 0x" +
        unchecked((uint)exception.HResult).ToString("X8", CultureInfo.InvariantCulture) + "）";

    private static bool IsIoFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException
            or System.Security.SecurityException;

    private static bool IsSidecarFailure(Exception exception) =>
        exception is JsonException or IOException or UnauthorizedAccessException or FormatException
            or NotSupportedException or System.Security.SecurityException or InvalidOperationException;

    private readonly record struct CopyOutcome(long Bytes, string Sha256);

    private readonly record struct TrashMoveResult(
        bool Success,
        string? TrashPath,
        string Message,
        IReadOnlyList<string> RemainingPaths);

    private readonly record struct BackupSnapshot(
        long TotalBytes,
        int EntryCount,
        IReadOnlyDictionary<string, int> PerGame)
    {
        public SaveBackupUsage ToUsage() => new(TotalBytes, EntryCount);

        public int CountFor(string gameFolder) =>
            PerGame.TryGetValue(gameFolder, out var count) ? count : 0;
    }
}
