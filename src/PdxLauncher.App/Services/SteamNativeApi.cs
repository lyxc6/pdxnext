namespace PdxLauncher.App.Services;

using System.Runtime.InteropServices;

/// <summary>
/// <c>steam_api64.dll</c> 的 flat API 声明（Steamworks SDK 1.65）。
/// <para>
/// 为什么不用 Steamworks.NET：1.65 的 <c>steam_api.h</c> 把 <c>SteamAPI_Init()</c> 改成了
/// inline（转发到 <c>SteamAPI_InitEx</c> → <c>SteamInternal_SteamAPI_Init</c>），
/// DLL 里**不再导出** <c>SteamAPI_Init</c>；而 Steamworks.NET 2024.8.0 编译自 SDK 1.60，
/// 走的正是那个已消失的导出，加载 1.65 的 DLL 会直接 EntryPointNotFound。
/// 这里只声明真正用到的 7 个导出（均已用 <c>NativeLibrary.TryGetExport</c> 在本机 1.65 DLL 上核对过）。
/// </para>
/// <para>
/// 注意 C++ 的 <c>bool</c> 是 1 字节，必须用 <see cref="UnmanagedType.I1"/> 修饰返回值，
/// 否则高位脏数据会让 <c>false</c> 变成 <c>true</c>。
/// </para>
/// </summary>
internal static class SteamNativeApi
{
    /// <summary>逻辑库名；实际路径由 <c>DllImportResolver</c> 映射。</summary>
    internal const string LibraryName = "steam_api64";

    /// <summary><c>ESteamAPIInitResult SteamAPI_InitFlat(SteamErrMsg*)</c>；返回 0 表示成功。</summary>
    [DllImport(LibraryName, EntryPoint = "SteamAPI_InitFlat", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int InitFlat(byte[] errorMessage);

    /// <summary><c>void SteamAPI_Shutdown()</c>。</summary>
    [DllImport(LibraryName, EntryPoint = "SteamAPI_Shutdown", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Shutdown();

    /// <summary><c>void SteamAPI_RunCallbacks()</c>；必须在初始化它的线程上周期调用。</summary>
    [DllImport(LibraryName, EntryPoint = "SteamAPI_RunCallbacks", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RunCallbacks();

    /// <summary><c>bool SteamAPI_IsSteamRunning()</c>。</summary>
    [DllImport(LibraryName, EntryPoint = "SteamAPI_IsSteamRunning", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool IsSteamRunning();

    /// <summary><c>ISteamUGC* SteamAPI_SteamUGC_v021()</c>（无参）。</summary>
    [DllImport(LibraryName, EntryPoint = "SteamAPI_SteamUGC_v021", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr SteamUgc();

    /// <summary><c>SteamAPICall_t SteamAPI_ISteamUGC_SubscribeItem(ISteamUGC*, PublishedFileId_t)</c>。</summary>
    [DllImport(LibraryName, EntryPoint = "SteamAPI_ISteamUGC_SubscribeItem", CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong SubscribeItem(IntPtr self, ulong publishedFileId);

    /// <summary><c>SteamAPICall_t SteamAPI_ISteamUGC_UnsubscribeItem(ISteamUGC*, PublishedFileId_t)</c>。</summary>
    [DllImport(LibraryName, EntryPoint = "SteamAPI_ISteamUGC_UnsubscribeItem", CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong UnsubscribeItem(IntPtr self, ulong publishedFileId);

    /// <summary><c>uint32 SteamAPI_ISteamUGC_GetItemState(ISteamUGC*, PublishedFileId_t)</c>。</summary>
    [DllImport(LibraryName, EntryPoint = "SteamAPI_ISteamUGC_GetItemState", CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint GetItemState(IntPtr self, ulong publishedFileId);
}
