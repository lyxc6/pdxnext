namespace PdxLauncher.Core.Steam;

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// 在**短命子进程里**读一次某个 appid 的成就列表（含当前账号的解锁状态）。
/// <para>
/// 做法与 <see cref="SteamWorkshopHostOperation"/> 一致：每次进程只加载命令行指定的那一份
/// <c>steam_api64.dll</c>，用完即 <c>Shutdown</c> + <c>Free</c>，进程退出后 Steam 那边的
/// game process 登记自然消失。主启动器进程里一次都不许调 <c>Init</c>。
/// </para>
/// <para>
/// 成就数据必须先 <c>RequestUserStats</c> 再收 <c>UserStatsReceived_t</c>（callback 1101）；
/// 没等到这条回调之前读到的解锁状态是不可信的，所以这里带超时轮询，超时按
/// <see cref="SteamAchievementCodes.Timeout"/> 返回，绝不让宿主卡死。
/// </para>
/// </summary>
internal sealed class SteamAchievementsHostOperation : IDisposable
{
    private const string AppIdVariable = "SteamAppId";
    private const int InitFlatBufferLength = 1024;
    private const int ResultOk = 1;

    /// <summary><c>UserStatsReceived_t::k_iCallback</c>（sdk/public/steam/isteamuserstats.h:327）。</summary>
    private const int UserStatsReceivedId = 1101;

    /// <summary><c>GlobalAchievementPercentagesReady_t::k_iCallback</c>（isteamuserstats.h:444）。</summary>
    private const int GlobalPercentagesReadyId = 1110;

    /// <summary>拿到 UserStats 后给 callback 1110 的收尾时间；不改变「拿不到就不显示」的语义。</summary>
    private static readonly TimeSpan GlobalPercentagesGrace = TimeSpan.FromSeconds(2);

    /// <summary>防御性上限：正常游戏最多几千个成就，超过就只读这么多，避免坏 DLL 让我们死循环。</summary>
    internal const int MaxAchievements = 4096;

    /// <summary>轮询回调的间隔（与工坊发布同口径）。</summary>
    private const int CallbackPumpSleepMs = 50;

    private readonly string _apiFilePath;
    private readonly int _appId;

    private IntPtr _library;
    private bool _initialized;
    private string? _previousAppId;
    private IntPtr _userStats;
    private IntPtr _user;
    private int _pipe;
    private bool _manualInitialized;
    private UserStatsReceived? _statsReceived;
    private bool _percentagesReady;
    private bool _percentagesRequested;

    private ShutdownFn? _shutdown;
    private RunCallbacksFn? _runCallbacks;
    private IsSteamRunningFn? _isSteamRunning;
    private SteamUserStatsFn? _steamUserStats;
    private SteamUserFn? _steamUser;
    private GetSteamIdFn? _getSteamId;
    private RequestUserStatsFn? _requestUserStats;
    private GetNumAchievementsFn? _getNumAchievements;
    private GetAchievementNameFn? _getAchievementName;
    private GetAchievementDisplayAttributeFn? _getAchievementDisplayAttribute;
    private GetAchievementAchievedPercentFn? _getAchievementAchievedPercent;
    private RequestGlobalPercentagesFn? _requestGlobalPercentages;
    private GetAchievementAndUnlockTimeFn? _getAchievementAndUnlockTime;
    private GetHSteamPipeFn? _getHSteamPipe;
    private ManualDispatchInitFn? _manualDispatchInit;
    private ManualDispatchRunFrameFn? _manualDispatchRunFrame;
    private ManualDispatchGetNextCallbackFn? _manualDispatchGetNextCallback;
    private ManualDispatchFreeLastCallbackFn? _manualDispatchFreeLastCallback;

    /// <summary>构造。</summary>
    /// <param name="apiFilePath">本进程要加载的 <c>steam_api64.dll</c> 绝对路径。</param>
    /// <param name="appId">游戏的 Steam AppID。</param>
    public SteamAchievementsHostOperation(string apiFilePath, int appId)
    {
        _apiFilePath = apiFilePath;
        _appId = appId;
    }

