using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class GetModCreatorsProfileResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public ModCreator CreatorProfile { get; set; }
}
