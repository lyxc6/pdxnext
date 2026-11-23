using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class DependencyCandidatesResponse : HTTPBodyBase
{
	[Preserve]
	public SimplifiedDependencyResponse[] mods { get; set; }
}
