using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Legal.Response;

[Preserve]
internal class LegalViewedResponse : HTTPBodyBase
{
	[Preserve]
	public string[] viewed { get; set; }
}
