namespace PdxLauncher.Core.ModStudio;

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.LoadFiles;
using PdxLauncher.Core.Parsing;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 把一款已订阅的工坊 mod 复制成同内容的本地 mod：磁盘先行、写库在后，失败逆序补偿。
/// <para>
/// 设计要点（与 HANDOFF 评审遗留一致）：
/// <list type="bullet">
/// <item>目标固定在 <c>game.ModDir</c>，绝不动游戏安装目录；所有 path/archive 在暂存期就写最终绝对路径。</item>
/// <item>staging 取 ModDir 的兄弟目录 <c>.pdxnext-staging</c>，与落地目标必然同卷（Directory.Move 跨卷会抛）。</item>
/// <item>legacy 外层 <c>.mod</c> 写顶层并作为磁盘最后一步；回滚必须先删它，再删落地目录/zip，否则留下显示 missing 的幽灵 mod。</item>
/// <item>落地后用 <see cref="ModLoadRules.DescribeSkipKey"/> 强制 <c>Enabled=true</c> 自检，任何警告都按失败回滚，不谎报成功。</item>
/// <item>写库经 Router 的 WriteAsync，并检查返回值；自有库行在目标库成功之后写，失败用对称的反向仓储方法补偿。</item>
/// <item>磁盘副本只在「库不引用它」时删：尚未写库或反向补偿成功才删盘；补偿失败必须留住磁盘，
/// 否则库引用会指向已删掉的文件，正好造出本类要避免的 missing 幽灵 mod。</item>
/// </list>
/// </para>
/// </summary>
public sealed class WorkshopToLocalConverter
{
    private const string StagingFolderName = ".pdxnext-staging";
    private const string MarkerFileName = ".pdxnext-marker";
    private const string MarkerCreatedPrefix = "createdUtc=";
    private const int MaxNameAttempts = 99;
    private static readonly TimeSpan StagingTtl = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Regex TagsPattern = new(
        @"(?im)^\s*tags\s*=\s*\{([^}]*)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IFileSystem _fileSystem;
    private readonly IPlaySetRepository _playSets;
    private readonly IModCatalog _mods;
    private readonly IScanCacheInvalidator _scanInvalidator;
    private readonly IModArchivePackager _packager;
    private readonly OwnModStore _ownMods;
    private readonly IAppLogger _logger;
    private readonly IClock _clock;

    public WorkshopToLocalConverter(
        IFileSystem fileSystem,
        IPlaySetRepository playSets,
        IModCatalog mods,
        IScanCacheInvalidator scanInvalidator,
        IModArchivePackager packager,
        OwnModStore ownMods,
        IAppLogger logger,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(playSets);
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(scanInvalidator);
        ArgumentNullException.ThrowIfNull(packager);
        ArgumentNullException.ThrowIfNull(ownMods);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);

        _fileSystem = fileSystem;
        _playSets = playSets;
        _mods = mods;
        _scanInvalidator = scanInvalidator;
        _packager = packager;
        _ownMods = ownMods;
        _logger = logger;
        _clock = clock;
    }

