using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Telemetry.Request;

[Preserve]
internal class TelemetryEventRequest
{
	[Preserve]
	public string event_name;

	[Preserve]
	public object event_payload;

	[Preserve]
	public string event_schema_version;
}
