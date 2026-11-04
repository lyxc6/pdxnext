namespace PdxLauncher.Core.Steam;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Patch;

/// <summary>
/// 把「补丁状态 + 游戏目录里那份 steam_api DLL 报告的 Steam 授权」合成一份判定报告。
/// <para>
/// 口径对齐官方启动器的 <c>SteamService</c>：拥有 = <c>BIsSubscribedApp</c>
/// || 可信 <c>DisabledDLC</c> || <c>BIsDlcInstalled</c>，其中 <c>DisabledDLC</c>
/// 只有在 <c>appmanifest</c> 的 <c>LastOwner</c> 等于当前账号时才采信。
/// </para>
/// <para>
/// 只读、不联网；失败一律降级成带 <see cref="SteamOwnershipReport.ErrorKey"/> 的报告，不抛异常。
/// </para>
/// </summary>
public sealed class SteamOwnershipService : ISteamOwnershipService
{
    private const string X64ApiFileName = "steam_api64.dll";

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly IGamePatchService _patchService;
    private readonly Func<IReadOnlyList<string>> _libraryRoots;
    private readonly ISteamDllProbe _probe;
    private readonly AppManifestLookup _manifests;
    private readonly object _cacheGate = new();

    private string? _cacheKey;
    private DateTime _cacheStampUtc;
    private SteamOwnershipReport? _cacheReport;

    /// <summary>构造。</summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <param name="logger">日志。</param>
    /// <param name="patchService">补丁服务（用来定位「游戏真正加载的那份 DLL」和当前补丁状态）。</param>
    /// <param name="libraryRoots">已知 Steam 库根目录（找 appmanifest 用）；可为 null。</param>
    /// <param name="probe">原生探针；默认用真实实现，自检可注入假件。</param>
    public SteamOwnershipService(
        IFileSystem fileSystem,
        IAppLogger logger,
        IGamePatchService patchService,
        Func<IReadOnlyList<string>>? libraryRoots = null,
        ISteamDllProbe? probe = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _patchService = patchService ?? throw new ArgumentNullException(nameof(patchService));
        _libraryRoots = libraryRoots ?? (() => Array.Empty<string>());
        _probe = probe ?? new SteamDllProbe();
        _manifests = new AppManifestLookup(_fileSystem, _logger);
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        lock (_cacheGate)
        {
            _cacheReport = null;
            _cacheKey = null;
        }
    }

    /// <inheritdoc />
    public SteamOwnershipReport Inspect(GameInstallation game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var patch = SafeInspect(game);
        var appId = ResolveAppId(game, patch);
        if (appId is null)
        {
            return CreateUnavailable(patch, null, null, SteamProbeError.NoAppId);
        }

        if (patch.ApiDirectory is null || string.IsNullOrWhiteSpace(patch.ApiFileName))
        {
            return CreateUnavailable(patch, appId, null, SteamProbeError.NoDll, patch.Detail);
        }

        var apiPath = _fileSystem.Combine(patch.ApiDirectory, patch.ApiFileName);
        if (!_fileSystem.FileExists(apiPath))
        {
            return CreateUnavailable(patch, appId, apiPath, SteamProbeError.NoDll);
        }

        if (!string.Equals(patch.ApiFileName, X64ApiFileName, StringComparison.OrdinalIgnoreCase))
        {
            return CreateUnavailable(patch, appId, apiPath, SteamProbeError.X86);
        }

        var size = _fileSystem.GetFileSize(apiPath);
        var cacheKey = BuildCacheKey(apiPath, size, appId.Value, patch);
        if (TryGetCached(cacheKey, out var cached))
        {
            return cached;
        }

        var result = _probe.Probe(apiPath, appId.Value);
        cancellationToken.ThrowIfCancellationRequested();

        AppManifest? manifest = null;
        if (result.Succeeded)
        {
            try
            {
                manifest = _manifests.Find(game, appId.Value, _libraryRoots());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warn("读取 appmanifest 失败（只影响 DisabledDLC 判定）：" + ex.Message);
            }
        }

        var report = Build(patch, appId.Value, apiPath, result, manifest);
        if (result.Succeeded && patch.IsLoaderActive && patch.BackupApiFilePath is not null)
        {
            // 主探测拿到的是 loader 的 [dlc] 表（含其他游戏，数字会虚高）。
            // 再探一次原版备份，单独回答「账号本身拥有多少」——失败只降级这一行，不影响主判定。
            report = report with
            {
                Account = ProbeAccount(patch, appId.Value, apiPath, size, manifest, report),
            };
        }

        Store(cacheKey, report);
        return report;
    }

