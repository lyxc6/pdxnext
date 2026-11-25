namespace PdxLauncher.Core.Updates;

using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Online;

/// <summary>一次更新检查的结果（供 UI 决定是否弹提示）。</summary>
public enum UpdateCheckStatus
{
    Disabled,
    NotDue,
    UpToDate,
    Available,
    Skipped,
    Failed,
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    UpdateManifest? Manifest,
    string Message)
{
    public bool HasUpdate => Status == UpdateCheckStatus.Available && Manifest is not null;
}

/// <summary>下载进度；<see cref="Fraction"/> 在未知总长时为 null。</summary>
public sealed record UpdateProgress(long BytesReceived, long? TotalBytes)
{
    public double? Fraction => TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value : null;
}

/// <summary>启动安装器的结果。</summary>
public sealed record UpdateLaunchResult(bool Started, bool ElevationCanceled, string? Error);

/// <summary>
/// 检查 GitHub Releases 更新、下载并校验安装包、按安装模式启动安装器。
/// <para>
/// 这里不碰 UI：应用层负责弹浮层与退出；本类只做网络、文件与进程。
/// 每机器安装时用 ShellExecute 的 <c>runas</c> 触发 UAC，取消（1223）会原样返回给调用方，
/// 不会把已经退出的进程留在半空。
/// </para>
/// </summary>
public sealed class UpdateService
{
    /// <summary>默认公开清单地址；安装包与源码放在同一个公开仓库，走 Releases 静态资产。</summary>
    public static readonly Uri DefaultManifestUri =
        new("https://github.com/lyxc6/pdxnext/releases/latest/download/update.json");

    /// <summary>自动检查的节流窗口。</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    /// <summary>清单最大长度，防止异常响应撑爆内存。</summary>
    public const int MaxManifestBytes = 128 * 1024;

    /// <summary>改名尝试次数（首次 + 2 次重试）。</summary>
    public const int MoveAttempts = 3;

    /// <summary>改名失败后的重试间隔；杀毒 / 索引器通常只短暂占用刚写完的文件。</summary>
    public static readonly TimeSpan DefaultMoveRetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>安装包下载尝试次数（首次 + 2 次重试）。</summary>
    public const int DownloadAttempts = 3;

    /// <summary>下载失败后的首个退避间隔；第 2 次重试按 3 倍退避（500ms → 1500ms）。</summary>
    public static readonly TimeSpan DefaultDownloadRetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly IHttpFetcher _http;
    private readonly IDownloadFetcher? _downloads;
    private readonly IAppLogger _logger;
    private readonly AppSettings _settings;
    private readonly AppSettingsStore? _settingsStore;
    private readonly string _cacheDirectory;
    private readonly string _installDirectory;
    private readonly string _currentVersion;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly IUninstallEntrySource? _uninstallEntries;
    private InstallMode? _installMode;

    public UpdateService(
        IHttpFetcher http,
        IAppLogger logger,
        AppSettings settings,
        string cacheDirectory,
        string installDirectory,
        string currentVersion,
        AppSettingsStore? settingsStore = null,
        Func<DateTimeOffset>? utcNow = null,
        IUninstallEntrySource? uninstallEntries = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);

