using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class BulkResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<string> Failures { get; set; }
}
