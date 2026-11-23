using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

internal class GetFollowedModCreatorsResponse : HTTPBodyBase
{
	[Preserve]
	public ModCreatorResponse[] creators { get; set; }
}