        _http = http;
        _downloads = http as IDownloadFetcher;
        _logger = logger;
        _settings = settings;
        _cacheDirectory = cacheDirectory;
        _installDirectory = installDirectory;
        _currentVersion = UpdateVersion.Parse(currentVersion).ToString();
        _settingsStore = settingsStore;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _uninstallEntries = uninstallEntries;
    }

    public string CurrentVersion => _currentVersion;

    /// <summary>改名重试间隔；自检把它设成 zero 以免拖慢。</summary>
    public TimeSpan MoveRetryDelay { get; init; } = DefaultMoveRetryDelay;

    /// <summary>下载重试间隔基数；自检把它设成 zero 以免拖慢。</summary>
    public TimeSpan DownloadRetryDelay { get; init; } = DefaultDownloadRetryDelay;

    /// <summary>构造时传入的设置实例；必须与 AppServices.Settings 是同一个对象。</summary>
    public AppSettings Settings => _settings;

    /// <summary>
    /// 本次更新会传 <c>/ALLUSERS</c>（需要提权）还是 <c>/CURRENTUSER</c>。
    /// 判据与设置页「安装模式」完全一致：同一份 marker、同一份卸载项、同一个程序目录。
    /// </summary>
    public bool WillUseAllUsers => ShouldUseAllUsers(CurrentInstallMode);

    /// <summary>当前目录的安装形态；首次访问时读一次 marker 与卸载项，之后复用。</summary>
    public InstallMode CurrentInstallMode => _installMode ??= ResolveInstallMode();

    /// <summary>
    /// 只有「Inno 正式安装 + 每机器 + App Path 命中当前目录」才用 <c>/ALLUSERS</c>。
    /// <para>
    /// 刻意不参考 <c>PdxDataRoot</c> 的旧「每机器安装」判据：它把「marker=perMachine」
    /// 和「Program Files 下不可写」都当成每机器证据，绿色拷贝进 Program Files、或卸载残留
    /// 都会被它判成已安装，于是更新时对便携副本传 <c>/ALLUSERS</c>，装到别处甚至覆盖别人的安装。
    /// 判据的唯一来源是 <see cref="InstallModeResolver"/>（与设置页同一结论）。
    /// </para>
    /// </summary>
    public static bool ShouldUseAllUsers(InstallMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        return mode.Kind == InstallModeKind.InstalledInno &&
               mode.Reason == InstallModeReason.MatchedInstallDir &&
               mode.Scope == InstallScope.PerMachine;
    }

    private InstallMode ResolveInstallMode()
    {
        try
        {
            var marker = PdxDataRoot.ReadInstallMode(_installDirectory);
            var entries = _uninstallEntries?.ReadEntries() ?? [];
            return InstallModeResolver.Resolve(_installDirectory, marker, entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // 读注册表/磁盘失败只等于「没有证据」：绝不能因此把便携副本按每机器去装。
            _logger.Warn("判定更新安装模式失败，按便携处理: " + ex.Message);
            return InstallMode.PortableRun(InstallModeReason.NoEntry);
        }
    }

    /// <summary>从程序集信息版本读取当前版本；失败时退回 1.0.0。</summary>
    public static string ReadCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        var informational = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (UpdateVersion.TryParse(informational, out var version))
        {
            return version.ToString();
        }

        var fileVersion = assembly?.GetName().Version;
        return fileVersion is null
            ? "1.0.0"
            : new UpdateVersion(fileVersion.Major, fileVersion.Minor, Math.Max(0, fileVersion.Build)).ToString();
    }

    /// <summary>
    /// 检查更新。自动检查受 <see cref="AppSettings.AutoCheckUpdates"/> 与 24 小时节流约束；
    /// 手动检查总是访问网络。成功读到清单后会写回 LastUpdateCheckUtc。
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(
        bool manual,
        CancellationToken cancellationToken,
        Uri? manifestUri = null)
    {
        if (!manual && !_settings.AutoCheckUpdates)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Disabled, null, "自动检查更新已关闭");
        }

        var now = _utcNow();
        if (!manual &&
            _settings.LastUpdateCheckUtc is { } last &&
            now - last < CheckInterval &&
            now >= last)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NotDue, null, "距离上次检查不足 24 小时");
        }

        try
        {
            var uri = manifestUri ?? DefaultManifestUri;
            var json = await ReadManifestTextAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!UpdateManifest.TryParse(json, out var manifest, out var error) || manifest is null)
            {
                _logger.Warn("更新清单无效: " + error);
                return new UpdateCheckResult(UpdateCheckStatus.Failed, null, error ?? "更新清单无效");
            }

            _settings.LastUpdateCheckUtc = now;
            Persist();

            var current = UpdateVersion.Parse(_currentVersion);
            var next = UpdateVersion.Parse(manifest.Version);
            if (next.CompareTo(current) <= 0)
            {
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null, "当前已是最新版本 " + current);
            }

            if (!manual &&
                !manifest.Mandatory &&
                string.Equals(_settings.SkippedUpdateVersion, manifest.Version, StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateCheckResult(UpdateCheckStatus.Skipped, manifest, "已跳过版本 " + manifest.Version);
            }

            return new UpdateCheckResult(UpdateCheckStatus.Available, manifest, "发现新版本 " + manifest.Version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is HttpRequestException
                or IOException
                or InvalidDataException
                or UnauthorizedAccessException
                or System.Net.Sockets.SocketException
                or TaskCanceledException)
        {
            // TaskCanceledException 也要兜住：连接超时就是它抛出来的（内含 TimeoutException），
            // 不接住的话会变成「未观察的 Task 异常」，界面上只剩一片空白。
            _logger.Warn("检查更新失败: " + ex.Message);
            return new UpdateCheckResult(UpdateCheckStatus.Failed, null, "检查更新失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 下载并校验安装包。已存在且校验通过的文件直接复用；
    /// 上次改名被占用时留下的完整 <c>.part</c> 也会复用，不必重下整包。
    /// </summary>
    public async Task<string> DownloadAsync(
        UpdateManifest manifest,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Directory.CreateDirectory(_cacheDirectory);

        var fileName = "PdxNext-Setup-" + manifest.Version + ".exe";
        var finalPath = Path.Combine(_cacheDirectory, fileName);
        if (File.Exists(finalPath) && await VerifyAsync(finalPath, manifest, cancellationToken).ConfigureAwait(false))
        {
            progress?.Report(new UpdateProgress(manifest.Size, manifest.Size));
            return finalPath;
        }

        var tempPath = finalPath + ".part";

        // 上次改名时被占用：.part 里已是校验通过的完整包，直接落位。
        if (File.Exists(tempPath) && await VerifyAsync(tempPath, manifest, cancellationToken).ConfigureAwait(false))
        {
            await MoveVerifiedPackageAsync(tempPath, finalPath, cancellationToken).ConfigureAwait(false);
            progress?.Report(new UpdateProgress(manifest.Size, manifest.Size));
            _logger.Info("复用已校验的更新安装包: " + finalPath);
            return finalPath;
        }

        TryDelete(tempPath);
        // 用户已经取消时不要再多发一次网络请求。
        cancellationToken.ThrowIfCancellationRequested();
        var downloads = _downloads
            ?? throw new InvalidOperationException("当前网络层不支持文件下载");

        var verified = false;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // 更新包不做续传：每次尝试都整份重下，失败后删掉 .part。
                var download = await downloads
                    .GetDownloadAsync(manifest.SetupUri, startOffset: null, cancellationToken)
                    .ConfigureAwait(false);
                using var content = download.Content;
                var total = download.ContentLength ?? manifest.Size;

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long received = 0;
                var lastReported = 0L;
                await using (var output = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true))
                {
                    int read;
                    while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        hash.AppendData(buffer, 0, read);
                        received += read;

                        if (received - lastReported >= 256 * 1024)
                        {
                            lastReported = received;
                            progress?.Report(new UpdateProgress(received, total));
                        }
                    }

                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                // 写句柄必须在这里已经关闭：Windows 改名要求源文件句柄共享 Delete，
                // FileShare.None 下改名必然报 sharing violation（也就是「文件被另一进程占用」）。
                if (received != manifest.Size)
                {
                    throw new InvalidDataException("安装包大小不符：期望 " + manifest.Size + "，实际 " + received);
                }

                var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(actual, manifest.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("安装包 SHA-256 不符");
                }

                verified = true;
                await MoveVerifiedPackageAsync(tempPath, finalPath, cancellationToken).ConfigureAwait(false);
                progress?.Report(new UpdateProgress(received, total));
                _logger.Info("更新安装包已下载并校验: " + finalPath);
                return finalPath;
            }
            catch (Exception ex) when (ex is not SteamThrottleException
                && ex is not InvalidDataException
                && ex is not UpdatePackageLockedException
                && !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                // 已校验完整的包不能删：改名被占用时下次可直接复用，避免重下几十 MB。
                if (!verified)
                {
                    TryDelete(tempPath);
                }

                if (attempt >= DownloadAttempts)
                {
                    _logger.Warn("更新包下载失败（已重试 " + (DownloadAttempts - 1) + " 次）：" + ex.Message);
                    throw;
                }

                // 传输中途失败也重试；本轮进度作废，下一轮从 0 重新报。
                progress?.Report(new UpdateProgress(0, manifest.Size));
                _logger.Warn(
                    "更新包下载重试 " + attempt + "/" + (DownloadAttempts - 1) + "：" + ex.Message);
                await Task.Delay(DownloadRetryDelayFor(attempt), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 限流 / 校验不符 / 改名被占用 / 用户取消：不重试，保留原语义。
                if (!verified)
                {
                    TryDelete(tempPath);
                }

                throw;
            }
        }
    }

    /// <summary>第 n 次失败后的等待时间：500ms、1500ms（3 倍退避）。</summary>
    private TimeSpan DownloadRetryDelayFor(int attempt) =>
        attempt <= 1
            ? DownloadRetryDelay
            : TimeSpan.FromMilliseconds(DownloadRetryDelay.TotalMilliseconds * 3);

    /// <summary>
    /// 把已校验的 <c>.part</c> 改名为正式安装包。Windows 上杀毒 / 索引器可能在文件刚写完时
    /// 短暂持有句柄：锁住源文件时 <see cref="File.Move(string,string,bool)"/> 抛 <see cref="IOException"/>，
    /// 锁住目标文件时抛 <see cref="UnauthorizedAccessException"/>，两种都要重试。
    /// 失败按 <see cref="MoveAttempts"/> 次 × <see cref="MoveRetryDelay"/>；仍失败时抛
    /// <see cref="UpdatePackageLockedException"/>，调用方应保留 <c>.part</c>。
    /// </summary>
    private async Task MoveVerifiedPackageAsync(string tempPath, string finalPath, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tempPath, finalPath, overwrite: true);
                if (attempt > 1)
                {
                    _logger.Info("更新安装包改名在第 " + attempt + " 次尝试成功: " + finalPath);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < MoveAttempts)
                {
                    _logger.Warn("更新安装包改名被占用，第 " + attempt + "/" + MoveAttempts + " 次尝试失败: " + ex.Message);
                    await Task.Delay(MoveRetryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _logger.Error("更新安装包改名失败，已保留 .part: " + tempPath, ex);
                throw new UpdatePackageLockedException(tempPath, finalPath, ex);
            }
        }
    }

    /// <summary>
    /// 启动安装器。每机器安装必须提权：失败/取消 UAC 返回 ElevationCanceled，调用方不应退出应用。
    /// </summary>
    public UpdateLaunchResult LaunchInstaller(string setupPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(setupPath) || !File.Exists(setupPath))
        {
            return new UpdateLaunchResult(false, false, "安装包不存在");
        }

        var useAllUsers = WillUseAllUsers;
        var arguments = "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART " +
                        (useAllUsers ? "/ALLUSERS" : "/CURRENTUSER");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(setupPath) ?? _cacheDirectory,
                UseShellExecute = useAllUsers,
                Verb = useAllUsers ? "runas" : string.Empty,
                CreateNoWindow = false,
            };

            var process = Process.Start(startInfo);
            if (process is null)
            {
                return new UpdateLaunchResult(false, false, "无法启动安装器");
            }

            _logger.Info("已启动更新安装器（" + (useAllUsers ? "每机器/UAC" : "每用户") + "）：" + setupPath);
            return new UpdateLaunchResult(true, false, null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _logger.Info("用户取消了更新提权");
            return new UpdateLaunchResult(false, true, "已取消管理员授权");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _logger.Warn("启动更新安装器失败: " + ex.Message);
            return new UpdateLaunchResult(false, false, ex.Message);
        }
    }

    /// <summary>校验已有安装包的大小与 SHA-256。</summary>
    public async Task<bool> VerifyAsync(string path, UpdateManifest manifest, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != manifest.Size)
        {
            return false;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash).ToLowerInvariant(), manifest.Sha256, StringComparison.Ordinal);
    }

    private async Task<string> ReadManifestTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        await using var stream = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 8192, leaveOpen: false);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (text.Length > MaxManifestBytes)
        {
            throw new InvalidDataException("更新清单超过 " + MaxManifestBytes + " 字节");
        }

        return text;
    }

    private void Persist()
    {
        if (_settingsStore is null)
        {
            return;
        }

        var result = _settingsStore.Save(_settings);
        if (!result.IsSuccess)
        {
            _logger.Warn("保存更新检查时间失败: " + result.ErrorMessage);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 下次启动会覆盖 .part。
        }
    }
}
