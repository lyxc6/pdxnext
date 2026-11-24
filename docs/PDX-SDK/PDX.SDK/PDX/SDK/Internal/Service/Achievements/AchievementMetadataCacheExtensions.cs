using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Service.Achievements.Models;
using PDX.SDK.Internal.Service.Achievements.Models;

namespace PDX.SDK.Internal.Service.Achievements;

internal static class AchievementMetadataCacheExtensions
{
	internal static async Task<Dictionary<string, AchievementsMetadata>> GetAllMetadata(this IAchievementsMetadataCache metadataCache, IAchievementsImageCache imageCache)
	{
		IAchievementsMetadataData achievementData = await metadataCache.GetAchievementsData().ConfigureAwait(continueOnCapturedContext: false);
		IAchievementsImageData imageData = await imageCache.GetAchievementsImageData().ConfigureAwait(continueOnCapturedContext: false);
		return achievementData.Metadata.ToDictionary((KeyValuePair<string, CachedMetadata> x) => x.Key, (KeyValuePair<string, CachedMetadata> x) => new AchievementsMetadata
		{
			Name = x.Value.Name,
			Descriptions = x.Value.Descriptions,
			Hidden = x.Value.Hidden,
			ProgressMax = x.Value.ProgressMax,
			ProgressMin = x.Value.ProgressMin,
			Titles = x.Value.Titles,
			AchievedImage = (imageData.Images.FirstOrDefault((CachedAchievementImageData i) => i.Id == x.Key && i.Achieved)?.BinaryData ?? new byte[0]),
			UnachievedImage = (imageData.Images.FirstOrDefault((CachedAchievementImageData i) => i.Id == x.Key && !i.Achieved)?.BinaryData ?? new byte[0])
		});
	}
}
