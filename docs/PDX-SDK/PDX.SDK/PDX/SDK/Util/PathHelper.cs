using System.IO;

namespace PDX.SDK.Util;

public static class PathHelper
{
	internal static string SanitizePath(string path)
	{
		string[] array = path.Replace('/', Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
		string[] array2 = new string[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array2[i] = string.Join("_", array[i].Split(Path.GetInvalidFileNameChars()));
		}
		return Path.Combine(array2);
	}
}
