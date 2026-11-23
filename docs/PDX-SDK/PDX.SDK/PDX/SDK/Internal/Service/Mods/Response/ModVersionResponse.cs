using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ModVersionResponse : HTTPBodyBase
{
	[Preserve]
	public List<VersionResponse> modVersions { get; set; }
}
