using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Achievements.Response;

[Preserve]
internal class GetMetadataResponse : HTTPBodyBase
{
	[Preserve]
	public Dictionary<string, MetadataResponse> achievements { get; set; }
}
