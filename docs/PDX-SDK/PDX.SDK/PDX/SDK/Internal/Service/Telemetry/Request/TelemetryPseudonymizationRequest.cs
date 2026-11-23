using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Telemetry.Request;

[Preserve]
internal class TelemetryPseudonymizationRequest
{
	[Preserve]
	public string idType;

	[Preserve]
	public string thirdPartyId;
}
