namespace PdxLauncher.App.Services;

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Steam;

/// <summary>
/// 通过 Steamworks 原生 flat API 真正订阅 / 取消订阅 Steam 创意工坊条目。
/// <para>
/// 前提（缺一不可）：本机 Steam 客户端正在运行且已登录、用户拥有目标 appid 的游戏、
/// 能找到 <c>steam_api64.dll</c>、用户在设置里打开了实验开关。
/// 任一不满足时 <see cref="IsAvailable"/> 为 false，由视图模型回退到"在 Steam 中订阅"深链。
/// </para>
/// <para>
/// <b>为什么必须有 steam_appid.txt</b>：<c>ISteamUGC::SubscribeItem</c> 没有 appid 参数，
/// 作用于"进程初始化时的 appid"。启动器自己不在 Steam 上，只能以目标游戏的 appid 初始化；
/// 该文件是 Valve 标注的**开发用途**手段，属灰色用法，因此本功能默认关闭。
/// </para>
/// </summary>
public sealed class SteamWorkshopBridge : IWorkshopSubscribeBackend, IDisposable
{
    /// <summary>appid 文件名（Steam 从工作目录读取）。</summary>
    private const string AppIdFileName = "steam_appid.txt";

    /// <summary>appid 环境变量名（DLL 在找不到文件时的第二条路）。</summary>
    private const string AppIdEnvironmentVariable = "SteamAppId";

    /// <summary><c>SteamErrMsg</c> 的容量（<c>k_cchMaxSteamErrMsg</c>）。</summary>
    private const int ErrorMessageLength = 1024;

    /// <summary><c>k_EItemStateSubscribed</c>。</summary>
    private const uint StateSubscribed = 1u << 0;

    /// <summary><c>k_EItemStateInstalled</c>。</summary>
    private const uint StateInstalled = 1u << 2;

    /// <summary><c>k_EItemStateDownloading</c>。</summary>
    private const uint StateDownloading = 1u << 4;

    /// <summary><c>k_EItemStateDownloadPending</c>。</summary>
    private const uint StateDownloadPending = 1u << 5;

    /// <summary>轮询间隔。</summary>
    private const int PollIntervalMilliseconds = 500;

    /// <summary>轮询次数上限（合计约 20 秒）。</summary>
    private const int PollAttempts = 40;

    /// <summary>回调泵间隔。</summary>
    private const int PumpIntervalMilliseconds = 100;

    private static readonly object ResolverGate = new();
    private static bool _resolverInstalled;
    private static string? _resolvedDllPath;

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly SteamApiLocator _locator;
    private readonly string _appDirectory;

    private DispatcherQueue? _dispatcher;
    private DispatcherQueueTimer? _pumpTimer;
    private IntPtr _ugc;
    private bool _initialized;
    private bool _disposed;
    private int _appId;
    private string? _dllPath;
    private string? _unavailableReason;

