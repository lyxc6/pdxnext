using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Telemetry.Result;

[Preserve]
internal class PseudonymizationResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public string PseudonymId { get; set; }

	[Preserve]
	public string PseudonymIdType { get; set; }
}
