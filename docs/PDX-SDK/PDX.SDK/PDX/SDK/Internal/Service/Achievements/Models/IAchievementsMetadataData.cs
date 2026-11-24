using System.Collections.Generic;

namespace PDX.SDK.Internal.Service.Achievements.Models;

internal interface IAchievementsMetadataData
{
	Dictionary<string, CachedMetadata> Metadata { get; }

	string MetadataEtag { get; }
}
