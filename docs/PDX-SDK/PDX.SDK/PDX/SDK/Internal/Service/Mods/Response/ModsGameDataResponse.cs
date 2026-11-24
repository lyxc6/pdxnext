using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Internal.Service.Mods.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ModsGameDataResponse
{
	[Preserve]
	public InternalFilters filters { get; set; }
}
