using System;
using System.Runtime.InteropServices;

namespace PDX.SDK.Internal.Util;

internal static class SteamNativeMethods
{
	private const string LibraryName = "steam_api64";

	[DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool SteamAPI_IsSteamRunning();

	[DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool SteamAPI_Init();

	[DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
	public static extern void SteamAPI_Shutdown();

	[DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr SteamAPI_SteamUser_v021();

	[DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
	public static extern IntPtr SteamAPI_SteamUser_v023();

	[DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SteamAPI_ISteamUser_GetSteamID")]
	public static extern ulong ISteamUser_GetSteamID(IntPtr instancePtr);
}
