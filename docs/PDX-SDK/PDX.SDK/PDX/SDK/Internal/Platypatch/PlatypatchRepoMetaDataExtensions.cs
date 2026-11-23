using System.Linq;
using PDX.SDK.Contracts.Exceptions;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Platypatch.Models;

namespace PDX.SDK.Internal.Platypatch;

internal static class PlatypatchRepoMetaDataExtensions
{
	public static long CalculateSizeOfPatch(this RepoMetaData repoMetaData, string versionToInstall, string currentInstalledVersion = "-1", FlowData flowData = null)
	{
		new FlowData(flowData);
		Version version = null;
		Version version2 = null;
		if (versionToInstall != null && repoMetaData.Content.Versions.ContainsKey(versionToInstall))
		{
			version = repoMetaData.Content.Versions[versionToInstall];
		}
		if (currentInstalledVersion != null && repoMetaData.Content.Versions.ContainsKey(currentInstalledVersion))
		{
			version2 = repoMetaData.Content.Versions[currentInstalledVersion];
		}
		if ((version2 == null && currentInstalledVersion != "-1") || version == null)
		{
			throw new PlatypatchException("Supplied version/s not valid, currentInstalledVersion: " + currentInstalledVersion + " | versionToInstall: " + versionToInstall);
		}
		Patch patch = null;
		if (version.Patches.ContainsKey(currentInstalledVersion))
		{
			patch = version.Patches[currentInstalledVersion];
		}
		if (patch == null)
		{
			throw new PlatypatchException("Patch to install was null, versionToInstall: " + versionToInstall);
		}
		long num = patch.SizePacked + patch.SizeUnpacked;
		long num2 = version.CompleteSize - (version2?.CompleteSize ?? 0);
		long num3 = version.Dependencies?.Sum((Dependency d) => d.Exe.DependencySize) ?? 0;
		long valueOrDefault = (version2?.Dependencies?.Sum((Dependency d) => d.Exe.DependencySize)).GetValueOrDefault();
		long num4 = num3 - valueOrDefault;
		return num + num2 + num4;
	}
}
