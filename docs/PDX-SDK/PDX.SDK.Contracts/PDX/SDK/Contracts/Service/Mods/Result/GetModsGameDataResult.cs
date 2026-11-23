using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class GetModsGameDataResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public ModFilters Filters { get; set; }

	[Preserve]
	public GetModsGameDataResult()
	{
		Filters = new ModFilters();
	}
}
