using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Generic;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class GetGameDataResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public string Name { get; set; }

	[Preserve]
	public string Extension { get; set; }

	[Preserve]
	public Dictionary<string, ITag> Tags { get; set; }

	[Preserve]
	public GameActions Actions { get; set; }

	[Preserve]
	public ModFilters Filters { get; set; }

	[Preserve]
	public GetGameDataResult()
	{
		Tags = new Dictionary<string, ITag>();
		Filters = new ModFilters();
	}
}
