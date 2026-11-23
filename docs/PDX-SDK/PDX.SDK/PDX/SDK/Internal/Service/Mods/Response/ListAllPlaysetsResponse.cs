using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ListAllPlaysetsResponse : HTTPBodyBase
{
	[Preserve]
	public ListPlaysetResponse[] sets { get; set; }
}
