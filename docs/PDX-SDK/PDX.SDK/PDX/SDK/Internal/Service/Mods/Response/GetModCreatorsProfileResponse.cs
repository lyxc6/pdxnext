using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

internal class GetModCreatorsProfileResponse : HTTPBodyBase
{
	[Preserve]
	public ModCreatorResponse profile { get; set; }
}
