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
        if (TryGetCached(apiPath, size, appId.Value, out var cached))
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
        Store(apiPath, size, appId.Value, report);
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

        var trusted = result.Succeeded
            ? AppManifestLookup.TrustedDisabledDlc(manifest, steamId)
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

        var state = result.Succeeded
            ? SteamOwnershipState.Succeeded
            : result.Error switch
            {
                SteamProbeError.SteamNotRunning => SteamOwnershipState.SteamNotRunning,
                SteamProbeError.NoDll or SteamProbeError.X86 or SteamProbeError.NotWindows => SteamOwnershipState.Unavailable,
                _ => SteamOwnershipState.InitFailed,
            };

        if (!result.Succeeded)
        {
            _logger.Warn("Steam 授权探测未成功: " + result.Error + " " + (result.ErrorDetail ?? string.Empty));
        }

        return new SteamOwnershipReport(
            state,
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
            result.ErrorDetail);
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

    private bool TryGetCached(string apiPath, long size, int appId, out SteamOwnershipReport report)
    {
        var key = apiPath + "|" + size.ToString(CultureInfo.InvariantCulture) + "|" + appId.ToString(CultureInfo.InvariantCulture);
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

    private void Store(string apiPath, long size, int appId, SteamOwnershipReport report)
    {
        var key = apiPath + "|" + size.ToString(CultureInfo.InvariantCulture) + "|" + appId.ToString(CultureInfo.InvariantCulture);
        lock (_cacheGate)
        {
            _cacheKey = key;
            _cacheReport = report;
            _cacheStampUtc = DateTime.UtcNow;
        }
    }
}
