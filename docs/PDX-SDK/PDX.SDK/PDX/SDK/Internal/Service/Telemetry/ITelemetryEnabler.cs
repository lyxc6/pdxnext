using System.Threading.Tasks;

namespace PDX.SDK.Internal.Service.Telemetry;

internal interface ITelemetryEnabler
{
	bool IsEnabled { get; }

	Task SetAgeCheckStatus(bool bPositive);

	Task SetAllLegalDocumentsViewedDone();
}
