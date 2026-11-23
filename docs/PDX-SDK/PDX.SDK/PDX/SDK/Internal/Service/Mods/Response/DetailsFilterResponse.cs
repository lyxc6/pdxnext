using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class DetailsFilterResponse : HTTPBodyBase
{
	[Preserve]
	public ModDetailsResponse modDetail { get; set; }
}
