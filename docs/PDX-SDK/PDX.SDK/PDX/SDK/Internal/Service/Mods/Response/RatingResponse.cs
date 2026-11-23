using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class RatingResponse : HTTPBodyBase
{
	[Preserve]
	public int rating { get; set; }
}
