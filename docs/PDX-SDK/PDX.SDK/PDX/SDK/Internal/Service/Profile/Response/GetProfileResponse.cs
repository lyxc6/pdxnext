using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Profile.Response;

[Preserve]
internal class GetProfileResponse : HTTPBodyBase
{
	[Preserve]
	public string guid { get; set; }

	[Preserve]
	public string franchise { get; set; }

	[Preserve]
	public string username { get; set; }

	[Preserve]
	public string isBanned { get; set; }

	[Preserve]
	public string developer { get; set; }
}
