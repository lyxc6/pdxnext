namespace PdxLauncher.Core.Patch;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;

/// <summary>
/// <see cref="IGamePatchService"/> 的默认实现。
/// <para>
/// 状态判定靠「文件内容对比」而不是只看备份是否存在：Steam 更新会把 loader 覆盖回原版，
/// 这时 <c>_o</c> 还在但补丁已经失效，必须让用户看到 <see cref="GamePatchStatus.SteamOverwrote"/>。
/// </para>
/// </summary>
public sealed class GamePatchService : IGamePatchService
{
    /// <summary>原文件备份后缀：<c>steam_api64.dll</c> → <c>steam_api64_o.dll</c>（CreamAPI 的约定）。</summary>
    public const string BackupSuffix = "_o";

    /// <summary>ini 首行标记；移除补丁时只删带这个标记的文件，用户自备的 ini 不动。</summary>
    private const string Marker = "; PdxNext 自动生成 — 可在「设置·高级」一键移除";

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IFileSystem _fs;
    private readonly IAppLogger _logger;
    private readonly IPatchAssets _assets;
    private readonly PatchVersionRegistry _versions;
    private readonly string _statePath;
    private readonly Func<string, bool>? _isProcessRunning;

    public GamePatchService(
        IFileSystem fileSystem,
        IAppLogger logger,
        IPatchAssets assets,
        string statePath,
        Func<string, bool>? isProcessRunning = null,
        PatchVersionRegistry? versions = null)
    {
        _fs = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _statePath = statePath ?? throw new ArgumentNullException(nameof(statePath));
        _isProcessRunning = isProcessRunning;
        _versions = versions ?? PatchVersionRegistry.Default;
    }

    /// <inheritdoc />
    public string AssetsDirectory => _assets.AssetsDirectory;

    /// <inheritdoc />
    public bool UsesExternalAssets => _assets.UsesExternalAssets;

    // ------------------------------------------------------------ 检查

