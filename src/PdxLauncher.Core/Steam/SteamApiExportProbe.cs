namespace PdxLauncher.Core.Steam;

using System.Runtime.InteropServices;

/// <summary>
/// 导出表探测：判断一份 <c>steam_api64.dll</c> 能不能用于「读成就」。
/// <para>
/// 判据看的是导出表，不靠体积/文件名猜：必须同时具备 <c>SteamAPI_Init</c>（或 <c>InitFlat</c>）
/// 与 <c>SteamAPI_ISteamUserStats_GetNumAchievements</c>。拿不到后者的 loader（CreamAPI 之类
/// 只挂 DLC 解锁的实现）即使体积更大也不算合格候选。
/// </para>
/// <para>
/// <b>只能在 <c>--steam-host</c> 子进程里调用</b>：Load 会执行 DllMain，loader 形态的 DLL 甚至可能
/// 直接崩（0xC0000005）。主进程一律不碰原生库。
/// </para>
/// </summary>
public static class SteamApiExportProbe
{
    /// <summary>成就用途必需的 UserStats 导出。</summary>
    public const string RequiredUserStatsExport = "SteamAPI_ISteamUserStats_GetNumAchievements";

    /// <summary>Load 并检查导出；检查完立即 Free，不调用 <c>SteamAPI_Init</c>。</summary>
    /// <param name="path">DLL 绝对路径。</param>
    /// <param name="reason">不合格原因；合格时为 null。</param>
    /// <returns>合格时为 true。</returns>
    public static bool TryCheck(string path, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "路径为空";
            return false;
        }

        IntPtr library;
        try
        {
            library = NativeLibrary.Load(path!);
        }
        catch (Exception ex)
        {
            reason = ex.GetType().Name + "：" + ex.Message;
            return false;
        }

        try
        {
            var hasInit = NativeLibrary.TryGetExport(library, "SteamAPI_InitFlat", out _)
                || NativeLibrary.TryGetExport(library, "SteamAPI_Init", out _);
            if (!hasInit)
            {
                reason = "缺少 SteamAPI_Init / SteamAPI_InitFlat 导出";
                return false;
            }

            if (!NativeLibrary.TryGetExport(library, RequiredUserStatsExport, out _))
            {
                reason = "缺少 " + RequiredUserStatsExport + " 导出（像是只做 DLC 解锁的 loader）";
                return false;
            }

            return true;
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }
}
