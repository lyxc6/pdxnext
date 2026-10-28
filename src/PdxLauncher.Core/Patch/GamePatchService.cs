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
    private const string BackupSuffix = "_o";

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
    private readonly string _statePath;
    private readonly Func<string, bool>? _isProcessRunning;

    public GamePatchService(
        IFileSystem fileSystem,
        IAppLogger logger,
        IPatchAssets assets,
        string statePath,
        Func<string, bool>? isProcessRunning = null)
    {
        _fs = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _statePath = statePath ?? throw new ArgumentNullException(nameof(statePath));
        _isProcessRunning = isProcessRunning;
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
            status = isBackup ? GamePatchStatus.SteamOverwrote : GamePatchStatus.PatchedOther;
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
            CollectPatchedFiles(targets));
    }

    // ------------------------------------------------------------ 打补丁

    /// <inheritdoc />
    public GamePatchReport Apply(GameInstallation game)
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

        var appId = before.SteamAppId!.Value;
        var targets = GamePatchProfiles.Resolve(_fs, game);
        var patched = new List<PatchAppliedFile>(targets.Count);

        try
        {
            if (!_assets.TryGetIniTemplate(out var template))
            {
                throw new InvalidOperationException("缺少 cream_api.ini 模板素材");
            }

            var iniText = BuildIni(template, appId);

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
                    // 只在「当前文件不是 loader」时备份：重复打补丁不会把 loader 自己备份成 _o，
                    // 从而保证移除补丁永远能回到打补丁之前的那份原文件。
                    _fs.WriteAllBytesAtomic(BackupPath(target.ApiPath), current);
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

        return Inspect(game);
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
    /// 用模板生成 ini：首行插标记，只改 <c>[steam]</c> 段的 <c>appid</c>，
    /// 其余（orgapi / orgapi64 / language / unlockall / 490 条 [dlc]）原样保留。
    /// </summary>
    private static string BuildIni(string template, int appId)
    {
        var text = template.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = text.Split('\n');
        var builder = new StringBuilder(text.Length + 128);
        builder.Append(Marker).Append("\r\n");

        var section = string.Empty;
        var replaced = false;
        foreach (var line in lines)
        {
            var current = line;
            if (!replaced
                && string.Equals(section, "steam", StringComparison.OrdinalIgnoreCase)
                && IsAppIdLine(current))
            {
                current = "appid = " + appId.ToString(CultureInfo.InvariantCulture);
                replaced = true;
            }

            builder.Append(current).Append("\r\n");

            var trimmed = current.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
            {
                section = trimmed[1..^1].Trim();
            }
        }

        if (!replaced)
        {
            throw new InvalidOperationException("cream_api.ini 模板里没有 appid 行");
        }

        return builder.ToString();
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