    /// <summary>构造。</summary>
    public SteamWorkshopBridge(IFileSystem fileSystem, IAppLogger logger, SteamApiLocator locator)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _appDirectory = AppContext.BaseDirectory;
    }

    /// <inheritdoc />
    public bool IsAvailable => _initialized && !_disposed;

    /// <inheritdoc />
    public string? UnavailableReason => IsAvailable ? null : _unavailableReason;

    /// <summary>已加载的原生库路径（用于设置页显示）。</summary>
    public string? DllPath => _dllPath;

    /// <summary>
    /// 以指定 appid 初始化。同一进程只能以一个 appid 初始化，appid 变化时会先
    /// <c>Shutdown</c> 再重新初始化。
    /// </summary>
    /// <param name="appId">目标游戏的 Steam appid。</param>
    /// <param name="settingsPath">设置里手填的 dll 路径（可空）。</param>
    /// <param name="game">当前游戏（用于在安装目录里找 dll），可空。</param>
    /// <param name="error">失败原因（中文，可直接展示）。</param>
    public bool TryInitialize(int appId, string? settingsPath, GameInstallation? game, out string? error)
    {
        error = null;
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized && _appId == appId)
        {
            return true;
        }

        if (_initialized)
        {
            // 切游戏：Steamworks 一个进程只能绑一个 appid。
            ShutdownCore(keepReason: true);
        }

        if (appId <= 0)
        {
            return SetUnavailable("当前游戏没有 Steam appid，无法直连 Steam 创意工坊", out error);
        }

        var path = _locator.Resolve(settingsPath, _appDirectory, game);
        if (path is null)
        {
            return SetUnavailable("未找到 steam_api64.dll（可在设置里手动指定路径）", out error);
        }

        if (!_locator.LooksUsable(path, out var sizeReason))
        {
            return SetUnavailable("steam_api64.dll 不可用：" + sizeReason, out error);
        }

        try
        {
            EnsureDllResolver(path);
        }
        catch (Exception ex)
        {
            _logger.Error("加载 steam_api64.dll 失败：" + path, ex);
            return SetUnavailable("加载 steam_api64.dll 失败：" + ex.Message, out error);
        }

        WriteAppId(appId);

        try
        {
            var buffer = new byte[ErrorMessageLength];
            var result = SteamNativeApi.InitFlat(buffer);
            if (result != 0)
            {
                _dllPath = path;
                return SetUnavailable(DescribeInitFailure(result, buffer), out error);
            }

            _ugc = SteamNativeApi.SteamUgc();
            if (_ugc == IntPtr.Zero)
            {
                SteamNativeApi.Shutdown();
                _dllPath = path;
                return SetUnavailable("SteamUGC 接口不可用（steam_api64.dll 版本过旧）", out error);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Steamworks 初始化异常（appid=" + appId + "）", ex);
            return SetUnavailable("Steamworks 初始化异常：" + ex.Message, out error);
        }

        _initialized = true;
        _appId = appId;
        _dllPath = path;
        _unavailableReason = null;
        StartPump();
        _logger.Info("Steamworks 已初始化：appid=" + appId + "，dll=" + path);
        return true;

    }

    /// <inheritdoc />
    public async Task<WorkshopSubscribeResult> SetSubscribedAsync(
        ulong publishedFileId,
        bool subscribe,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable || _ugc == IntPtr.Zero)
        {
            return WorkshopSubscribeResult.Fail(_unavailableReason ?? "Steam 直连订阅不可用");
        }

        try
        {
            if (!SteamNativeApi.IsSteamRunning())
            {
                return WorkshopSubscribeResult.Fail("Steam 客户端未运行");
            }

            if (subscribe)
            {
                SteamNativeApi.SubscribeItem(_ugc, publishedFileId);
            }
            else
            {
                SteamNativeApi.UnsubscribeItem(_ugc, publishedFileId);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("调用 Steam 订阅接口失败（file=" + publishedFileId + ", subscribe=" + subscribe + "）", ex);
            return WorkshopSubscribeResult.Fail("调用 Steam 接口失败：" + ex.Message);
        }

        _logger.Info((subscribe ? "已请求 Steam 订阅：" : "已请求 Steam 取消订阅：") + publishedFileId);

        // 不用 CallResult：省掉结构体布局与回调注册，直接轮询 GetItemState 判定是否生效。
        for (var attempt = 0; attempt < PollAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(PollIntervalMilliseconds, cancellationToken).ConfigureAwait(true);

            var state = ReadState(publishedFileId);
            if (state.IsSubscribed == subscribe)
            {
                return WorkshopSubscribeResult.Success;
            }
        }

        return WorkshopSubscribeResult.Fail(
            subscribe ? "Steam 未在 20 秒内确认订阅" : "Steam 未在 20 秒内确认取消订阅");
    }

    /// <inheritdoc />
    public WorkshopInstallState ReadState(ulong publishedFileId)
    {
        if (!IsAvailable || _ugc == IntPtr.Zero)
        {
            return WorkshopInstallState.Unknown;
        }

        try
        {
            var state = SteamNativeApi.GetItemState(_ugc, publishedFileId);
            return new WorkshopInstallState(
                IsSubscribed: (state & StateSubscribed) != 0,
                IsInstalled: (state & StateInstalled) != 0,
                IsDownloading: (state & (StateDownloading | StateDownloadPending)) != 0,
                AcfPath: null);
        }
        catch (Exception ex)
        {
            _logger.Warn("读取 Steam 条目状态失败（file=" + publishedFileId + "）：" + ex.Message);
            return WorkshopInstallState.Unknown;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ShutdownCore(keepReason: true);
    }

    /// <summary>
    /// 关闭 Steamworks（退出时调用）。保留 <see cref="UnavailableReason"/>，避免退出路径上反复记日志。
    /// </summary>
    public void Shutdown()
    {
        if (_disposed)
        {
            return;
        }

        ShutdownCore(keepReason: true);
    }

    private void ShutdownCore(bool keepReason)
    {
        StopPump();

        if (_initialized)
        {
            try
            {
                SteamNativeApi.Shutdown();
            }
            catch (Exception ex)
            {
                _logger.Warn("SteamAPI_Shutdown 异常：" + ex.Message);
            }
        }

        _initialized = false;
        _ugc = IntPtr.Zero;
        _appId = 0;
        if (!keepReason)
        {
            _unavailableReason = null;
        }
    }

    private void StartPump()
    {
        _dispatcher ??= DispatcherQueue.GetForCurrentThread();
        if (_dispatcher is null || _pumpTimer is not null)
        {
            return;
        }

        _pumpTimer = _dispatcher.CreateTimer();
        _pumpTimer.Interval = TimeSpan.FromMilliseconds(PumpIntervalMilliseconds);
        _pumpTimer.IsRepeating = true;
        _pumpTimer.Tick += OnPumpTick;
        _pumpTimer.Start();
    }

    private void StopPump()
    {
        if (_pumpTimer is null)
        {
            return;
        }

        _pumpTimer.Tick -= OnPumpTick;
        _pumpTimer.Stop();
        _pumpTimer = null;
    }

    private void OnPumpTick(DispatcherQueueTimer sender, object args)
    {
        if (!_initialized)
        {
            return;
        }

        try
        {
            SteamNativeApi.RunCallbacks();
        }
        catch (Exception ex)
        {
            _logger.Warn("SteamAPI_RunCallbacks 异常：" + ex.Message);
        }
    }

    /// <summary>把 appid 写到 exe 同目录的 steam_appid.txt，并设同名环境变量。</summary>
    private void WriteAppId(int appId)
    {
        var text = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Environment.SetEnvironmentVariable(AppIdEnvironmentVariable, text);

        try
        {
            var path = _fileSystem.Combine(_appDirectory, AppIdFileName);
            if (_fileSystem.FileExists(path) &&
                string.Equals(_fileSystem.ReadAllText(path).Trim(), text, StringComparison.Ordinal))
            {
                return;
            }

            _fileSystem.WriteAllTextAtomic(path, text);
            _logger.Info("已写入 steam_appid.txt：" + text);
        }
        catch (Exception ex)
        {
            // 写失败不代表初始化一定失败（环境变量那条路还在），但要让用户能看出问题。
            _logger.Warn("写入 steam_appid.txt 失败：" + ex.Message);
        }
    }

    /// <summary>标记为不可用并返回 false；<paramref name="error"/> 供调用方展示。</summary>
    private bool SetUnavailable(string reason, out string? error)
    {
        _initialized = false;
        _ugc = IntPtr.Zero;
        _unavailableReason = reason;
        _logger.Warn("Steam 直连订阅不可用：" + reason);
        error = reason;
        return false;
    }

    /// <summary>把 <c>ESteamAPIInitResult</c> 翻译成中文原因。</summary>
    private static string DescribeInitFailure(int result, byte[] buffer)
    {
        var detail = DecodeErrorMessage(buffer);
        return result switch
        {
            2 => "Steam 客户端未运行或未登录",
            3 => "Steam 客户端版本过旧，请更新后重试",
            _ => string.IsNullOrWhiteSpace(detail) ? "Steamworks 初始化失败" : "Steamworks 初始化失败：" + detail,
        };
    }

    /// <summary>读 <c>SteamErrMsg</c>（UTF-8，NUL 结尾）。</summary>
    private static string? DecodeErrorMessage(byte[] buffer)
    {
        var length = Array.IndexOf(buffer, (byte)0);
        if (length <= 0)
        {
            return null;
        }

        return Encoding.UTF8.GetString(buffer, 0, length).Trim();
    }

    /// <summary>
    /// 让 <c>DllImport("steam_api64")</c> 解析到指定绝对路径。整个进程只能设置一次解析器，
    /// 之后的路径变更只在下次解析时生效（因此改设置后建议重启程序）。
    /// </summary>
    private static void EnsureDllResolver(string dllPath)
    {
        lock (ResolverGate)
        {
            _resolvedDllPath = dllPath;
            if (_resolverInstalled)
            {
                return;
            }

            try
            {
                NativeLibrary.SetDllImportResolver(typeof(SteamNativeApi).Assembly, ResolveLibrary);
            }
            catch (InvalidOperationException)
            {
                // 已被其它模块设置过；仍继续，交给默认解析。
            }

            _resolverInstalled = true;
        }
    }

    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, SteamNativeApi.LibraryName, StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        var path = _resolvedDllPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return IntPtr.Zero;
        }

        return NativeLibrary.TryLoad(path!, out var handle) ? handle : IntPtr.Zero;
    }
}
