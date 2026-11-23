using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Telemetry.Response;

[Preserve]
internal class PseudonymizationResponse : HTTPBodyBase
{
	[Preserve]
	public string pseudonymId { get; set; }
}
