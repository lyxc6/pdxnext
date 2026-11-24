using System.IO;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Service.Mods.Helpers;

internal static class ModPath
{
	internal static string GetModFolderName(int modId, string version, bool tempFolder = false, ILogger logger = null)
	{
		if (modId <= 0 || string.IsNullOrWhiteSpace(version))
		{
			logger?.Log($"GetModFolderName was called with invalid values modId={modId} version={version}!", LogLevel.L2_Warning);
			return null;
		}
		string text = $"{modId}{'_'}{version}";
		if (tempFolder)
		{
			text = string.Format("{0}{1}{2}", "Temp", '_', text);
		}
		return text;
	}

	internal static string GetPlaysetModsJournalName(string journalName, int playsetId)
	{
		return $"{playsetId}_{journalName}";
	}

	internal static bool VerifyModResourceAbsolutePath(IFileIO fileIO, string modPath, string resourceFileName, out string absolutePath)
	{
		string text = Path.Combine(modPath, ".metadata", resourceFileName);
		if (fileIO.PathExists(text))
		{
			absolutePath = text;
			return true;
		}
		string text2 = Path.Combine(modPath, resourceFileName);
		if (fileIO.PathExists(text2))
		{
			absolutePath = text2;
			return true;
		}
		absolutePath = null;
		return false;
	}
}
