using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal class MarkAsSubscribedResult : Result
{
	[Preserve]
	public IMod[] ModsSubscribed { get; set; }

	[Preserve]
	public int PlaysetId { get; set; }
}
