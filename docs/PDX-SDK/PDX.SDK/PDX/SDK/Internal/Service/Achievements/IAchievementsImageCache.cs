using System.Threading.Tasks;
using PDX.SDK.Internal.Cache.Models;
using PDX.SDK.Internal.Service.Achievements.Models;

namespace PDX.SDK.Internal.Service.Achievements;

internal interface IAchievementsImageCache
{
	Task Init();

	Task<IAchievementsImageData> GetAchievementsImageData();

	Task<UpdateResult<IAchievementsMetadataData>> SetImageDataEtag(string etag);

	Task<UpdateResult<IAchievementsImageData>> SetImages(string serverData);
}
