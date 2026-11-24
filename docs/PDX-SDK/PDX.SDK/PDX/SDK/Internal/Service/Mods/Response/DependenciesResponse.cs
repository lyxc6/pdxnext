using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class DependenciesResponse : HTTPBodyBase
{
	[Preserve]
	public DependencyResponse[] mods { get; set; }
}
