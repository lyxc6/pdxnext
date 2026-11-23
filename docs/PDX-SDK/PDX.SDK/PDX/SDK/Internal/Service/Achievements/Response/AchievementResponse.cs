using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Achievements.Response;

[Preserve]
internal class AchievementResponse
{
	[Preserve]
	public bool isAchieved { get; set; }

	[Preserve]
	public string achieved { get; set; }

	[Preserve]
	public string updated { get; set; }

	[Preserve]
	public int progress { get; set; }
}
