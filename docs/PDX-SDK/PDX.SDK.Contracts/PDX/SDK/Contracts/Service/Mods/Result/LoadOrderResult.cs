using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class LoadOrderResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<ModLoadOrder> OrderList { get; set; }

	[Preserve]
	public LoadOrderResult()
	{
		OrderList = new List<ModLoadOrder>();
	}
}