    private SteamOwnershipReport Build(
        GamePatchReport patch,
        int appId,
        string apiPath,
        SteamDllProbeResult result,
        AppManifest? manifest)
    {
        var steamId = result.SteamId64;
        var ownerText = steamId == 0 ? null : steamId.ToString(CultureInfo.InvariantCulture);
        var manifestOwnerMatches = ownerText is not null
            && manifest?.LastOwner is { Length: > 0 } lastOwner
            && string.Equals(lastOwner.Trim(), ownerText, StringComparison.Ordinal);

        var (dlcs, ownedIds, trusted) = ApplyTrusted(result, manifest);

        if (!result.Succeeded)
        {
            _logger.Warn("Steam 授权探测未成功: " + result.Error + " " + (result.ErrorDetail ?? string.Empty));
        }

        return new SteamOwnershipReport(
            MapState(result),
            patch.Status,
            appId,
            patch.ApiDirectory,
            patch.ApiFileName,
            apiPath,
            steamId,
            manifest?.LastOwner,
            manifestOwnerMatches,
            dlcs,
            ownedIds,
            trusted,
            result.Succeeded ? null : result.Error,
            result.ErrorDetail,
            patch.IsLoaderActive);
    }

    /// <summary>
    /// 用原版备份再探一次，得到「账号真实拥有」。返回 null 表示没有备份、无从判断。
    /// </summary>
    private DlcAccountOwnership? ProbeAccount(
        GamePatchReport patch,
        int appId,
        string apiPath,
        long primarySize,
        AppManifest? manifest,
        SteamOwnershipReport primary)
    {
        var backupPath = patch.BackupApiFilePath;
        if (backupPath is null || !_fileSystem.FileExists(backupPath))
        {
            _logger.Info("没有原版备份（" + (backupPath ?? "<未知路径>") + "），跳过账号授权探测");
            return null;
        }

        try
        {
            var backupSize = _fileSystem.GetFileSize(backupPath);
            if (backupSize == primarySize
                && _fileSystem.ReadAllBytes(apiPath).AsSpan().SequenceEqual(_fileSystem.ReadAllBytes(backupPath)))
            {
                _logger.Warn("原版备份与当前 API 文件字节相同，疑似本身就是 loader：" + backupPath);
                return AccountUnavailable(backupPath, SteamProbeError.BackupNotOriginal, "备份与当前 API 文件字节相同");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("检查原版备份失败：" + backupPath + " / " + ex.Message);
            return AccountUnavailable(backupPath, SteamProbeError.Native, ex.Message);
        }

        var result = _probe.Probe(backupPath, appId);
        if (!result.Succeeded)
        {
            _logger.Warn("账号授权探测未成功（备份 " + backupPath + "）：" + result.Error);
            return new DlcAccountOwnership(
                MapState(result),
                backupPath,
                0,
                Array.Empty<SteamDlcOwnership>(),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                result.Error ?? SteamProbeError.Native,
                result.ErrorDetail);
        }

        if (result.Dlcs.Count > 0 && SameIds(result.Dlcs, primary.Dlcs))
        {
            _logger.Warn("原版备份返回了与 loader 相同的 DLC 表，疑似也是 loader：" + backupPath);
            return AccountUnavailable(backupPath, SteamProbeError.BackupNotOriginal, "备份返回了与 loader 相同的 DLC 表");
        }

        var (dlcs, ownedIds, _) = ApplyTrusted(result, manifest);
        _logger.Info(
            "账号授权探测（" + backupPath + "）：Steam 目录 " + dlcs.Count + " 个 DLC、"
            + ownedIds.Count + " 个已拥有");
        return new DlcAccountOwnership(
            SteamOwnershipState.Succeeded,
            backupPath,
            result.SteamId64,
            dlcs,
            ownedIds,
            null,
            result.ErrorDetail);
    }

    /// <summary>套用可信 <c>DisabledDLC</c> 升级；主报告与账号视图共用，避免两套口径。</summary>
    private static (IReadOnlyList<SteamDlcOwnership> Dlcs, HashSet<string> OwnedAppIds, IReadOnlySet<string> TrustedDisabledDlc) ApplyTrusted(
        SteamDllProbeResult result,
        AppManifest? manifest)
    {
        IReadOnlySet<string> trusted = result.Succeeded
            ? AppManifestLookup.TrustedDisabledDlc(manifest, result.SteamId64)
            : new HashSet<string>(StringComparer.Ordinal);

        var dlcs = new List<SteamDlcOwnership>(result.Dlcs.Count);
        var ownedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dlc in result.Dlcs)
        {
            var owned = dlc.IsOwned || trusted.Contains(dlc.AppId);
            dlcs.Add(owned == dlc.IsOwned ? dlc : dlc with { IsOwned = true });
            if (owned)
            {
                ownedIds.Add(dlc.AppId);
            }
        }

        return (dlcs, ownedIds, trusted);
    }

