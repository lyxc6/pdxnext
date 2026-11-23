using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class ReportRequest
{
	[Preserve]
	public string modId;

	[Preserve]
	public string reason;

	[Preserve]
	public string description;
}
