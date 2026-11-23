using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Telemetry.Response;

[Preserve]
internal class TelemetryRejectedResponse
{
	[Preserve]
	public string field;

	[Preserve]
	public string value;
}
