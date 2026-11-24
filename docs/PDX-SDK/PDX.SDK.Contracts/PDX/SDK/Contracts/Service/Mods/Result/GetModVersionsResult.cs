using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class GetModVersionsResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<ModVersionResult> Versions { get; set; }
}
