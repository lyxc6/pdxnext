namespace PdxLauncher.Core.Steam;

using System.Globalization;
using System.Runtime.InteropServices;

/// <summary>
/// 直接加载游戏目录里那份 steam_api DLL，问 Steam「这些 DLC 授权了吗」。
/// <para>
/// 走 <see cref="NativeLibrary"/> 而不是静态 <c>DllImport</c>：静态解析器全进程只能绑一个
/// 路径，探测第二个游戏就会锁死；而且本类型现在只在 <c>--steam-host</c> 子进程里运行，
/// 每次只加载命令行指定的那一份 DLL。
/// </para>
/// <para>
/// 只读：不改游戏目录里的任何文件，也不联网；结束后必定 <c>SteamAPI_Shutdown</c> + Free。
/// </para>
/// </summary>
internal sealed class SteamDllProbe : ISteamDllProbe
{
    /// <summary>喂给 steam_api 的环境变量名（与官方启动器的 <c>SteamAppId</c> 一致）。</summary>
    private const string AppIdVariable = "SteamAppId";

    private const string OwnerName = "DLC 授权探测";

    private const int NameBufferLength = 256;

    private const int InitFlatBufferLength = 1024;

    /// <summary>防止异常 DLL 报出离谱的 DLC 数量把界面拖死。</summary>
    private const uint MaxDlcCount = 8192;

