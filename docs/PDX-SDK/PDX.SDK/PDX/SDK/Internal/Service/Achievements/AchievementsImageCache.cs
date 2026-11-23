using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Internal.Cache;
using PDX.SDK.Internal.Cache.Models;
using PDX.SDK.Internal.Enums;
using PDX.SDK.Internal.Service.Achievements.Models;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Service.Achievements;

internal class AchievementsImageCache : CacheBase<AchievementsImageCacheData>, IAchievementsImageCache
{
	public AchievementsImageCache(IFileIO fileIo, ILogger logger = null)
		: base(fileIo, FilePath.AchievementsImagedata, logger)
	{
	}

	public Task<IAchievementsImageData> GetAchievementsImageData()
	{
		return ObtainReadlockAndReturnData((IAchievementsImageData)base.CachedData);
	}

	public Task<UpdateResult<IAchievementsImageData>> SetImages(string serverData)
	{
		return Update<IAchievementsImageData>(delegate(AchievementsImageCacheData state)
		{
			state.Images = (from line in serverData.Split('\n')
				select line.Split('\t') into row
				where row.Length == 3
				select new CachedAchievementImageData
				{
					Id = row[0].Trim(),
					Achieved = (row[1].Trim() == "1"),
					Base64Data = row[2].Trim()
				}).ToList();
		});
	}

	public Task<UpdateResult<IAchievementsMetadataData>> SetImageDataEtag(string etag)
	{
		return Update<IAchievementsMetadataData>(delegate(AchievementsImageCacheData state)
		{
			state.ImagedataEtag = etag;
		});
	}
}
