using System.Collections.Generic;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal class LoadUnmanagedModsResult : Result
{
	[Preserve]
	public List<IMod> ModsAdded { get; set; } = new List<IMod>();

	[Preserve]
	public List<IMod> ModsRemoved { get; set; } = new List<IMod>();
}
