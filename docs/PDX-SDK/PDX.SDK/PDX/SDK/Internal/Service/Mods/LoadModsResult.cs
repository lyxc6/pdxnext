using System.Collections.Generic;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal class LoadModsResult : Result
{
	[Preserve]
	public Dictionary<string, IMod> Mods { get; set; } = new Dictionary<string, IMod>();
}
