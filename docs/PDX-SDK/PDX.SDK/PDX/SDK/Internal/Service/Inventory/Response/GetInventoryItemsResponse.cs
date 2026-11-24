using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Inventory.Response;

[Preserve]
internal class GetInventoryItemsResponse : HTTPBodyBase
{
	[Preserve]
	public Dictionary<string, Dictionary<string, string>> items { get; set; }
}
