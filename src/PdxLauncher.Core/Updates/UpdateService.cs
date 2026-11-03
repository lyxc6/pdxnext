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

    private readonly HttpFetcher _http;
    private readonly IAppLogger _logger;
    private readonly AppSettings _settings;
    private readonly AppSettingsStore? _settingsStore;
    private readonly string _cacheDirectory;
    private readonly string _installDirectory;
    private readonly string _currentVersion;
    private readonly Func<DateTimeOffset> _utcNow;

    public UpdateService(
        HttpFetcher http,
        IAppLogger logger,
        AppSettings settings,
        string cacheDirectory,
        string installDirectory,
        string currentVersion,
        AppSettingsStore? settingsStore = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);

        _http = http;
        _logger = logger;
        _settings = settings;
        _cacheDirectory = cacheDirectory;
        _installDirectory = installDirectory;
        _currentVersion = UpdateVersion.Parse(currentVersion).ToString();
        _settingsStore = settingsStore;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string CurrentVersion => _currentVersion;

    /// <summary>构造时传入的设置实例；必须与 AppServices.Settings 是同一个对象。</summary>
    public AppSettings Settings => _settings;

    public bool IsPerMachineInstall => PdxDataRoot.IsPerMachineInstall(_installDirectory);

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
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or System.Net.Sockets.SocketException)
        {
            _logger.Warn("检查更新失败: " + ex.Message);
            return new UpdateCheckResult(UpdateCheckStatus.Failed, null, "检查更新失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 下载并校验安装包。已存在且校验通过的文件直接复用；下载失败不会留下半成品。
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
        TryDelete(tempPath);

        try
        {
            var download = await _http.GetDownloadAsync(manifest.SetupUri, cancellationToken).ConfigureAwait(false);
            using var content = download.Content;
            var total = download.ContentLength ?? manifest.Size;
            await using var output = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long received = 0;
            var lastReported = 0L;
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

            if (received != manifest.Size)
            {
                throw new InvalidDataException("安装包大小不符：期望 " + manifest.Size + "，实际 " + received);
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, manifest.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("安装包 SHA-256 不符");
            }

            File.Move(tempPath, finalPath, overwrite: true);
            progress?.Report(new UpdateProgress(received, total));
            _logger.Info("更新安装包已下载并校验: " + finalPath);
            return finalPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
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

        var perMachine = IsPerMachineInstall;
        var arguments = "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART " +
                        (perMachine ? "/ALLUSERS" : "/CURRENTUSER");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(setupPath) ?? _cacheDirectory,
                UseShellExecute = perMachine,
                Verb = perMachine ? "runas" : string.Empty,
                CreateNoWindow = false,
            };

            var process = Process.Start(startInfo);
            if (process is null)
            {
                return new UpdateLaunchResult(false, false, "无法启动安装器");
            }

            _logger.Info("已启动更新安装器（" + (perMachine ? "每机器/UAC" : "每用户") + "）：" + setupPath);
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
