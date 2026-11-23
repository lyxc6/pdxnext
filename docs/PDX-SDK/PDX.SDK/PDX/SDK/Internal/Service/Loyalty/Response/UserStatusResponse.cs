using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Loyalty.Response;

[Preserve]
internal class UserStatusResponse : HTTPBodyBase
{
	[Preserve]
	public List<CampaignResponse> campaigns { get; set; }

	[Preserve]
	public List<ThreasholdReachedResponse> userThresholdsReached { get; set; }
}