    private static SteamOwnershipState MapState(SteamDllProbeResult result) => result.Succeeded
        ? SteamOwnershipState.Succeeded
        : result.Error switch
        {
            SteamProbeError.SteamNotRunning => SteamOwnershipState.SteamNotRunning,
            SteamProbeError.NoDll or SteamProbeError.X86 or SteamProbeError.NotWindows => SteamOwnershipState.Unavailable,
            _ => SteamOwnershipState.InitFailed,
        };

    private static DlcAccountOwnership AccountUnavailable(string? apiFilePath, string errorKey, string? detail) =>
        new(
            SteamOwnershipState.Unavailable,
            apiFilePath,
            0,
            Array.Empty<SteamDlcOwnership>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            errorKey,
            detail);

    /// <summary>两份 DLC 列表的 appid 集合是否相同（用来识别「备份其实也是 loader」）。</summary>
    private static bool SameIds(IReadOnlyList<SteamDlcOwnership> left, IReadOnlyList<SteamDlcOwnership> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dlc in left)
        {
            ids.Add(dlc.AppId);
        }

        foreach (var dlc in right)
        {
            if (!ids.Contains(dlc.AppId))
            {
                return false;
            }
        }

        return true;
    }

    private SteamOwnershipReport CreateUnavailable(
        GamePatchReport patch,
        int? appId,
        string? apiPath,
        string errorKey,
        string? detail = null) =>
        new(
            SteamOwnershipState.Unavailable,
            patch.Status,
            appId,
            patch.ApiDirectory,
            patch.ApiFileName,
            apiPath,
            0,
            null,
            false,
            Array.Empty<SteamDlcOwnership>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.Ordinal),
            errorKey,
            detail);

    private GamePatchReport SafeInspect(GameInstallation game)
    {
        try
        {
            return _patchService.Inspect(game);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warn("检查补丁状态失败（授权判定降级）：" + ex.Message);
            return new GamePatchReport(
                GamePatchStatus.Unsupported,
                null,
                null,
                null,
                ex.Message,
                false,
                false,
                Array.Empty<string>());
        }
    }

    private static int? ResolveAppId(GameInstallation game, GamePatchReport patch)
    {
        if (game.SteamAppId is { } fromGame && fromGame > 0)
        {
            return fromGame;
        }

        if (GameSteamAppIds.TryGet(game.GameId, out var known) && known > 0)
        {
            return known;
        }

        if (patch.SteamAppId is { } fromPatch && fromPatch > 0)
        {
            return fromPatch;
        }

        return null;
    }

    /// <summary>
    /// 缓存键要覆盖一切会改变结论的输入：补丁状态变了（打/还原补丁）以及 <c>_o</c> 备份变了，
    /// 都必须立刻重新探测，不能被「文件大小没变」蒙过去。
    /// </summary>
    private string BuildCacheKey(string apiPath, long size, int appId, GamePatchReport patch)
    {
        var backup = patch.BackupApiFilePath;
        var backupSize = "-";
        if (backup is not null)
        {
            try
            {
                backupSize = _fileSystem.FileExists(backup)
                    ? _fileSystem.GetFileSize(backup).ToString(CultureInfo.InvariantCulture)
                    : "missing";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                backupSize = "error";
            }
        }

        return string.Join(
            "|",
            apiPath,
            size.ToString(CultureInfo.InvariantCulture),
            appId.ToString(CultureInfo.InvariantCulture),
            patch.Status.ToString(),
            backup ?? "-",
            backupSize);
    }

    private bool TryGetCached(string key, out SteamOwnershipReport report)
    {
        lock (_cacheGate)
        {
            if (_cacheReport is not null
                && string.Equals(_cacheKey, key, StringComparison.Ordinal)
                && DateTime.UtcNow - _cacheStampUtc < CacheLifetime)
            {
                report = _cacheReport;
                return true;
            }
        }

        report = null!;
        return false;
    }

    private void Store(string key, SteamOwnershipReport report)
    {
        lock (_cacheGate)
        {
            _cacheKey = key;
            _cacheReport = report;
            _cacheStampUtc = DateTime.UtcNow;
        }
    }
}
