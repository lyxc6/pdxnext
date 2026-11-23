using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

internal class ModDependantsResponse : HTTPBodyBase
{
	[Preserve]
	public List<ModDependant> mods { get; set; }
}
