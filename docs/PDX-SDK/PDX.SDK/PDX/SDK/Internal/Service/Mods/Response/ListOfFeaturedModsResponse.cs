using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Internal.Service.Mods.Models;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ListOfFeaturedModsResponse : HTTPBodyBase
{
	[Preserve]
	public List<InternalListOfFeaturedModsItem> items { get; set; }

	[Preserve]
	public int count { get; set; }

	[Preserve]
	public int page { get; set; }

	[Preserve]
	public int limit { get; set; }
}