    /// <summary>自检用：<c>UserStatsReceived_t</c> 的托管尺寸（x64 期望 24）。</summary>
    internal static int UserStatsReceivedSize => Marshal.SizeOf<UserStatsReceived>();

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (_initialized)
            {
                _shutdown?.Invoke();
            }
        }
        catch
        {
            // 宿主马上退出，Shutdown 的异常没有意义。
        }
        finally
        {
            _initialized = false;
            if (_library != IntPtr.Zero)
            {
                try
                {
                    NativeLibrary.Free(_library);
                }
                catch
                {
                    // 同上：进程即将结束。
                }

                _library = IntPtr.Zero;
            }

            if (_previousAppId is not null)
            {
                Environment.SetEnvironmentVariable(AppIdVariable, _previousAppId);
            }
        }
    }

    /// <summary>
    /// 读一次成就列表。任何失败都以结果码返回，不抛异常（宿主只关心结果行）。
    /// </summary>
    /// <param name="timeout">等 <c>UserStatsReceived_t</c> 的上限。</param>
    public SteamHostAchievementsResult ReadAchievements(TimeSpan timeout)
    {
        if (!File.Exists(_apiFilePath))
        {
            return SteamHostAchievementsResult.Fail(
                SteamAchievementCodes.NoDll,
                "找不到 steam_api64.dll：" + _apiFilePath);
        }

        var initCode = EnsureInitialized();
        if (initCode is not null)
        {
            return SteamHostAchievementsResult.Fail(initCode.Value.Code, initCode.Value.Error, initCode.Value.Detail);
        }

        if (_userStats == IntPtr.Zero || _user == IntPtr.Zero || _getSteamId is null)
        {
            return SteamHostAchievementsResult.Fail(
                SteamAchievementCodes.Unsupported,
                "steam_api64.dll 缺少成就读取所需的 Steamworks 导出（UserStats/User）",
                initialized: true);
        }

        if (!EnsureAchievementBindings())
        {
            return SteamHostAchievementsResult.Fail(
                SteamAchievementCodes.Unsupported,
                "steam_api64.dll 缺少成就读取所需的 Steamworks 导出（UserStats 查询）",
                initialized: true);
        }

        ulong steamId;
        try
        {
            steamId = _getSteamId(_user);
        }
        catch (Exception ex)
        {
            return SteamHostAchievementsResult.Fail(
                SteamAchievementCodes.NoSteamUser,
                "读取当前 SteamID 失败：" + ex.Message,
                initialized: true);
        }

        if (steamId == 0)
        {
            return SteamHostAchievementsResult.Fail(
                SteamAchievementCodes.NoSteamUser,
                "没有取到当前账号的 SteamID（Steam 未登录？）",
                initialized: true);
        }

        uint count;
        try
        {
            count = _getNumAchievements!(_userStats);
        }
        catch (Exception ex)
        {
            return SteamHostAchievementsResult.Fail(
                SteamAchievementCodes.Unsupported,
                "读取成就数量失败：" + ex.Message,
                initialized: true);
        }

        if (count == 0)
        {
            // 这不是失败：Init 与查询都成功了，只是这款游戏没有成就。
            return SteamHostAchievementsResult.None(initialized: true);
        }

        // 先发全球解锁率请求（异步），它和下面等 UserStats 的回调共用同一个泵。
        var waitError = RequestUserStats(steamId, timeout);
        if (waitError is not null)
        {
            return waitError;
        }

        WaitGlobalPercentages(GlobalPercentagesGrace);

        try
        {
            return SteamHostAchievementsResult.Success(ReadItems(count));
        }
        catch (Exception ex)
        {
            return SteamHostAchievementsResult.Fail(
                SteamAchievementCodes.Unsupported,
                "读取成就条目失败：" + ex.GetType().Name + "：" + ex.Message,
                initialized: true);
        }
    }

    private IReadOnlyList<SteamHostAchievementItem> ReadItems(uint count)
    {
        var total = (int)Math.Min(count, (uint)MaxAchievements);
        var items = new List<SteamHostAchievementItem>(total);
        for (var index = 0u; index < total; index++)
        {
            var namePtr = _getAchievementName!(_userStats, index);
            var apiName = namePtr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(namePtr);
            if (string.IsNullOrWhiteSpace(apiName))
            {
                // 名字读不到就没法查状态，跳过而不是造假条目。
                continue;
            }

            var displayName = ReadAttribute(apiName, "name") ?? apiName;
            var description = ReadAttribute(apiName, "desc") ?? string.Empty;
            var hidden = ReadAttribute(apiName, "hidden") == "1";

            bool achieved;
            uint unlockTime;
            try
            {
                var read = _getAchievementAndUnlockTime!(_userStats, apiName, out var ok, out unlockTime);
                achieved = read && ok;
            }
            catch
            {
                achieved = false;
                unlockTime = 0;
            }

            double? percent = null;
            if (_percentagesReady && _getAchievementAchievedPercent is not null)
            {
                try
                {
                    if (_getAchievementAchievedPercent(_userStats, apiName, out var raw))
                    {
                        percent = Math.Round(raw, 1, MidpointRounding.AwayFromZero);
                    }
                }
                catch
                {
                    // 全球解锁率是锦上添花，读不到就留 null。
                    percent = null;
                }
            }

            items.Add(new SteamHostAchievementItem
            {
                Name = apiName,
                DisplayName = displayName,
                Description = description,
                Hidden = hidden,
                Achieved = achieved,
                UnlockTime = achieved ? unlockTime : 0,
                GlobalPercent = percent,
            });
        }

        return items;
    }

    private string? ReadAttribute(string apiName, string key)
    {
        if (_getAchievementDisplayAttribute is null)
        {
            return null;
        }

        var ptr = _getAchievementDisplayAttribute(_userStats, apiName, key);
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
    }

    /// <summary>
    /// 发 <c>RequestUserStats</c> 并等 callback 1101；返回 null 表示拿到了成功结果。
    /// </summary>
    private SteamHostAchievementsResult? RequestUserStats(ulong steamId, TimeSpan timeout)
    {
        if (_manualDispatchInit is null
            || _manualDispatchRunFrame is null
            || _manualDispatchGetNextCallback is null
            || _manualDispatchFreeLastCallback is null
            || _getHSteamPipe is null)
        {
            return SteamHostAchievementsResult.Fail(
                SteamAchievementCodes.Unsupported,
                "steam_api64.dll 缺少 ManualDispatch 导出，无法等待成就回调",
                initialized: true);
        }

        try
        {
            if (!_manualInitialized)
            {
                _manualDispatchInit();
                _pipe = _getHSteamPipe();
                _manualInitialized = true;
            }
        }
        catch (Exception ex)
        {
            return SteamHostAchievementsResult.Fail(
                SteamAchievementCodes.Unsupported,
                "初始化 Steam 回调分发失败：" + ex.Message,
                initialized: true);
        }

        _statsReceived = null;
        RequestGlobalPercentages();
        try
        {
            _requestUserStats!(_userStats, steamId);
        }
        catch (Exception ex)
        {
            return SteamHostAchievementsResult.Fail(
                SteamAchievementCodes.RequestFailed,
                "RequestUserStats 调用失败：" + ex.Message,
                initialized: true);
        }

        var deadline = DateTime.UtcNow + timeout;
        while (_statsReceived is null && DateTime.UtcNow < deadline)
        {
            PumpCallbacks();
            PumpManualCallbacks();
            if (_statsReceived is not null)
            {
                break;
            }

            Thread.Sleep(CallbackPumpSleepMs);
        }

        if (_statsReceived is not { } received)
        {
            return SteamHostAchievementsResult.Fail(
                SteamAchievementCodes.Timeout,
                "等待 Steam 成就数据超时（" + (int)timeout.TotalSeconds + " 秒）",
                initialized: true);
        }

        if (received.Result != ResultOk)
        {
            return SteamHostAchievementsResult.Fail(
                SteamAchievementCodes.RequestFailed,
                "Steam 拒绝返回成就数据（EResult = " + received.Result.ToString(CultureInfo.InvariantCulture) + "）",
                initialized: true);
        }

        return null;
    }

    /// <summary>
    /// 全球解锁率要先 <c>RequestGlobalAchievementPercentages</c>，结果走 callback 1110。
    /// 这里只发请求不等：让它和 <c>RequestUserStats</c> 并行，省掉一次串行等待。
    /// </summary>
    private void RequestGlobalPercentages()
    {
        if (_percentagesRequested || _requestGlobalPercentages is null || !_manualInitialized)
        {
            return;
        }

        _percentagesRequested = true;
        try
        {
            _requestGlobalPercentages(_userStats);
        }
        catch
        {
            // 请求失败就当拿不到百分比，条目里留 null。
        }
    }

    /// <summary>
    /// 给 callback 1110 一点收尾时间；等不到就保持未就绪，
    /// 条目里 <c>globalPercent</c> 留 null —— 不写 0 冒充真实数据。
    /// </summary>
    private void WaitGlobalPercentages(TimeSpan grace)
    {
        var deadline = DateTime.UtcNow + grace;
        while (!_percentagesReady && DateTime.UtcNow < deadline)
        {
            PumpCallbacks();
            PumpManualCallbacks();
            if (_percentagesReady)
            {
                return;
            }

            Thread.Sleep(CallbackPumpSleepMs);
        }
    }

    private (string Code, string? Error, string? Detail)? EnsureInitialized()
    {
        if (_initialized)
        {
            return null;
        }

        try
        {
            _library = NativeLibrary.Load(_apiFilePath);
        }
        catch (Exception ex)
        {
            return (SteamAchievementCodes.Native, "加载 steam_api64.dll 失败：" + ex.Message, ex.GetType().Name);
        }

        _shutdown = Bind<ShutdownFn>("SteamAPI_Shutdown");
        _runCallbacks = Bind<RunCallbacksFn>("SteamAPI_RunCallbacks");
        _isSteamRunning = Bind<IsSteamRunningFn>("SteamAPI_IsSteamRunning");
        _steamUserStats = BindAccessor<SteamUserStatsFn>("SteamUserStats", 13, 12, 11);
        _steamUser = BindAccessor<SteamUserFn>("SteamUser", 23, 21, 20);
        _getSteamId = Bind<GetSteamIdFn>("SteamAPI_ISteamUser_GetSteamID");
        _requestUserStats = Bind<RequestUserStatsFn>("SteamAPI_ISteamUserStats_RequestUserStats");
        _getNumAchievements = Bind<GetNumAchievementsFn>("SteamAPI_ISteamUserStats_GetNumAchievements");
        _getAchievementName = Bind<GetAchievementNameFn>("SteamAPI_ISteamUserStats_GetAchievementName");
        _getAchievementDisplayAttribute = Bind<GetAchievementDisplayAttributeFn>("SteamAPI_ISteamUserStats_GetAchievementDisplayAttribute");
        _getAchievementAchievedPercent = Bind<GetAchievementAchievedPercentFn>("SteamAPI_ISteamUserStats_GetAchievementAchievedPercent");
        _requestGlobalPercentages = Bind<RequestGlobalPercentagesFn>("SteamAPI_ISteamUserStats_RequestGlobalAchievementPercentages");
        _getAchievementAndUnlockTime = Bind<GetAchievementAndUnlockTimeFn>("SteamAPI_ISteamUserStats_GetAchievementAndUnlockTime");
        _getHSteamPipe = Bind<GetHSteamPipeFn>("SteamAPI_GetHSteamPipe");
        _manualDispatchInit = Bind<ManualDispatchInitFn>("SteamAPI_ManualDispatch_Init");
        _manualDispatchRunFrame = Bind<ManualDispatchRunFrameFn>("SteamAPI_ManualDispatch_RunFrame");
        _manualDispatchGetNextCallback = Bind<ManualDispatchGetNextCallbackFn>("SteamAPI_ManualDispatch_GetNextCallback");
        _manualDispatchFreeLastCallback = Bind<ManualDispatchFreeLastCallbackFn>("SteamAPI_ManualDispatch_FreeLastCallback");

        if (_shutdown is null)
        {
            return (SteamAchievementCodes.Unsupported, "steam_api64.dll 缺少必要的 Steamworks 导出（Shutdown）", null);
        }

        // 注意顺序：先 Init，失败后才问 IsSteamRunning。这个导出依赖 Steam 客户端的管道/注册表，
        // 个别受限会话里客户端明明在跑也会返回 false —— 先问会把「Init 失败」误报成「Steam 没运行」。
        _previousAppId = Environment.GetEnvironmentVariable(AppIdVariable);
        Environment.SetEnvironmentVariable(AppIdVariable, _appId.ToString(CultureInfo.InvariantCulture));

        var buffer = new byte[InitFlatBufferLength];
        var initFlat = Bind<InitFlatFn>("SteamAPI_InitFlat");
        var initialized = false;
        if (initFlat is not null && initFlat(buffer) == 0)
        {
            initialized = true;
        }
        else
        {
            var plain = Bind<InitFn>("SteamAPI_Init");
            if (plain is not null && plain())
            {
                initialized = true;
            }
        }

        if (!initialized)
        {
            var message = Encoding.UTF8.GetString(buffer).TrimEnd('\0', ' ');
            if (ClassifyInitFailure(TryIsSteamRunning(), IsSteamClientProcessRunning()) == SteamAchievementCodes.SteamNotRunning)
            {
                return (SteamAchievementCodes.SteamNotRunning, "Steam 客户端没有运行", message.Length > 0 ? message : null);
            }

            return message.Length > 0
                ? (SteamAchievementCodes.InitFailed, "SteamAPI_Init 失败：" + message, message)
                : (SteamAchievementCodes.InitFailed, "SteamAPI_Init 失败（Steam 未运行或未登录）", null);
        }

        _initialized = true;
        try
        {
            _userStats = _steamUserStats?.Invoke() ?? IntPtr.Zero;
        }
        catch
        {
            _userStats = IntPtr.Zero;
        }

        try
        {
            _user = _steamUser?.Invoke() ?? IntPtr.Zero;
        }
        catch
        {
            _user = IntPtr.Zero;
        }

        return null;
    }

    private bool EnsureAchievementBindings() =>
        _getNumAchievements is not null
        && _getAchievementName is not null
        && _getAchievementDisplayAttribute is not null
        && _getAchievementAndUnlockTime is not null
        && _requestUserStats is not null;

    /// <summary>
    /// 只用来在 Init 失败后区分「Steam 没运行」和「运行了但初始化失败」。
    /// 导出缺失或调用抛异常时返回 null（判不出来，让 Init 的错误信息说话）。
    /// </summary>
    private bool? TryIsSteamRunning()
    {
        if (_isSteamRunning is null)
        {
            return null;
        }

        try
        {
            return _isSteamRunning();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Init 失败时决定结果码。只有「Steam 确实没在跑」才报 <c>steamNotRunning</c>；
    /// appid 无效、未登录、被占用都会让 Init 失败，但它们都说明客户端在跑，必须报 <c>initFailed</c>。
    /// </summary>
    /// <param name="isSteamRunning"><c>SteamAPI_IsSteamRunning</c> 的结果；判不出来为 null。</param>
    /// <param name="steamClientProcessFound">进程表里是否看到 Steam 客户端。</param>
    /// <returns><see cref="SteamAchievementCodes.SteamNotRunning"/> 或 <see cref="SteamAchievementCodes.InitFailed"/>。</returns>
    internal static string ClassifyInitFailure(bool? isSteamRunning, bool steamClientProcessFound)
    {
        if (isSteamRunning == true || steamClientProcessFound)
        {
            return SteamAchievementCodes.InitFailed;
        }

        // 导出缺失（null）时不要武断说「没运行」：Init 的错误信息里会写「未运行或未登录」。
        return isSteamRunning == false ? SteamAchievementCodes.SteamNotRunning : SteamAchievementCodes.InitFailed;
    }

    /// <summary>
    /// 进程表里的 Steam 客户端旁证。<c>SteamAPI_IsSteamRunning</c> 在部分受限会话里
    /// 即使客户端在跑也返回 false，所以再看一眼进程：<c>steam.exe</c> 可能因会话隔离不可见，
    /// 但它的 <c>steamwebhelper</c> 子进程通常能看到。只排除常驻的 <c>SteamService</c>。
    /// </summary>
    private static bool IsSteamClientProcessRunning()
    {
        System.Diagnostics.Process[] processes;
        try
        {
            processes = System.Diagnostics.Process.GetProcesses();
        }
        catch
        {
            return false;
        }

        try
        {
            foreach (var process in processes)
            {
                string name;
                try
                {
                    name = process.ProcessName;
                }
                catch
                {
                    continue;
                }

                if (!name.StartsWith("steam", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (name.Equals("steamservice", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return true;
            }

            return false;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private void PumpCallbacks()
    {
        try
        {
            _runCallbacks?.Invoke();
        }
        catch
        {
            // 回调泵失败不影响 manual dispatch 的轮询。
        }
    }

    private void PumpManualCallbacks()
    {
        if (!_manualInitialized)
        {
            return;
        }

        _manualDispatchRunFrame!(_pipe);
        var message = Marshal.AllocHGlobal(Marshal.SizeOf<SteamCallbackMsg>());
        try
        {
            while (_manualDispatchGetNextCallback!(_pipe, message))
            {
                try
                {
                    var header = Marshal.PtrToStructure<SteamCallbackMsg>(message);
                    if (header.Param == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (header.CallbackId == UserStatsReceivedId)
                    {
                        _statsReceived = Marshal.PtrToStructure<UserStatsReceived>(header.Param);
                    }
                    else if (header.CallbackId == GlobalPercentagesReadyId)
                    {
                        _percentagesReady = true;
                    }
                }
                finally
                {
                    _manualDispatchFreeLastCallback!(_pipe);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(message);
        }
    }

    /// <summary>按版本从高到低找 <c>SteamAPI_SteamXxx_vNNN</c> 访问器。</summary>
    private T? BindAccessor<T>(string family, params int[] versions)
        where T : Delegate
    {
        foreach (var version in versions)
        {
            var accessor = Bind<T>("SteamAPI_" + family + "_v" + version.ToString("D3", CultureInfo.InvariantCulture));
            if (accessor is not null)
            {
                return accessor;
            }
        }

        return null;
    }

    /// <summary>取一个导出并绑定成委托；不存在返回 null。</summary>
    private T? Bind<T>(string exportName)
        where T : Delegate
    {
        if (_library == IntPtr.Zero
            || !NativeLibrary.TryGetExport(_library, exportName, out var address)
            || address == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }
        catch
        {
            return null;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitFlatFn(byte[] errorMessage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool InitFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ShutdownFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RunCallbacksFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool IsSteamRunningFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SteamUserStatsFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SteamUserFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong GetSteamIdFn(IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong RequestUserStatsFn(IntPtr userStats, ulong steamId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetNumAchievementsFn(IntPtr userStats);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetAchievementNameFn(IntPtr userStats, uint index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetAchievementDisplayAttributeFn(
        IntPtr userStats,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool GetAchievementAchievedPercentFn(
        IntPtr userStats,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        out float percent);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool GetAchievementAndUnlockTimeFn(
        IntPtr userStats,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.I1)] out bool achieved,
        out uint unlockTime);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong RequestGlobalPercentagesFn(IntPtr userStats);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetHSteamPipeFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ManualDispatchInitFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ManualDispatchRunFrameFn(int pipe);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ManualDispatchGetNextCallbackFn(int pipe, IntPtr message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ManualDispatchFreeLastCallbackFn(int pipe);

    /// <summary><c>CCallbackMsg</c> 头（HSteamUser int、回调 id int、参数指针、参数长度）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SteamCallbackMsg
    {
        public int SteamUser;
        public int CallbackId;
        public IntPtr Param;
        public int ParamSize;
    }

    /// <summary>
    /// <c>UserStatsReceived_t</c>（callback 1101）：<c>uint64 m_nGameID</c>、
    /// <c>EResult m_eResult</c>（int）、<c>CSteamID m_steamIDUser</c>（uint64）。
    /// x64 下 <c>Marshal.SizeOf</c> = 24。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct UserStatsReceived
    {
        public ulong GameId;
        public int Result;
        public ulong SteamIdUser;
    }
}
