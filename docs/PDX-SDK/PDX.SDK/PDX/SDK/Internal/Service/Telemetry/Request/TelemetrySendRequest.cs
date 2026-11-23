using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Telemetry.Request;

[Preserve]
internal class TelemetrySendRequest
{
	[Preserve]
	public string app_ecosystem;

	[Preserve]
	public string user_id_type;

	[Preserve]
	public string user_id;

	[Preserve]
	public string paradox_account_id;

	[Preserve]
	public string app_version;

	[Preserve]
	public string app_checksum;

	[Preserve]
	public string app_session_id;

	[Preserve]
	public TelemetryEventRequest[] events;
}
