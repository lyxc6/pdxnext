using System.Collections.Generic;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Service.Achievements.Models;
using PDX.SDK.Internal.Cache.Models;
using PDX.SDK.Internal.Service.Achievements.Models;

namespace PDX.SDK.Internal.Service.Achievements;

internal interface IAchievementsMetadataCache
{
	Task Init();

	Task<IAchievementsMetadataData> GetAchievementsData();

	Task<UpdateResult<IAchievementsMetadataData>> ClearCache();

	Task<UpdateResult<IAchievementsMetadataData>> SetAllMetadata(Dictionary<string, AchievementsMetadata> metadata);

	Task<UpdateResult<IAchievementsMetadataData>> SetMetadataEtag(string etag);
}
