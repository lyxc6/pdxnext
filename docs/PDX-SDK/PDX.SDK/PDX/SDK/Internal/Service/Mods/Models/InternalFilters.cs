using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Models;

[Preserve]
internal class InternalFilters
{
	[Preserve]
	public List<InternalModNode> tags { get; set; }
}
