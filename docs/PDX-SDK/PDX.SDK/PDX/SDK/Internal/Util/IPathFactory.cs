using System.Threading.Tasks;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Configuration;
using PDX.SDK.Contracts.Service.Mods.Enums;

namespace PDX.SDK.Internal.Util;

[Preserve]
internal interface IPathFactory
{
	Task Configure(Config config);

	string GetAchievementsPath(string path = "");

	string GetCloudSavesPath(string path = "");

	string GetModsRootPath(string path = "");

	string GetModsPath(string path = "", LocalType type = LocalType.Subscribed);

	string GetModsMetadataPath(string path = "", LocalType type = LocalType.Subscribed);

	string GetModPlaysetsMetadataPath(string path = "");

	string GetJournalPath(string journalName);

	string GetPatchDownloadStagingPath(string path = "");

	string GetTempPath(string path = "");

	string GetPath(string path);

	string GetPathWithCurrentCacheRoot(string path = "");
}
