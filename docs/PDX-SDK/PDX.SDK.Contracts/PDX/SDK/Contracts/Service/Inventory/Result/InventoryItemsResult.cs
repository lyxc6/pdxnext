using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Inventory.Result;

[Preserve]
public class InventoryItemsResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public Dictionary<string, string> InventoryItems { get; set; }
}
