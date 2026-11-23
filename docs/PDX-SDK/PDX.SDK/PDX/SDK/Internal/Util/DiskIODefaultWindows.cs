using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace PDX.SDK.Internal.Util;

internal class DiskIODefaultWindows : DiskIO
{
	private const int MAX_SHORT_PATH_LENGTH = 260;

	private const string LONG_PATH_PREFIX = "\\\\?\\";

	internal SafeFileHandle FileHandle;

	public override bool PathExists(string path)
	{
		if (!IsLongPath(path))
		{
			path = GetCleanPath(path);
			if (!File.Exists(path))
			{
				return Directory.Exists(path);
			}
			return true;
		}
		if (!LongPathFileExists(path))
		{
			return LongPathDirectoryExists(path);
		}
		return true;
	}

	internal bool FileExists(string fileName)
	{
		if (!IsLongPath(fileName))
		{
			fileName = GetCleanPath(fileName);
			return File.Exists(fileName);
		}
		return LongPathFileExists(fileName);
	}

	internal bool DirectoryExists(string dirName)
	{
		if (!IsLongPath(dirName))
		{
			dirName = GetCleanPath(dirName);
			return Directory.Exists(dirName);
		}
		return LongPathDirectoryExists(dirName);
	}

	public override string GetDirectoryName(string path)
	{
		if (!IsLongPath(path))
		{
			return Path.GetDirectoryName(GetCleanPath(path));
		}
		return GetLongPathDirectoryName(path);
	}

	public override bool CopyFile(string sourceFile, string targetFile, bool failIfExists = false)
	{
		if (FileExists(sourceFile))
		{
			if (IsLongPath(sourceFile) || IsLongPath(targetFile))
			{
				return CopyLongPathFile(sourceFile, targetFile, failIfExists);
			}
			try
			{
				File.Copy(GetCleanPath(sourceFile), GetCleanPath(targetFile), !failIfExists);
				return true;
			}
			catch
			{
				return false;
			}
		}
		return false;
	}

	public override bool CopyDirectory(string sourceDir, string targetDir, bool recursive = true, bool failIfExists = false)
	{
		if (DirectoryExists(sourceDir))
		{
			try
			{
				CreateDirectory(targetDir);
				foreach (string item in ListFiles(sourceDir))
				{
					string fileName = Path.GetFileName(item);
					string targetFile = Combine(targetDir, fileName);
					if (!CopyFile(item, targetFile, failIfExists))
					{
						return false;
					}
				}
				if (recursive)
				{
					foreach (string item2 in ListDirectories(sourceDir))
					{
						string name = new DirectoryInfo(item2).Name;
						string targetDir2 = Combine(targetDir, name);
						if (!CopyDirectory(item2, targetDir2, recursive: true, failIfExists))
						{
							return false;
						}
					}
				}
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}
		return false;
	}

	public override void Delete(string path)
	{
		if (PathExists(path))
		{
			if (!IsLongPath(path))
			{
				File.Delete(GetCleanPath(path));
			}
			else
			{
				DeleteLongPathFile(path);
			}
		}
	}

	public override void DeleteDirectory(string path, bool recursive)
	{
		if (PathExists(path))
		{
			if (!recursive && !IsLongPath(path))
			{
				Directory.Delete(GetCleanPath(path), recursive);
			}
			else
			{
				DeleteLongPathDirectory(path, recursive);
			}
		}
	}

	public override void CreateDirectory(string path)
	{
		if (!PathExists(path))
		{
			if (!IsLongPath(path))
			{
				Directory.CreateDirectory(GetCleanPath(path));
			}
			else
			{
				CreateLongPathDirectory(path);
			}
		}
	}

	public override List<string> ListDirectories(string path)
	{
		if (!PathExists(path))
		{
			return new List<string>();
		}
		if (!IsLongPath(path))
		{
			return Directory.GetDirectories(GetCleanPath(path)).ToList();
		}
		return GetLongPathDirectories(path);
	}

	public override List<string> ListFiles(string path)
	{
		if (!PathExists(path))
		{
			return new List<string>();
		}
		if (!IsLongPath(path))
		{
			return Directory.GetFiles(path).ToList();
		}
		return GetLongPathFiles(path);
	}

	public override List<string> ListFilesRecursive(string path, string searchPattern = "*")
	{
		if (!PathExists(path))
		{
			return new List<string>();
		}
		if (!IsLongPath(path))
		{
			return Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories).ToList();
		}
		return GetLongPathFilesRecursive(path, searchPattern);
	}

