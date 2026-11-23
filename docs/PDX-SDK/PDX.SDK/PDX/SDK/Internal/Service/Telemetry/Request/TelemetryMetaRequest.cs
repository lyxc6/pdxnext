using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Telemetry.Request;

[Preserve]
internal class TelemetryMetaRequest
{
	[Preserve]
	public string paradox_account_id;

	[Preserve]
	public string universe;

	[Preserve]
	public string user_id;

	[Preserve]
	public string application_session_id;

	[Preserve]
	public string application_version;

	[Preserve]
	public string platform_type;
}