    /// <inheritdoc />
    public SteamDllProbeResult Probe(string apiFilePath, int appId)
    {
        if (string.IsNullOrWhiteSpace(apiFilePath))
        {
            return SteamDllProbeResult.Fail(SteamProbeError.NoDll);
        }

        if (!OperatingSystem.IsWindows())
        {
            return SteamDllProbeResult.Fail(SteamProbeError.NotWindows);
        }

        IDisposable? session = null;
        var library = IntPtr.Zero;
        var initialized = false;
        var previousAppId = Environment.GetEnvironmentVariable(AppIdVariable);
        try
        {
            if (!SteamNativeSession.TryEnter(OwnerName, out session))
            {
                return SteamDllProbeResult.Fail(SteamProbeError.Busy);
            }

            Environment.SetEnvironmentVariable(
                AppIdVariable,
                appId.ToString(CultureInfo.InvariantCulture));

            library = NativeLibrary.Load(apiFilePath);

            if (TryGetDelegate<IsSteamRunningFn>(library, "SteamAPI_IsSteamRunning", out var isRunning)
                && isRunning is not null
                && isRunning() == 0)
            {
                return SteamDllProbeResult.Fail(SteamProbeError.SteamNotRunning);
            }

            initialized = Initialize(library);
            if (!initialized)
            {
                return SteamDllProbeResult.Fail(SteamProbeError.InitFailed);
            }

            return new SteamDllProbeResult(
                Initialized: true,
                SteamRunning: true,
                SteamId64: ReadSteamId(library),
                Dlcs: ReadDlcs(library),
                Error: null);
        }
        catch (DllNotFoundException ex)
        {
            return SteamDllProbeResult.Fail(SteamProbeError.NoDll, ex.Message);
        }
        catch (BadImageFormatException ex)
        {
            return SteamDllProbeResult.Fail(SteamProbeError.X86, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return SteamDllProbeResult.Fail(SteamProbeError.Native, ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            if (library != IntPtr.Zero)
            {
                if (initialized)
                {
                    TryShutdown(library);
                }

                NativeLibrary.Free(library);
            }

            Environment.SetEnvironmentVariable(AppIdVariable, previousAppId);
            session?.Dispose();
        }
    }

    private static bool Initialize(IntPtr library)
    {
        // 启动器 sdk 里的 steam_api 暴露 InitFlat；游戏目录那份通常只有 Init / InitSafe。
        if (TryGetDelegate<InitFlatFn>(library, "SteamAPI_InitFlat", out var initFlat) && initFlat is not null)
        {
            var buffer = Marshal.AllocHGlobal(InitFlatBufferLength);
            try
            {
                Marshal.Copy(new byte[InitFlatBufferLength], 0, buffer, InitFlatBufferLength);

                if (initFlat(buffer) != 0)
                {
                    return true;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        if (TryGetDelegate<InitFn>(library, "SteamAPI_Init", out var init) && init is not null && init() != 0)
        {
            return true;
        }

        if (TryGetDelegate<InitFn>(library, "SteamAPI_InitSafe", out var initSafe) && initSafe is not null && initSafe() != 0)
        {
            return true;
        }

        return false;
    }

    private static void TryShutdown(IntPtr library)
    {
        try
        {
            if (TryGetDelegate<ShutdownFn>(library, "SteamAPI_Shutdown", out var shutdown) && shutdown is not null)
            {
                shutdown();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 关不掉不影响判定结果，吞掉即可。
        }
    }

    private static ulong ReadSteamId(IntPtr library)
    {
        var user = IntPtr.Zero;
        if (TryGetDelegate<SteamUserFn>(library, "SteamAPI_SteamUser_v021", out var v21) && v21 is not null)
        {
            user = v21();
        }

        if (user == IntPtr.Zero
            && TryGetDelegate<SteamUserFn>(library, "SteamAPI_SteamUser_v020", out var v20)
            && v20 is not null)
        {
            user = v20();
        }

        if (user == IntPtr.Zero)
        {
            return 0;
        }

        return TryGetDelegate<GetSteamIdFn>(library, "SteamAPI_ISteamUser_GetSteamID", out var getSteamId)
            && getSteamId is not null
                ? getSteamId(user)
                : 0;
    }

    private static IReadOnlyList<SteamDlcOwnership> ReadDlcs(IntPtr library)
    {
        if (!TryGetDelegate<SteamAppsFn>(library, "SteamAPI_SteamApps_v008", out var getApps) || getApps is null)
        {
            return Array.Empty<SteamDlcOwnership>();
        }

        var apps = getApps();
        if (apps == IntPtr.Zero)
        {
            return Array.Empty<SteamDlcOwnership>();
        }

        if (!TryGetDelegate<GetDlcCountFn>(library, "SteamAPI_ISteamApps_GetDLCCount", out var getCount) || getCount is null)
        {
            return Array.Empty<SteamDlcOwnership>();
        }

        if (!TryGetDelegate<GetDlcDataFn>(library, "SteamAPI_ISteamApps_BGetDLCDataByIndex", out var getData) || getData is null)
        {
            return Array.Empty<SteamDlcOwnership>();
        }

        TryGetDelegate<BoolAppFn>(library, "SteamAPI_ISteamApps_BIsSubscribedApp", out var isSubscribed);
        TryGetDelegate<BoolAppFn>(library, "SteamAPI_ISteamApps_BIsDlcInstalled", out var isDlcInstalled);

        var count = getCount(apps);
        if (count > MaxDlcCount)
        {
            count = MaxDlcCount;
        }

        var results = new List<SteamDlcOwnership>((int)count);
        var buffer = Marshal.AllocHGlobal(NameBufferLength);
        try
        {
            for (uint index = 0; index < count; index++)
            {
                if (getData(apps, (int)index, out var dlcId, out var available, buffer, NameBufferLength) == 0)
                {
                    continue;
                }

                var name = Marshal.PtrToStringUTF8(buffer);
                if (string.IsNullOrWhiteSpace(name))
                {
                    // 官方启动器也把空名的条目滤掉。
                    continue;
                }

                var owned = (isSubscribed is not null && isSubscribed(apps, dlcId) != 0)
                    || (isDlcInstalled is not null && isDlcInstalled(apps, dlcId) != 0);

                results.Add(new SteamDlcOwnership(
                    dlcId.ToString(CultureInfo.InvariantCulture),
                    name!,
                    owned,
                    available != 0));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return results;
    }

    private static bool TryGetDelegate<T>(IntPtr library, string exportName, out T? value)
        where T : Delegate
    {
        value = null;
        if (!NativeLibrary.TryGetExport(library, exportName, out var address) || address == IntPtr.Zero)
        {
            return false;
        }

        value = Marshal.GetDelegateForFunctionPointer<T>(address);
        return true;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte InitFlatFn(IntPtr errorBuffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte InitFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ShutdownFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte IsSteamRunningFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SteamAppsFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetDlcCountFn(IntPtr apps);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte GetDlcDataFn(IntPtr apps, int index, out uint appId, out byte available, IntPtr nameBuffer, int nameLength);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte BoolAppFn(IntPtr apps, uint appId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SteamUserFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong GetSteamIdFn(IntPtr user);
}
