using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class SearchResponse : HTTPBodyBase
{
	[Preserve]
	public SearchModResponse[] mods { get; set; }

	[Preserve]
	public int totalCount { get; set; }
}
