using System.Collections.Generic;

namespace PDX.SDK.Internal.Service.Achievements.Models;

internal interface IAchievementsImageData
{
	List<CachedAchievementImageData> Images { get; }

	string ImagedataEtag { get; }
}