    /// <summary>固定 API：把一个工坊 mod 转为本地 mod。</summary>
    public async Task<AppResult<WorkshopToLocalResult>> ConvertToLocalAsync(
        GameInstallation game,
        Guid oldModId,
        string targetFolderName,
        string? targetDisplayName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        // ---------------------------------------------------------------- 步骤 0：范围与输入守卫（全部在写操作之前）
        var family = ModDescriptorRules.FamilyFor(game.GameId);
        if (family == ModDescriptorFamily.None)
        {
            return Fail(ErrorKind.Unsupported, "unsupported-game");
        }

        if (game.Format == LoadFileFormat.PlaysetConfigV1)
        {
            return Fail(ErrorKind.Unsupported, "unsupported-format:playset-config");
        }

        if (string.IsNullOrWhiteSpace(game.ModDir))
        {
            return Fail(ErrorKind.Invalid, "no-mod-dir");
        }

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ModLibraryEntry> library;
        IReadOnlyList<PlaySetMod> catalog;
        try
        {
            library = await _playSets.GetModLibraryAsync(game, cancellationToken).ConfigureAwait(true);
            catalog = await _mods.DiscoverAllAsync(game, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(ErrorKind.Io, "read-mod-library:" + exception.Message);
        }

        var entry = library.FirstOrDefault(x => x.ModId == oldModId);
        var catalogMod = catalog.FirstOrDefault(x => x.ModId == oldModId);
        if (entry is null && catalogMod is null)
        {
            return Fail(ErrorKind.NotFound, "mod-not-found:" + oldModId.ToString("D"));
        }

        // 来源只按 ModOrigins.Normalize 判定：目录扫描的 catalog.Source 已被归一，优先于
        // EU5 ModLibrary 里硬编码的 Source="local"（它只把 SteamId 原样带过来）。
        var source = FirstNonBlank(catalogMod?.Source, entry?.Source);
        var steamId = FirstNonBlank(catalogMod?.SteamId, entry?.SteamId);
        if (ModOrigins.Normalize(source, steamId) != ModOrigin.Subscribed)
        {
            return Fail(ErrorKind.Invalid, "not-subscribed-mod");
        }

        // 展示名可能是中文：转换保留可读名字（CK3 等 UTF-8 描述符能加载中文目录），
        // 只有 Vic3 的 ASCII 限制在下面单独判。ModCreator.IsValidFolderName 是「新建 mod」页的
        // 保守口径，这里用更宽的本地 mod 目录规则，但不放松路径安全约束。
        // 不做 ToString.Normalize()：Windows 不区分 NFC/NFD，转形会让两个不同源名在库里对不上。
        var folder = (targetFolderName ?? string.Empty).Trim();
        if (!ModCreator.IsValidLocalModFolderName(folder))
        {
            return Fail(ErrorKind.Invalid, "invalid-folder-name");
        }

        // 排除 ugc_<数字>：SteamWorkshop.ExtractFromRegistryId 会把它反解成工坊 id，落库后又被判成 steam。
        if (SteamWorkshop.GetWorkshopId(null, "mod/" + folder + ".mod") is not null)
        {
            return Fail(ErrorKind.Invalid, "invalid-folder-workshop-id");
        }

        // Vic3：最终落地路径的父目录含非 ASCII → 立即拒绝（此时尚未做任何写操作）。
        if (GamePathRules.RequiresAsciiPaths(game.GameId))
        {
            var prospective = _fileSystem.Combine(game.ModDir, folder);
            if (ModLoadRules.ContainsNonAscii(_fileSystem.GetDirectoryName(prospective)))
            {
                return Fail(
                    ErrorKind.Invalid,
                    GamePathRules.DescribeAsciiRuleKey(game.GameId) ?? ModLoadRules.WarnNonAsciiParent);
            }
        }

        var needsArchive = game.Format == LoadFileFormat.ContentLoadV1;

        // 每次转换入口先清理过期暂存，避免半成品被 LocalModInventory.Merge 当成 unregistered。
        SafeCleanupAbandonedStaging(game);

        // ---------------------------------------------------------------- 步骤 1：定位工坊源
        var oldDirPath = FirstNonBlank(catalogMod?.DirPath, entry?.DirPath);
        var oldArchivePath = FirstNonBlank(catalogMod?.ArchivePath, entry?.ArchivePath);

        var isDirectorySource = !string.IsNullOrWhiteSpace(oldDirPath) && IsWorkshopPath(oldDirPath!);
        var isArchiveSource = !isDirectorySource
            && !string.IsNullOrWhiteSpace(oldArchivePath)
            && oldArchivePath!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        if (isDirectorySource && !_fileSystem.DirectoryExists(oldDirPath!))
        {
            return Fail(ErrorKind.NotFound, "workshop-source-dir-missing");
        }

        if (isArchiveSource && !_fileSystem.FileExists(oldArchivePath!))
        {
            return Fail(ErrorKind.NotFound, "workshop-source-archive-missing");
        }

        if (!isDirectorySource && !isArchiveSource)
        {
            return Fail(ErrorKind.NotFound, "workshop-source-not-found");
        }

        // metadata 家族靠扫目录找 .metadata/metadata.json，压缩包内元数据扫不到。
        if (family == ModDescriptorFamily.Metadata && isArchiveSource)
        {
            return Fail(ErrorKind.Unsupported, "unsupported-metadata-archive");
        }

        var oldName = FirstNonBlank(entry?.Name, catalogMod?.Name) ?? "Local Mod";
        var newName = string.IsNullOrWhiteSpace(targetDisplayName) ? oldName : targetDisplayName!.Trim();
        var requiredVersion = FirstNonBlank(entry?.RequiredVersion, catalogMod?.RequiredVersion) ?? string.Empty;
        var modVersion = FirstNonBlank(entry?.ModVersion, catalogMod?.ModVersion) ?? string.Empty;
        var status = FirstNonBlank(entry?.Status, catalogMod?.Status) ?? "ready_to_play";
        var thumbnailPath = FirstNonBlank(entry?.ThumbnailPath, catalogMod?.ThumbnailPath);
        var oldGameRegistryId = FirstNonBlank(catalogMod?.GameRegistryId, entry?.GameRegistryId) ?? string.Empty;
        var oldEnabled = catalogMod?.Enabled ?? true;
        var oldPosition = catalogMod?.Position ?? 0;

        // legacy 需要源描述符里的 name/version/supported_version/tags；优先读 ModDir 顶层的外层 .mod
        // （工坊指针文件带 remote_file_id），否则读工坊目录里的 descriptor.mod。
        string? sourceDescriptor = null;
        if (family == ModDescriptorFamily.Legacy)
        {
            var registryPath = ResolveRegistryPath(game, oldGameRegistryId);
            if (registryPath is not null && _fileSystem.FileExists(registryPath))
            {
                sourceDescriptor = TryReadText(registryPath);
            }

            if (string.IsNullOrWhiteSpace(sourceDescriptor) && isDirectorySource)
            {
                var inner = _fileSystem.Combine(oldDirPath!, "descriptor.mod");
                if (_fileSystem.FileExists(inner))
                {
                    sourceDescriptor = TryReadText(inner);
                }
            }

            if (string.IsNullOrWhiteSpace(sourceDescriptor))
            {
                // 源文件丢失时仍可继续：用库里的字段拼一份最小描述符，避免整个转换被一份 .mod 卡死。
                sourceDescriptor = BuildMinimalDescriptor(oldName, requiredVersion, modVersion);
                _logger.Warn("工坊转本地：未找到源描述符，已按库字段生成最小描述符（" + oldModId.ToString("N") + "）");
            }
        }

        // 归档落地：目录源 + ContentLoadV1 需要现场打包；归档源直接复制 zip。
        var landingIsZip = isArchiveSource || needsArchive;

        // ---------------------------------------------------------------- 步骤 2/3/3b/4/5/5b
        string? staging = null;
        string? landedOuter = null;
        string? landedDir = null;
        string? landedZip = null;
        var succeeded = false;
        var adopted = false;
        // 磁盘副本是否允许回滚。写库前库没有新 mod 引用，删盘安全；AdoptLocalModAsync 成功后
        // 库已指向新 ModId，只有反向补偿成功（库恢复成引用旧 mod、新行删除）才允许删盘。
        // 补偿失败时保持 false：宁可留一份有磁盘支撑的本地 mod 供人工处理，也不制造幽灵引用。
        var diskRollbackAllowed = true;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var token = "conv-" + _clock.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N")[..8];
            var stagingRoot = _fileSystem.Combine(game.ModDir, StagingFolderName);
            staging = _fileSystem.Combine(stagingRoot, token);
            var stagingContent = _fileSystem.Combine(staging, "content");
            var stagingZip = _fileSystem.Combine(staging, "content.zip");

            _fileSystem.EnsureDirectory(staging);
            _fileSystem.WriteAllTextAtomic(
                _fileSystem.Combine(staging, MarkerFileName),
                "purpose=workshop-to-local" + "\n"
                + MarkerCreatedPrefix + _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\n");

            // 复制/打包放 Task.Run 以免阻塞 UI：CopyDirectory 与 packager 内部逐文件检查取消，
            // 取消时抛 OperationCanceledException；已复制到 staging 的部分由外层 finally 清理，
            // 因此取消不需要等到整棵目录复制完才被识别。回 UI 用 ConfigureAwait(true)。
            if (isDirectorySource)
            {
                var sourceDir = oldDirPath!;
                await Task.Run(
                        () => _fileSystem.CopyDirectory(sourceDir, stagingContent, overwrite: true, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(true);

                // 目录型 legacy 会把工坊目录整棵复制进来，内层 descriptor.mod 仍带着 remote_file_id。
                // 必须在打包前清掉：zip 落地时之后再改 staging 已经进不到包里。
                if (family == ModDescriptorFamily.Legacy)
                {
                    ClearInnerDescriptorWorkshopId(stagingContent);
                }

                if (needsArchive)
                {
                    await _packager.CreateFromDirectory(stagingContent, stagingZip, cancellationToken).ConfigureAwait(true);
                }
            }
            else
            {
                var sourceZip = oldArchivePath!;
                await Task.Run(() => _fileSystem.Copy(sourceZip, stagingZip, overwrite: true), cancellationToken)
                    .ConfigureAwait(true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var finalFolder = ChooseFolderName(game, folder, landingIsZip);
            if (finalFolder is null)
            {
                return Fail(ErrorKind.Conflict, "folder-name-exhausted");
            }

            // 步骤 3：改写描述符，path/archive 在暂存期就写最终绝对路径。
            var stagedOuterText = BuildOuterDescriptor(
                family, sourceDescriptor, oldName, requiredVersion, modVersion,
                finalFolder, game.ModDir, landingIsZip, stagingContent);

            // 目录型 legacy 的内层 descriptor.mod 原样保留（CopyDirectory 已整棵复制），只改顶层外层 .mod。
            var finalDirPath = landingIsZip ? null : _fileSystem.Combine(game.ModDir, finalFolder);
            var finalZipPath = landingIsZip ? _fileSystem.Combine(game.ModDir, finalFolder + ".zip") : null;

            // 落地：同卷 Move 是原子的。
            if (landingIsZip)
            {
                var from = stagingZip;
                var to = finalZipPath!;
                await Task.Run(() => _fileSystem.Move(from, to, overwrite: false), cancellationToken).ConfigureAwait(true);
                landedZip = to;
            }
            else
            {
                var from = stagingContent;
                var to = finalDirPath!;
                await Task.Run(() => _fileSystem.MoveDirectory(from, to), cancellationToken).ConfigureAwait(true);
                landedDir = to;
            }

            // legacy 的外层 .mod 是磁盘最后一步：出现时内容已是最终形态。
            if (family == ModDescriptorFamily.Legacy && stagedOuterText is not null)
            {
                landedOuter = _fileSystem.Combine(game.ModDir, finalFolder + ".mod");
                _fileSystem.WriteAllTextAtomic(landedOuter, stagedOuterText);
            }

            // ---------------------------------------------------------------- 步骤 4：分配新 ModId，构造本地条目
            var newModId = Guid.NewGuid();
            var localMod = new PlaySetMod(
                ModId: newModId,
                Name: newName,
                DirPath: finalDirPath ?? string.Empty,
                ArchivePath: finalZipPath,
                GameRegistryId: family == ModDescriptorFamily.Legacy ? "mod/" + finalFolder + ".mod" : string.Empty,
                ThumbnailPath: thumbnailPath,
                RequiredVersion: requiredVersion,
                Status: status,
                Enabled: oldEnabled,
                Position: oldPosition,
                SteamId: null,
                Source: ModOrigins.LocalSource,
                ModVersion: modVersion);

            // 二次断言：新条目绝不能被反解出工坊 id（targetFolderName 守卫已经排除 ugc_<数字>）。
            if (SteamWorkshop.GetWorkshopId(localMod.SteamId, localMod.GameRegistryId) is not null)
            {
                return Fail(ErrorKind.Invalid, "new-mod-has-workshop-id");
            }

            // ---------------------------------------------------------------- 步骤 3b：落地后、写库前的加载可用性自检
            // DescribeSkipKey 对 !Enabled 直接返回 null，故这里必须强制 Enabled=true；
            // 持久化的 localMod.Enabled 仍等于旧值（启用状态不变），两者不可混用。
            var skipKey = ModLoadRules.DescribeSkipKey(
                game,
                localMod with { Enabled = true },
                directoryExists: path => _fileSystem.DirectoryExists(path),
                fileExists: path => _fileSystem.FileExists(path));
            if (skipKey is not null)
            {
                return Fail(ErrorKind.Invalid, skipKey);
            }

            // ---------------------------------------------------------------- 步骤 5：目标库写入（经 Router.WriteAsync）
            var adopt = await _playSets.AdoptLocalModAsync(
                game, oldModId, oldDirPath, oldArchivePath, localMod, cancellationToken).ConfigureAwait(true);
            if (!adopt.IsSuccess)
            {
                // 库写失败一律 AppResult.Fail，绝不静默当成功；sqlite 由 WriteAsync 的单事务回滚收敛。
                return Fail(adopt.Kind, adopt.ErrorMessage ?? "adopt-local-mod-failed");
            }

            adopted = true;
            // 从这一刻起库引用已改到新 ModId；在反向补偿成功前，finally 不能删磁盘副本。
            diskRollbackAllowed = false;

            // ---------------------------------------------------------------- 步骤 5b：自有库行在目标库成功之后写
            AppResult<bool> upsert;
            try
            {
                upsert = await Task.Run(
                    () => _ownMods.UpsertMods(game.GameId, new[] { localMod }),
                    cancellationToken).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // 5b 抛异常（含取消）：先反向补偿库引用，再向上抛；磁盘由外层 finally 按补偿结果决定。
                // 补偿必须用 CancellationToken.None，否则已被取消的 token 会让反向方法立刻拒绝执行。
                // 补偿结果不能丢弃：只有成功后才允许 finally 删盘，失败必须留住磁盘副本。
                var revert = await SafeRevertAsync(
                    game, newModId, oldModId, oldDirPath, oldArchivePath, finalDirPath, finalZipPath, adopted)
                    .ConfigureAwait(true);
                if (revert.IsSuccess)
                {
                    diskRollbackAllowed = true;
                }
                else
                {
                    LogRevertFailure(game, oldModId, newModId, finalDirPath, finalZipPath, revert.ErrorMessage);
                }

                throw;
            }

            if (!upsert.IsSuccess)
            {
                // 唯一跨库部分成功点：严格顺序先反向改引用、再删新行，确认恢复后才删磁盘副本。
                var revert = await SafeRevertAsync(
                    game, newModId, oldModId, oldDirPath, oldArchivePath, finalDirPath, finalZipPath, adopted)
                    .ConfigureAwait(true);
                if (revert.IsSuccess)
                {
                    // 库已恢复成引用旧 mod、新行已删，磁盘副本可以安全删除。
                    diskRollbackAllowed = true;
                    return Fail(upsert.Kind, upsert.ErrorMessage ?? "upsert-own-mod-failed");
                }

                // 反向方法本身失败：库仍指向新 mod，必须保留磁盘副本，让库与磁盘一致。
                // 只记 Error 与人工处理信息，不做整库恢复（避免撤销并发写入）。
                LogRevertFailure(game, oldModId, newModId, finalDirPath, finalZipPath, revert.ErrorMessage);
                return Fail(upsert.Kind, "upsert-own-mod-failed-revert-failed:" + revert.ErrorMessage);
            }

            succeeded = true;
            return AppResult<WorkshopToLocalResult>.Ok(
                new WorkshopToLocalResult(newModId, finalDirPath ?? string.Empty, finalZipPath));
        }
        catch (OperationCanceledException)
        {
            // 取消：外层 finally 按 diskRollbackAllowed 决定是否回滚磁盘；若 5b 之前已写库，
            // 上面的 catch 分支已完成补偿（或已记录补偿失败并保留磁盘）。
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
        {
            _logger.Error("工坊转本地失败（gameId=" + game.GameId + "）：" + exception.Message, exception);
            return Fail(ErrorKind.Io, "convert-to-local:" + exception.Message);
        }
        finally
        {
            // 回滚顺序：顶层外层 .mod → 落地目录/zip → staging（staging 单独删，绝不留幽灵 mod）。
            // 只有 !succeeded 且库已不引用新 mod 时才能删落地副本；反向补偿失败时保留磁盘，
            // 让「库引用」与「磁盘内容」要么同时存在、要么同时不存在。
            if (!succeeded && diskRollbackAllowed)
            {
                RollbackLanding(landedOuter, landedDir, landedZip);
            }

            TryDeleteAny(staging);

            // 步骤 6：成功与回滚都失效扫描缓存，避免 60 秒 TTL 内界面仍显示旧订阅/旧路径。
            InvalidateCache(game);
            SafeCleanupAbandonedStaging(game);
        }
    }

    /// <summary>
    /// 清理 <c>&lt;ModDir&gt;/.pdxnext-staging</c> 下带 marker 且超过 TTL 的目录。
    /// 供 App 启动时与每次转换入口各调一次。
    /// </summary>
    public void CleanupAbandonedStaging(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(game.ModDir))
        {
            return;
        }

        var root = _fileSystem.Combine(game.ModDir, StagingFolderName);
        if (!_fileSystem.DirectoryExists(root))
        {
            return;
        }

        var now = _clock.UtcNow;
        foreach (var directory in _fileSystem.EnumerateDirectories(root))
        {
            var marker = _fileSystem.Combine(directory, MarkerFileName);
            if (!_fileSystem.FileExists(marker))
            {
                continue;
            }

            DateTimeOffset created;
            try
            {
                var text = _fileSystem.ReadAllText(marker);
                var line = text.Split('\n').FirstOrDefault(
                    x => x.StartsWith(MarkerCreatedPrefix, StringComparison.Ordinal));
                created = line is not null
                    && DateTimeOffset.TryParse(
                        line[MarkerCreatedPrefix.Length..],
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var parsed)
                        ? parsed
                        : DateTimeOffset.MinValue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                created = DateTimeOffset.MinValue;
            }

            if (now - created < StagingTtl)
            {
                continue;
            }

            try
            {
                _fileSystem.Delete(directory);
                _logger.Info("已清理过期暂存目录：" + directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.Warn("清理暂存目录失败（" + directory + "）：" + exception.Message);
            }
        }
    }

    // ------------------------------------------------------------------ 私有工具

    private static AppResult<WorkshopToLocalResult> Fail(ErrorKind kind, string code) =>
        AppResult<WorkshopToLocalResult>.Fail(kind, code);

    private static string? FirstNonBlank(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : (!string.IsNullOrWhiteSpace(second) ? second : null);

    /// <summary>与 LocalModInventory.IsWorkshopPath 同口径：路径落在 steamapps/workshop/content 段内。</summary>
    private static bool IsWorkshopPath(string path)
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        return normalized.Contains(
            Path.DirectorySeparatorChar + "steamapps" + Path.DirectorySeparatorChar
            + "workshop" + Path.DirectorySeparatorChar + "content" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(
                Path.DirectorySeparatorChar + "workshop" + Path.DirectorySeparatorChar
                + "content" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>把 mod/xxx.mod 形式的注册 id 解析成绝对路径（绝对路径原样返回）。</summary>
    private string? ResolveRegistryPath(GameInstallation game, string? gameRegistryId)
    {
        if (string.IsNullOrWhiteSpace(gameRegistryId))
        {
            return null;
        }

        var normalized = gameRegistryId
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized)
            ? normalized
            : _fileSystem.Combine(game.GameDataDir, normalized);
    }

    private string? TryReadText(string path)
    {
        try
        {
            return _fileSystem.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("读取源描述符失败（" + path + "）：" + exception.Message);
            return null;
        }
    }

    private static string BuildMinimalDescriptor(string name, string supported, string version) =>
        "name=\"" + Escape(name) + "\"\n"
        + "version=\"" + Escape(version) + "\"\n"
        + "supported_version=\"" + Escape(supported) + "\"\n";

    /// <summary>在不撞名的前提下挑一个目录名（可含中文）；撞名按 _2.._99 递增，写库前再查一次唯一性。</summary>
    private string? ChooseFolderName(GameInstallation game, string baseName, bool isZip)
    {
        // 撞名判定必须大小写不敏感：Windows 上 `MyMod` 与 `mymod` 是同一目录，
        // 但 IFileSystem.DirectoryExists 可能因测试替身 / 网络盘给出 false，最后落到
        // MoveDirectory 才报已存在。父目录只需要枚举一次，99 次递增不必每次都重扫。
        var taken = EnumerateTakenNames(game.ModDir);
        for (var attempt = 1; attempt <= MaxNameAttempts; attempt++)
        {
            var candidate = attempt == 1
                ? baseName
                : baseName + "_" + attempt.ToString(CultureInfo.InvariantCulture);
            var directory = _fileSystem.Combine(game.ModDir, candidate);
            var archive = _fileSystem.Combine(game.ModDir, candidate + ".zip");
            var outer = _fileSystem.Combine(game.ModDir, candidate + ".mod");
            if (taken.Contains(candidate)
                || taken.Contains(candidate + ".zip")
                || taken.Contains(candidate + ".mod")
                || _fileSystem.DirectoryExists(directory)
                || _fileSystem.FileExists(archive)
                || _fileSystem.FileExists(outer))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    /// <summary>父目录下已有的条目名（忽略大小写）；父目录不存在时返回空集。</summary>
    private HashSet<string> EnumerateTakenNames(string parentDirectory)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!_fileSystem.DirectoryExists(parentDirectory))
        {
            return names;
        }

        foreach (var path in _fileSystem.EnumerateFiles(parentDirectory, "*", recursive: false)
                     .Concat(_fileSystem.EnumerateDirectories(parentDirectory)))
        {
            var name = _fileSystem.GetFileName(path);
            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// 生成 legacy 顶层外层 .mod 的文本；metadata 家族返回 null（内容在 staging 里改）。
    /// <para>保留源描述符的 name/version/supported_version/tags/picture/注释与键序，
    /// 删掉 remote_file_id，path/archive 只写最终绝对路径。</para>
    /// </summary>
    private string? BuildOuterDescriptor(
        ModDescriptorFamily family,
        string? sourceDescriptor,
        string oldName,
        string requiredVersion,
        string modVersion,
        string finalFolder,
        string modDir,
        bool landingIsZip,
        string stagingContent)
    {
        if (family == ModDescriptorFamily.Metadata)
        {
            ClearMetadataId(stagingContent);
            return null;
        }

        var source = sourceDescriptor ?? BuildMinimalDescriptor(oldName, requiredVersion, modVersion);
        var values = ClausewitzParser.Parse(source);
        var name = values.TryGetValue("name", out var parsedName) && !string.IsNullOrWhiteSpace(parsedName)
            ? parsedName
            : oldName;
        var version = values.TryGetValue("version", out var parsedVersion) ? parsedVersion : modVersion;
        var supported = values.TryGetValue("supported_version", out var parsedSupported)
            ? parsedSupported
            : requiredVersion;

        var text = LegacyDescriptorText.Apply(source, name, version, supported, ReadTags(source));
        text = LegacyDescriptorText.RemoveKey(text, "remote_file_id");
        text = LegacyDescriptorText.RemoveKey(text, "path");
        text = LegacyDescriptorText.RemoveKey(text, "archive");

        var finalAbsolute = landingIsZip
            ? _fileSystem.Combine(modDir, finalFolder + ".zip")
            : _fileSystem.Combine(modDir, finalFolder);
        text = LegacyDescriptorText.SetKeyValue(
            text,
            landingIsZip ? "archive" : "path",
            _fileSystem.GetFullPath(finalAbsolute).Replace('\\', '/'));
        return text;
    }

    /// <summary>目录型 legacy：删掉内层 <c>descriptor.mod</c> 里的 <c>remote_file_id</c>；文件不存在时跳过。</summary>
    private void ClearInnerDescriptorWorkshopId(string stagingContent)
    {
        var inner = _fileSystem.Combine(stagingContent, "descriptor.mod");
        if (!_fileSystem.FileExists(inner))
        {
            return;
        }

        var original = TryReadText(inner);
        if (string.IsNullOrWhiteSpace(original) || original.IndexOf("remote_file_id", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

        var updated = LegacyDescriptorText.RemoveKey(original, "remote_file_id");
        if (!string.Equals(updated, original, StringComparison.Ordinal))
        {
            _fileSystem.WriteAllTextAtomic(inner, updated);
        }
    }

    /// <summary>metadata 家族：把数字 id 清空为空串，保留其余键。</summary>
    private void ClearMetadataId(string stagingContent)
    {
        var metadataPath = _fileSystem.Combine(stagingContent, ".metadata", "metadata.json");
        if (!_fileSystem.FileExists(metadataPath))
        {
            return;
        }

        try
        {
            var original = _fileSystem.ReadAllText(metadataPath);
            var node = JsonNode.Parse(
                original,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Disallow,
                });
            if (node is not JsonObject root
                || !root.TryGetPropertyValue("id", out var idNode)
                || idNode is null)
            {
                return;
            }

            // metadata.json 的 id 常是 PDX 侧字符串；只有纯数字才是工坊 id，才需要清空。
            // ToJsonString 对字符串值会带引号，Trim('"') 后再判数字，避免依赖 JsonValue.ToString 的表现。
            var idText = idNode.ToJsonString().Trim().Trim('"');
            if (!SteamWorkshop.IsAllDigits(idText))
            {
                return;
            }

            root["id"] = string.Empty;
            var updated = node.ToJsonString(MetadataJsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
            if (!string.Equals(updated, original, StringComparison.Ordinal))
            {
                _fileSystem.WriteAllTextAtomic(metadataPath, updated);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            _logger.Warn("清空 metadata id 失败（" + metadataPath + "）：" + exception.Message);
            throw;
        }
    }

    private static IReadOnlyList<string> ReadTags(string text)
    {
        var match = TagsPattern.Match(text);
        if (!match.Success)
        {
            return Array.Empty<string>();
        }

        return match.Groups[1].Value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(static tag => tag.Trim().Trim('"'))
            .Where(static tag => tag.Length > 0)
            .ToArray();
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    /// <summary>回滚磁盘：顶层外层 .mod 必须先删，再删落地目录/zip；staging 由外层 finally 单独删。</summary>
    private void RollbackLanding(string? outer, string? directory, string? archive)
    {
        TryDeleteAny(outer);
        TryDeleteAny(directory);
        TryDeleteAny(archive);
    }

    /// <summary>记录「反向补偿失败」的人工处理信息；补偿失败会保留磁盘副本，不吞掉这个事实。</summary>
    private void LogRevertFailure(
        GameInstallation game,
        Guid oldModId,
        Guid newModId,
        string? newDirPath,
        string? newArchivePath,
        string? revertError)
    {
        _logger.Error(
            "工坊转本地：自有库行写入失败且反向恢复播放集引用失败，需人工处理。"
            + " gameId=" + game.GameId
            + " oldModId=" + oldModId.ToString("D")
            + " newModId=" + newModId.ToString("D")
            + " dir=" + (newDirPath ?? string.Empty)
            + " zip=" + (newArchivePath ?? string.Empty)
            + " revert=" + (revertError ?? string.Empty));
    }

    /// <summary>反向补偿库引用；反向失败不掩盖原始错误，只记 Error 与人工处理信息。</summary>
    private async Task<AppResult<bool>> SafeRevertAsync(
        GameInstallation game,
        Guid newModId,
        Guid oldModId,
        string? oldDirPath,
        string? oldArchivePath,
        string? newDirPath,
        string? newArchivePath,
        bool adopted)
    {
        if (!adopted)
        {
            return AppResult<bool>.Ok(true);
        }

        try
        {
            // 补偿是不可取消的收尾动作：用 CancellationToken.None，避免原始取消 token 让反向方法立刻拒绝。
            return await _playSets.RevertLocalModAdoptionAsync(
                game,
                newModId,
                oldModId,
                oldDirPath,
                oldArchivePath,
                CancellationToken.None,
                newDirPath,
                newArchivePath).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.Error(
                "工坊转本地：反向恢复播放集引用抛异常，需人工处理。"
                + " oldModId=" + oldModId.ToString("D")
                + " newModId=" + newModId.ToString("D")
                + " dir=" + (newDirPath ?? string.Empty)
                + " zip=" + (newArchivePath ?? string.Empty),
                exception);
            return AppResult<bool>.Fail(ErrorKind.Io, "revert-exception:" + exception.Message);
        }
    }

    private void InvalidateCache(GameInstallation game)
    {
        try
        {
            _scanInvalidator.InvalidateCache(game);
        }
        catch (Exception exception)
        {
            _logger.Warn("工坊转本地：扫描缓存失效失败：" + exception.Message);
        }
    }

    private void SafeCleanupAbandonedStaging(GameInstallation game)
    {
        try
        {
            CleanupAbandonedStaging(game);
        }
        catch (Exception exception)
        {
            _logger.Warn("工坊转本地：清理过期暂存失败：" + exception.Message);
        }
    }

    private void TryDeleteAny(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (_fileSystem.FileExists(path) || _fileSystem.DirectoryExists(path))
            {
                _fileSystem.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 磁盘回滚失败只记 Error，不掩盖原始错误；残留交 CleanupAbandonedStaging。
            _logger.Error("工坊转本地：删除失败（" + path + "）：" + exception.Message, exception);
        }
    }
}
