using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Achievements.Response;

[Preserve]
internal class GetAllAchievementProgressResponse : HTTPBodyBase
{
	[Preserve]
	public Dictionary<string, AchievementResponse> achievements { get; set; }
}
