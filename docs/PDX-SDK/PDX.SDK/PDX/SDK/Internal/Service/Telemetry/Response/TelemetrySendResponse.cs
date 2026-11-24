using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Telemetry.Response;

[Preserve]
internal class TelemetrySendResponse : HTTPBodyBase
{
	[Preserve]
	public int accepted;

	[Preserve]
	public TelemetryRejectedResponse[] rejected;
}
