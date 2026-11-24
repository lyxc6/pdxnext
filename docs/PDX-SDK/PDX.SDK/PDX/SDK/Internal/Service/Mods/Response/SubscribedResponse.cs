using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class SubscribedResponse : HTTPBodyBase
{
	[Preserve]
	public SubscribedModResponse[] mods { get; set; }

	[Preserve]
	public string latestUpdate { get; set; }
}