	public override void Move(string sourcePath, string targetPath)
	{
		if (PathExists(sourcePath))
		{
			if (!IsLongPath(sourcePath) && !IsLongPath(targetPath))
			{
				Directory.Move(GetCleanPath(sourcePath), GetCleanPath(targetPath));
			}
			else
			{
				LongPathMove(sourcePath, targetPath);
			}
		}
	}

	public override Stream CreateFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare fileShare)
	{
		if (!IsLongPath(path))
		{
			return new FileStream(GetCleanPath(path), fileMode, fileAccess, fileShare);
		}
		return CreateLongPathFileStream(path, fileMode, fileAccess, fileShare);
	}

	public override int GetFileAttributes(string path)
	{
		if (!IsLongPath(path))
		{
			return (int)File.GetAttributes(path);
		}
		return GetFileAttributesInternal(path);
	}

	private int GetFileAttributesInternal(string path)
	{
		return NativeMethods.GetFileAttributesW(GetLongPath(path));
	}

	private bool LongPathFileExists(string path)
	{
		int fileAttributesW = NativeMethods.GetFileAttributesW(GetLongPath(path));
		if (fileAttributesW != -1)
		{
			return (fileAttributesW & 0x20) == 32;
		}
		return false;
	}

	private bool LongPathDirectoryExists(string path)
	{
		int fileAttributesW = NativeMethods.GetFileAttributesW(GetLongPath(path));
		if (fileAttributesW != -1)
		{
			return (fileAttributesW & 0x10) == 16;
		}
		return false;
	}

	private string GetLongPathDirectoryName(string path)
	{
		path = GetLongPath(path);
		int num = path.LastIndexOf(Path.DirectorySeparatorChar);
		if (num < 0)
		{
			return string.Empty;
		}
		return path.Substring(0, num);
	}

	private bool CopyLongPathFile(string sourceFile, string targetFile, bool failIfExists)
	{
		return NativeMethods.CopyFileW(GetLongPath(sourceFile), GetLongPath(targetFile), failIfExists);
	}

	private void DeleteLongPathFile(string path)
	{
		if (!NativeMethods.DeleteFileW(GetLongPath(path)))
		{
			throw new IOException();
		}
	}

	private void DeleteLongPathDirectory(string path, bool recursive)
	{
		if (recursive)
		{
			foreach (string longPathFile in GetLongPathFiles(path))
			{
				Delete(longPathFile);
			}
			foreach (string longPathDirectory in GetLongPathDirectories(path))
			{
				DeleteLongPathDirectory(longPathDirectory, recursive: true);
			}
		}
		if (!NativeMethods.RemoveDirectory(GetLongPath(path)))
		{
			throw new IOException();
		}
	}

	private void CreateLongPathDirectory(string path)
	{
		foreach (string item in GetAllPathsFromPath(GetLongPath(path)))
		{
			if (!PathExists(item) && !NativeMethods.CreateDirectory(item, IntPtr.Zero))
			{
				throw new IOException();
			}
		}
	}

	private List<string> GetLongPathDirectories(string path)
	{
		List<string> list = new List<string>();
		NativeMethods.WIN32_FIND_DATA lpFindFileData;
		IntPtr intPtr = NativeMethods.FindFirstFile(Path.Combine(GetLongPath(path), "*"), out lpFindFileData);
		try
		{
			if (intPtr != new IntPtr(-1))
			{
				do
				{
					if ((lpFindFileData.dwFileAttributes & FileAttributes.Directory) != 0 && lpFindFileData.cFileName != "." && lpFindFileData.cFileName != "..")
					{
						list.Add(GetCleanPath(Path.Combine(path, lpFindFileData.cFileName)));
					}
				}
				while (NativeMethods.FindNextFile(intPtr, out lpFindFileData));
			}
		}
		finally
		{
			NativeMethods.FindClose(intPtr);
		}
		return list;
	}

	private List<string> GetLongPathFiles(string path)
	{
		List<string> list = new List<string>();
		NativeMethods.WIN32_FIND_DATA lpFindFileData;
		IntPtr intPtr = NativeMethods.FindFirstFile(Path.Combine(GetLongPath(path), "*"), out lpFindFileData);
		try
		{
			if (intPtr != new IntPtr(-1))
			{
				do
				{
					if ((lpFindFileData.dwFileAttributes & FileAttributes.Directory) == 0)
					{
						list.Add(GetCleanPath(Path.Combine(path, lpFindFileData.cFileName)));
					}
				}
				while (NativeMethods.FindNextFile(intPtr, out lpFindFileData));
			}
		}
		finally
		{
			NativeMethods.FindClose(intPtr);
		}
		return list;
	}

	private List<string> GetLongPathFilesRecursive(string path, string searchPattern)
	{
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		NativeMethods.WIN32_FIND_DATA lpFindFileData;
		IntPtr intPtr = NativeMethods.FindFirstFile(Path.Combine(GetLongPath(path), "*"), out lpFindFileData);
		try
		{
			if (intPtr != new IntPtr(-1))
			{
				do
				{
					if ((lpFindFileData.dwFileAttributes & FileAttributes.Directory) == 0)
					{
						if (Regex.IsMatch(lpFindFileData.cFileName, StringUtils.WildCardToRegular(searchPattern)))
						{
							list.Add(GetCleanPath(Path.Combine(path, lpFindFileData.cFileName)));
						}
					}
					else
					{
						list2.Add(lpFindFileData.cFileName);
					}
				}
				while (NativeMethods.FindNextFile(intPtr, out lpFindFileData));
			}
		}
		finally
		{
			NativeMethods.FindClose(intPtr);
		}
		foreach (string item in list2)
		{
			string path2 = Path.Combine(path, item);
			list.AddRange(GetLongPathFilesRecursive(path2, searchPattern));
		}
		return list;
	}

	private void LongPathMove(string sourcePath, string targetPath)
	{
		if (!NativeMethods.MoveFileW(GetLongPath(sourcePath), GetLongPath(targetPath)))
		{
			throw new IOException();
		}
	}

	private Stream CreateLongPathFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare fileShare)
	{
		if (fileMode == FileMode.Append)
		{
			fileMode = FileMode.OpenOrCreate;
		}
		FileHandle = NativeMethods.CreateFile(GetLongPath(path), fileAccess, FileShare.ReadWrite, IntPtr.Zero, fileMode, 0, IntPtr.Zero);
		if (FileHandle.IsInvalid)
		{
			throw new IOException();
		}
		return new FileStream(FileHandle, fileAccess);
	}

	private bool IsLongPath(string path)
	{
		return GetCleanPath(path).Length >= 260;
	}

	private string GetLongPath(string path)
	{
		if (!path.StartsWith("\\\\?\\"))
		{
			return "\\\\?\\" + path.Replace('/', Path.DirectorySeparatorChar);
		}
		return path.Replace('/', Path.DirectorySeparatorChar);
	}

	private string GetCleanPath(string path)
	{
		if (!path.StartsWith("\\\\?\\"))
		{
			return path;
		}
		return path.Substring("\\\\?\\".Length);
	}

	private List<string> GetAllPathsFromPath(string path)
	{
		string[] array = path.Replace('/', Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
		if (array.Length == 1)
		{
			return new List<string> { path };
		}
		int i = "\\\\?\\".Length;
		List<string> list = new List<string>();
		string text = string.Empty;
		for (int j = 0; j < i; j++)
		{
			if (j > 0)
			{
				text += Path.DirectorySeparatorChar;
			}
			text += array[j];
		}
		for (; i < array.Length; i++)
		{
			text = Combine(text, array[i]);
			list.Add(text);
		}
		return list;
	}

	private string Combine(string path1, string path2)
	{
		if (string.IsNullOrEmpty(path1))
		{
			return path2;
		}
		if (string.IsNullOrEmpty(path2))
		{
			return path1;
		}
		return path1.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + path2.TrimStart(Path.DirectorySeparatorChar).TrimEnd('.');
	}
}
