using System.Collections.Generic;
using System.IO;

namespace PDX.SDK.Contracts.Util;

public interface IDiskIO
{
	bool PathExists(string path);

	string GetDirectoryName(string path);

	void Delete(string path);

	void DeleteDirectory(string path, bool recursive);

	void CreateDirectory(string path);

	bool CopyFile(string sourceFile, string targetFile, bool failIfExists = false);

	bool CopyDirectory(string sourceDir, string targetDir, bool recursive = true, bool failIfExists = false);

	List<string> ListDirectories(string path);

	List<string> FindSubdirectoriesRecursive(string root, string path);

	List<string> ListFiles(string path);

	List<string> ListFilesRecursive(string path, string searchPattern = "*");

	void Move(string sourcePath, string targetPath);

	Stream CreateFileStream(string path, FileMode fileMode, FileAccess fileAccess, FileShare fileShare);

	int GetFileAttributes(string path);
}
