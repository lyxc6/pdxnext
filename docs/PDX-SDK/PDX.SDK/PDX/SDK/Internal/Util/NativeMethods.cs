using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32.SafeHandles;

namespace PDX.SDK.Internal.Util;

internal static class NativeMethods
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	internal struct WIN32_FIND_DATA
	{
		internal FileAttributes dwFileAttributes;

		internal System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;

		internal System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;

		internal System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;

		internal uint nFileSizeHigh;

		internal uint nFileSizeLow;

		internal uint dwReserved0;

		internal uint dwReserved1;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		internal string cFileName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
		internal string cAlternate;
	}

	internal const int FILE_ATTRIBUTE_REPARSE_POINT = 1024;

	internal const int FILE_ATTRIBUTE_ARCHIVE = 32;

	internal const int FILE_ATTRIBUTE_DIRECTORY = 16;

	internal const int INVALID_FILE_ATTRIBUTES = -1;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern SafeFileHandle CreateFile(string lpFileName, FileAccess dwDesiredAccess, FileShare dwShareMode, IntPtr lpSecurityAttributes, FileMode dwCreationDisposition, int dwFlagsAndAttributes, IntPtr hTemplateFile);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern int GetFileAttributesW(string lpFileName);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool CopyFileW(string lpExistingFileName, string lpNewFileName, bool bFailIfExists);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool DeleteFileW(string lpFileName);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool MoveFileW(string lpExistingFileName, string lpNewFileName);

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern IntPtr FindFirstFile(string lpFileName, out WIN32_FIND_DATA lpFindFileData);

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool FindNextFile(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool FindClose(IntPtr hFindFile);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool RemoveDirectory(string path);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool CreateDirectory(string lpPathName, IntPtr lpSecurityAttributes);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	internal static extern bool GetDiskFreeSpaceEx(string lpDirectoryName, out ulong lpFreeBytesAvailable, out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);
}
