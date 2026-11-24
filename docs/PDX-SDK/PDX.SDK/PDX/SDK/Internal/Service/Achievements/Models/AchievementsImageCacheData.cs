using System.Collections.Generic;

namespace PDX.SDK.Internal.Service.Achievements.Models;

internal class AchievementsImageCacheData : IAchievementsImageData
{
	public List<CachedAchievementImageData> Images { get; set; } = new List<CachedAchievementImageData>();

	public string ImagedataEtag { get; set; }
}
