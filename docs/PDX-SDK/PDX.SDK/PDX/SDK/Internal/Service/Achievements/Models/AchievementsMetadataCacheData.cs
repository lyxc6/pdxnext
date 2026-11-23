using System.Collections.Generic;

namespace PDX.SDK.Internal.Service.Achievements.Models;

internal class AchievementsMetadataCacheData : IAchievementsMetadataData
{
	public Dictionary<string, CachedMetadata> Metadata { get; set; } = new Dictionary<string, CachedMetadata>();

	public string MetadataEtag { get; set; }
}
