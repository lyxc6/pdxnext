using System.Threading.Tasks;
using PDX.SDK.Internal.Service.Telemetry.Result;

namespace PDX.SDK.Internal.Service.Telemetry;

internal interface IPseudonymizationService
{
	Task<PseudonymizationResult> GetPseudonymizedUserId();
}
