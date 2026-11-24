using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PDX.SDK.Contracts.Util;

namespace PDX.SDK.Internal.Util;

internal abstract class DiskIO : IDiskIO
{
	public virtual void CreateDirectory(string path)
	{
		if (!PathExists(path))
		{
			Directory.CreateDirectory(path);
		}
	}

	public virtual Stream CreateFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare fileShare)
	{
		return new FileStream(path, fileMode, fileAccess, fileShare);
	}

	public virtual void Delete(string path)
	{
		if (PathExists(path))
		{
			File.Delete(path);
		}
	}

	public virtual void DeleteDirectory(string path, bool recursive = false)
	{
		if (PathExists(path))
		{
			Directory.Delete(path, recursive);
		}
	}

	public virtual List<string> FindSubdirectoriesRecursive(string root, string path)
	{
		List<string> list = new List<string>();
		if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path))
		{
			return list;
		}
		List<string> list2 = new List<string>();
		try
		{
			foreach (string item in ListDirectories(root))
			{
				if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != FileAttributes.ReparsePoint)
				{
					if (item.Replace('/', Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Last() == path)
					{
						list.Add(item);
					}
					else
					{
						list2.Add(item);
					}
				}
			}
		}
		catch (Exception)
		{
		}
		foreach (string item2 in list2)
		{
			list.AddRange(FindSubdirectoriesRecursive(item2, path));
		}
		return list;
	}

	public virtual string GetDirectoryName(string path)
	{
		return Path.GetDirectoryName(path);
	}

	public virtual int GetFileAttributes(string path)
	{
		return (int)File.GetAttributes(path);
	}

	public virtual List<string> ListDirectories(string path)
	{
		if (PathExists(path))
		{
			return Directory.GetDirectories(path).ToList();
		}
		return new List<string>();
	}

	public virtual List<string> ListFiles(string path)
	{
		if (PathExists(path))
		{
			return Directory.GetFiles(path).ToList();
		}
		return new List<string>();
	}

	public virtual List<string> ListFilesRecursive(string path, string searchPattern = "*")
	{
		if (PathExists(path))
		{
			return Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories).ToList();
		}
		return new List<string>();
	}

	public virtual void Move(string sourcePath, string targetPath)
	{
		if (PathExists(sourcePath))
		{
			Directory.Move(sourcePath, targetPath);
		}
	}

	public virtual bool PathExists(string path)
	{
		if (!File.Exists(path))
		{
			return Directory.Exists(path);
		}
		return true;
	}

	public virtual bool CopyFile(string sourceFile, string targetFile, bool failIfExists = false)
	{
		if (File.Exists(sourceFile))
		{
			try
			{
				File.Copy(sourceFile, targetFile, !failIfExists);
				return true;
			}
			catch
			{
				return false;
			}
		}
		return false;
	}

	public virtual bool CopyDirectory(string sourceDir, string targetDir, bool recursive = true, bool failIfExists = false)
	{
		if (Directory.Exists(sourceDir))
		{
			try
			{
				CreateDirectory(targetDir);
				foreach (string item in ListFiles(sourceDir))
				{
					string fileName = Path.GetFileName(item);
					string targetFile = Path.Combine(targetDir, fileName);
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
						string targetDir2 = Path.Combine(targetDir, name);
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
}
