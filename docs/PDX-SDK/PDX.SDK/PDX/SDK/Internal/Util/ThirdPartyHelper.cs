using System;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Internal.Service.Generic;

namespace PDX.SDK.Internal.Util;

internal class ThirdPartyHelper
{
	public static StorageInformation GetDeviceStorageInformation(string rootPath, ILogger logger = null)
	{
		long num = 0L;
		long num2 = 0L;
		NativeMethods.GetDiskFreeSpaceEx(rootPath, out var lpFreeBytesAvailable, out var lpTotalNumberOfBytes, out var _);
		num = (long)lpFreeBytesAvailable;
		num2 = (long)lpTotalNumberOfBytes;
		return new StorageInformation
		{
			AvailableFreeBytes = num,
			TotalBytes = num2
		};
	}

	public static string GetSteamUserId()
	{
		IntPtr intPtr = SteamNativeMethods.SteamAPI_SteamUser_v023();
		if (intPtr == IntPtr.Zero)
		{
			return string.Empty;
		}
		ulong num = SteamNativeMethods.ISteamUser_GetSteamID(intPtr);
		if (num == 0L)
		{
			return string.Empty;
		}
		return num.ToString();
	}
}