    /// <inheritdoc />
    public GamePatchReport Inspect(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var appId = ResolveAppId(game);
        if (appId is null)
        {
            return new GamePatchReport(
                GamePatchStatus.Unsupported, null, null, null, null, false, false, Array.Empty<string>());
        }

        var targets = GamePatchProfiles.Resolve(_fs, game);
        if (targets.Count == 0)
        {
            return new GamePatchReport(
                GamePatchStatus.NotDetected, appId, null, null, null, false, false, Array.Empty<string>());
        }

        var primary = targets[0];
        if (!_assets.TryGetLoader(primary.IsX64, out var loader))
        {
            return new GamePatchReport(
                GamePatchStatus.Unsupported,
                appId,
                primary.ApiDirectory,
                primary.ApiFileName,
                "缺少 " + (primary.IsX64 ? "x64" : "x86") + " loader 素材",
                false,
                false,
                Array.Empty<string>());
        }

        byte[] current;
        try
        {
            current = _fs.ReadAllBytes(primary.ApiPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("读取 steam_api 失败：" + primary.ApiPath, ex);
            return new GamePatchReport(
                GamePatchStatus.Vanilla, appId, primary.ApiDirectory, primary.ApiFileName, ex.Message, false, false, Array.Empty<string>());
        }

        var backupPath = BackupPath(primary.ApiPath);
        var hasBackup = _fs.FileExists(backupPath);
        byte[]? backup = null;
        if (hasBackup)
        {
            try
            {
                backup = _fs.ReadAllBytes(backupPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warn("读取补丁备份失败：" + backupPath + " / " + ex.Message);
                hasBackup = false;
            }
        }

        var isLoader = current.AsSpan().SequenceEqual(loader);
        var isBackup = backup is not null && current.AsSpan().SequenceEqual(backup);
        var iniMarked = ReadIni(primary.ApiDirectory) is string ini
            && ini.Contains(Marker, StringComparison.Ordinal);

        // 版本识别：loader 在岗时 _o 才是原版，否则当前文件就是原版。
        var original = isLoader ? backup : current;
        KnownApiVersion? known = null;
        var versionState = PatchVersionState.NotChecked;
        if (original is not null)
        {
            known = _versions.Match(game.GameId, primary.ApiFileName, original);
            versionState = known is null ? PatchVersionState.Unknown : PatchVersionState.Known;
        }

        GamePatchStatus status;
        bool canApply;
        bool canRevert;
        if (isLoader)
        {
            canApply = true;
            canRevert = hasBackup;
            status = hasBackup
                ? (iniMarked ? GamePatchStatus.Patched : GamePatchStatus.PatchedOther)
                : GamePatchStatus.PatchedOther;
        }
        else if (hasBackup)
        {
            canApply = true;
            canRevert = true;

            // 以前只有 current == backup 才算被 Steam 覆盖，但真更新写回的是另一份原版，
            // 该条件几乎不可达。现在的判据：有 _o、当前文件不是自家 loader，且看起来是
            // 「可识别的原版」——与备份同字节、命中已知登记表、或与备份等长（更新不改变 api 大小）。
            var overwritten = isBackup
                || known is not null
                || (backup is not null && current.Length == backup.Length);
            status = overwritten ? GamePatchStatus.SteamOverwrote : GamePatchStatus.PatchedOther;
        }
        else
        {
            canApply = true;
            canRevert = false;
            status = GamePatchStatus.Vanilla;
        }

        return new GamePatchReport(
            status,
            appId,
            primary.ApiDirectory,
            primary.ApiFileName,
            null,
            canApply,
            canRevert,
            CollectPatchedFiles(targets))
        {
            VersionState = versionState,
            MatchedVersion = known?.Version,
            MatchedBuildId = known?.BuildId,
        };
    }

    // ------------------------------------------------------------ 打补丁

    /// <inheritdoc />
    public GamePatchReport Apply(GameInstallation game, bool allowUnverifiedVersion = false)
    {
        ArgumentNullException.ThrowIfNull(game);

        var before = Inspect(game);
        if (before.Status is GamePatchStatus.Unsupported or GamePatchStatus.NotDetected)
        {
            return before;
        }

        if (IsGameRunning(game))
        {
            return before with { Detail = "游戏正在运行，请先关闭游戏再打补丁。" };
        }

        // 版本闸门：目标不是已知原版就停在「未验证版本」，把决定权交回界面（用户确认后带
        // allowUnverifiedVersion 再打）。已经打过补丁的重复应用不拦，否则再点一次会莫名要求确认。
        if (!allowUnverifiedVersion
            && before.VersionState == PatchVersionState.Unknown
            && !before.IsPatched)
        {
            return before with
            {
                Status = GamePatchStatus.UnverifiedVersion,
                Detail = "unverified version: " + before.ApiFileName + " is not in the known-version table",
            };
        }

        var appId = before.SteamAppId!.Value;
        var targets = GamePatchProfiles.Resolve(_fs, game);
        var patched = new List<PatchAppliedFile>(targets.Count);
        var backupSuspect = false;
        string? archivePath = null;

        try
        {
            if (!_assets.TryGetIniTemplate(out var template))
            {
                throw new InvalidOperationException("缺少 cream_api.ini 模板素材");
            }

            if (!_assets.TryGetDlcCatalog(out var catalog))
            {
                throw new InvalidOperationException("missing dlc-catalog.json asset");
            }

            var problems = catalog.Validate();
            if (problems.Count > 0)
            {
                throw new InvalidOperationException("dlc-catalog.json validation failed: " + string.Join("; ", problems));
            }

            var iniText = BuildIni(template, appId, catalog);

            foreach (var target in targets)
            {
                if (!_assets.TryGetLoader(target.IsX64, out var loader))
                {
                    throw new InvalidOperationException(
                        "缺少 " + (target.IsX64 ? "x64" : "x86") + " loader 素材");
                }

                var current = _fs.ReadAllBytes(target.ApiPath);
                if (!current.AsSpan().SequenceEqual(loader))
                {
                    // 只在「当前文件不是 loader」时处理备份：重复打补丁不会把 loader 自己备份成 _o，
                    // 从而保证移除补丁永远能回到打补丁之前的那份原文件。
                    var backupPath = BackupPath(target.ApiPath);
                    var existing = _fs.FileExists(backupPath) ? _fs.ReadAllBytes(backupPath) : null;
                    if (existing is not null && !existing.AsSpan().SequenceEqual(current))
                    {
                        // 已有一份不同的 _o：它可能是别的 loader 留下的唯一原版，覆盖就再也还原不回去。
                        // 因此保留 _o 不动，把要替换掉的当前文件另存归档，并标注「备份来源存疑」。
                        archivePath ??= ArchivePath(target.ApiPath);
                        _fs.WriteAllBytesAtomic(archivePath, current);
                        if (!_versions.IsKnownOriginal(target.ApiFileName, existing))
                        {
                            backupSuspect = true;
                        }

                        _logger.Warn(
                            "已有备份与当前文件不一致，未覆盖备份：" + backupPath + " → 归档 " + archivePath);
                    }
                    else if (existing is null)
                    {
                        _fs.WriteAllBytesAtomic(backupPath, current);
                    }
                }

                _fs.WriteAllBytesAtomic(target.ApiPath, loader);
                patched.Add(new PatchAppliedFile(target.ApiPath, Convert.ToHexStringLower(SHA256.HashData(loader))));
            }

            foreach (var directory in targets.Select(t => t.ApiDirectory).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _fs.WriteAllTextAtomic(_fs.Combine(directory, PatchAssets.IniFileName), iniText);
            }

            UpsertManifest(game, patched, appId);
            _logger.Info("已打补丁：" + game.GameId + " / appId " + appId + " / " + string.Join(", ", patched));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.Error("打补丁失败：" + game.GameId, ex);
            return Inspect(game) with
            {
                Detail = ex.Message + "（如果游戏正在运行，请先关闭游戏；如果装在 Program Files 下，请用管理员身份运行）",
            };
        }

        return Inspect(game) with
        {
            BackupSuspect = backupSuspect,
            BackupArchivePath = archivePath,
        };
    }

    // ------------------------------------------------------------ 移除补丁

    /// <inheritdoc />
    public GamePatchReport Revert(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var before = Inspect(game);
        if (before.Status is GamePatchStatus.Unsupported or GamePatchStatus.NotDetected)
        {
            return before;
        }

        var targets = GamePatchProfiles.Resolve(_fs, game);
        var restored = 0;

        try
        {
            foreach (var target in targets)
            {
                var backupPath = BackupPath(target.ApiPath);
                if (!_fs.FileExists(backupPath))
                {
                    continue;
                }

                if (IsGameRunning(game))
                {
                    return before with { Detail = "游戏正在运行，请先关闭游戏再移除补丁。" };
                }

                _fs.Move(backupPath, target.ApiPath, overwrite: true);
                restored++;
            }

            foreach (var directory in targets.Select(t => t.ApiDirectory).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var iniPath = _fs.Combine(directory, PatchAssets.IniFileName);
                if (_fs.FileExists(iniPath)
                    && _fs.ReadAllText(iniPath).Contains(Marker, StringComparison.Ordinal))
                {
                    _fs.Delete(iniPath);
                }
            }

            RemoveManifest(game);
            _logger.Info("已移除补丁：" + game.GameId + " / 还原 " + restored + " 个文件");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("移除补丁失败：" + game.GameId, ex);
            return Inspect(game) with
            {
                Detail = ex.Message + "（如果游戏正在运行，请先关闭游戏；如果装在 Program Files 下，请用管理员身份运行）",
            };
        }

        return Inspect(game);
    }

    /// <inheritdoc />
    public IReadOnlyList<DlcEntry>? DlcCatalogFor(string? gameId)
    {
        // 素材读不到时返回 null（而不是空表）：空表会让页面把每条 DLC 都切成「补丁管不了」，
        // 而实际是「不知道」，两者对用户的含义完全不同。
        if (!_assets.TryGetDlcCatalog(out var catalog))
        {
            return null;
        }

        return catalog.ForGame(gameId);
    }

    // ------------------------------------------------------------ 内部

    private static int? ResolveAppId(GameInstallation game)
    {
        if (game.SteamAppId is int direct && direct > 0)
        {
            return direct;
        }

        return GameSteamAppIds.TryGet(game.GameId, out var mapped) ? mapped : null;
    }

    private bool IsGameRunning(GameInstallation game)
    {
        if (_isProcessRunning is null)
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(game.ExePath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            return _isProcessRunning(name);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Warn("检测游戏进程失败：" + name + " / " + ex.Message);
            return false;
        }
    }

    private static string BackupPath(string apiPath)
    {
        var directory = Path.GetDirectoryName(apiPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(apiPath);
        var extension = Path.GetExtension(apiPath);
        return Path.Combine(directory, name + BackupSuffix + extension);
    }

    /// <summary>
    /// 备份来源存疑时的归档路径：<c>steam_api64_o.bak-20261106-221530</c>（同秒冲突再补序号）。
    /// </summary>
    private string ArchivePath(string apiPath)
    {
        var directory = Path.GetDirectoryName(apiPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(apiPath);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var candidate = Path.Combine(directory, name + BackupSuffix + ".bak-" + stamp);
        var serial = 1;
        while (_fs.FileExists(candidate))
        {
            candidate = Path.Combine(
                directory,
                name + BackupSuffix + ".bak-" + stamp + "-" + serial.ToString(CultureInfo.InvariantCulture));
            serial++;
        }

        return candidate;
    }

    private string? ReadIni(string directory)
    {
        var path = _fs.Combine(directory, PatchAssets.IniFileName);
        if (!_fs.FileExists(path))
        {
            return null;
        }

        try
        {
            return _fs.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("读取 cream_api.ini 失败：" + path + " / " + ex.Message);
            return null;
        }
    }

    private List<string> CollectPatchedFiles(IReadOnlyList<PatchTarget> targets)
    {
        var list = new List<string>(targets.Count);
        foreach (var target in targets)
        {
            if (!_assets.TryGetLoader(target.IsX64, out var loader))
            {
                continue;
            }

            try
            {
                if (_fs.ReadAllBytes(target.ApiPath).AsSpan().SequenceEqual(loader))
                {
                    list.Add(target.ApiPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warn("读取 steam_api 失败：" + target.ApiPath + " / " + ex.Message);
            }
        }

        return list;
    }

    /// <summary>
    /// 用模板生成 ini：首行插标记，只改 <c>[steam]</c> 段的 <c>appid</c>，<c>[dlc]</c> 段的内容
    /// 由按游戏分节的清单（<see cref="PatchDlcCatalog"/>）重新生成，其余（orgapi / orgapi64 /
    /// language / unlockall / steam_misc）原样保留。
    /// <para>
    /// 模板里 <c>[dlc]</c> 之后的旧条目会被跳过：外部覆盖的 ini 若自带大表也不会与新清单重复。
    /// </para>
    /// </summary>
    private static string BuildIni(string template, int appId, PatchDlcCatalog catalog)
    {
        var text = template.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = text.Split('\n');
        var builder = new StringBuilder(text.Length + 4096);
        builder.Append(Marker).Append("\r\n");

        var section = string.Empty;
        var replaced = false;
        var dlcWritten = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var current = lines[i];
            if (!replaced
                && string.Equals(section, "steam", StringComparison.OrdinalIgnoreCase)
                && IsAppIdLine(current))
            {
                current = "appid = " + appId.ToString(CultureInfo.InvariantCulture);
                replaced = true;
            }

            builder.Append(current).Append("\r\n");

            if (!TrySectionName(current, out var name))
            {
                continue;
            }

            section = name;
            if (!dlcWritten && string.Equals(name, "dlc", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var dlcLine in catalog.DlcLines())
                {
                    builder.Append(dlcLine).Append("\r\n");
                }

                dlcWritten = true;

                // 模板 [dlc] 段里的旧条目以清单为准，整段跳过到下一个段头（或文件末尾）。
                while (i + 1 < lines.Length && !TrySectionName(lines[i + 1], out _))
                {
                    i++;
                }
            }
        }

        if (!replaced)
        {
            throw new InvalidOperationException("cream_api.ini 模板里没有 appid 行");
        }

        if (!dlcWritten)
        {
            throw new InvalidOperationException("cream_api.ini 模板里没有 [dlc] 段");
        }

        return builder.ToString();
    }

    private static bool TrySectionName(string line, out string name)
    {
        var trimmed = line.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            name = trimmed[1..^1].Trim();
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static bool IsAppIdLine(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '#')
        {
            return false;
        }

        var equals = trimmed.IndexOf('=');
        if (equals <= 0)
        {
            return false;
        }

        return string.Equals(trimmed[..equals].Trim(), "appid", StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------ 清单

    private PatchManifest LoadManifest()
    {
        if (!_fs.FileExists(_statePath))
        {
            return new PatchManifest();
        }

        try
        {
            var text = _fs.ReadAllText(_statePath);
            return string.IsNullOrWhiteSpace(text)
                ? new PatchManifest()
                : JsonSerializer.Deserialize<PatchManifest>(text, ManifestJson) ?? new PatchManifest();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Warn("补丁清单读取失败，按空清单处理：" + _statePath + " / " + ex.Message);
            return new PatchManifest();
        }
    }

    private void SaveManifest(PatchManifest manifest)
    {
        _fs.WriteAllTextAtomic(_statePath, JsonSerializer.Serialize(manifest, ManifestJson));
    }

    private static bool SameGame(ManifestEntry entry, GameInstallation game) =>
        string.Equals(entry.GameId, game.GameId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(entry.InstallRoot, game.InstallRoot, StringComparison.OrdinalIgnoreCase);

    private void UpsertManifest(GameInstallation game, IReadOnlyList<PatchAppliedFile> patched, int appId)
    {
        var manifest = LoadManifest();
        manifest.Entries.RemoveAll(entry => SameGame(entry, game));

        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        foreach (var file in patched)
        {
            manifest.Entries.Add(
                new ManifestEntry
                {
                    GameId = game.GameId,
                    InstallRoot = game.InstallRoot,
                    ApiFile = file.ApiFile,
                    BackupFile = BackupPath(file.ApiFile),
                    LoaderSha256 = file.LoaderSha256,
                    SteamAppId = appId,
                    AppliedUtc = now,
                });
        }

        SaveManifest(manifest);
    }

    private void RemoveManifest(GameInstallation game)
    {
        var manifest = LoadManifest();
        if (manifest.Entries.RemoveAll(entry => SameGame(entry, game)) > 0)
        {
            SaveManifest(manifest);
        }
    }

    private sealed class PatchManifest
    {
        public List<ManifestEntry> Entries { get; set; } = new();
    }

    /// <summary>刚写进安装目录的一对（api 文件, loader 摘要），只用于写清单。</summary>
    private readonly record struct PatchAppliedFile(string ApiFile, string LoaderSha256);

    private sealed class ManifestEntry
    {
        public string GameId { get; set; } = string.Empty;

        public string InstallRoot { get; set; } = string.Empty;

        public string ApiFile { get; set; } = string.Empty;

        public string BackupFile { get; set; } = string.Empty;

        public string LoaderSha256 { get; set; } = string.Empty;

        public int SteamAppId { get; set; }

        public string AppliedUtc { get; set; } = string.Empty;
    }
}
