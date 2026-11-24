using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Contracts.Service.Achievements.Models;
using PDX.SDK.Internal.Cache;
using PDX.SDK.Internal.Cache.Models;
using PDX.SDK.Internal.Enums;
using PDX.SDK.Internal.Service.Achievements.Models;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Service.Achievements;

internal class AchievementsMetadataCache : CacheBase<AchievementsMetadataCacheData>, IAchievementsMetadataCache
{
	public AchievementsMetadataCache(IFileIO fileIo, ILogger logger = null)
		: base(fileIo, FilePath.AchievementsMetadata, logger)
	{
	}

	public Task<IAchievementsMetadataData> GetAchievementsData()
	{
		return ObtainReadlockAndReturnData((IAchievementsMetadataData)base.CachedData);
	}

	public Task<UpdateResult<IAchievementsMetadataData>> ClearCache()
	{
		return Update<IAchievementsMetadataData>(delegate(AchievementsMetadataCacheData state)
		{
			state.MetadataEtag = null;
			state.Metadata.Clear();
		});
	}

	public Task<UpdateResult<IAchievementsMetadataData>> SetAllMetadata(Dictionary<string, AchievementsMetadata> metadata)
	{
		return Update<IAchievementsMetadataData>(delegate(AchievementsMetadataCacheData state)
		{
			state.Metadata = metadata.ToDictionary((KeyValuePair<string, AchievementsMetadata> x) => x.Key, (KeyValuePair<string, AchievementsMetadata> x) => new CachedMetadata
			{
				Name = x.Value.Name,
				Hidden = x.Value.Hidden,
				Descriptions = x.Value.Descriptions,
				ProgressMax = x.Value.ProgressMax.GetValueOrDefault(),
				ProgressMin = x.Value.ProgressMin.GetValueOrDefault(),
				Titles = x.Value.Titles
			});
		});
	}

	public Task<UpdateResult<IAchievementsMetadataData>> SetMetadataEtag(string etag)
	{
		return Update<IAchievementsMetadataData>(delegate(AchievementsMetadataCacheData state)
		{
			state.MetadataEtag = etag;
		});
	}
}
