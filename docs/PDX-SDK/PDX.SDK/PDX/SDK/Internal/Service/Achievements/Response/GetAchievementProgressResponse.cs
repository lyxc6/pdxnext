using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Achievements.Response;

[Preserve]
internal class GetAchievementProgressResponse : HTTPBodyBase
{
	[Preserve]
	public AchievementResponse achievement { get; set; }
}
