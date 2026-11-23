using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Telemetry;
using PDX.SDK.Internal.Enums;
using PDX.SDK.Internal.Service.Telemetry;

namespace PDX.SDK.Internal.Util;

internal class AgeCheck
{
	private readonly Context _context;

	internal AgeCheck(Context internalContext)
	{
		_context = internalContext;
	}

	internal async Task<UserUnderAge> IsUserUnderAgeAsync(FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		_context.Log("AgeCheck.IsUserUnderAgeAsync", LogLevel.L1_Debug, flowData2);
		UserUnderAge underAgeCheckResult = UserUnderAge.Unknown;
		ITelemetryService telemetry = _context.Telemetry;
		bool ageCheckStatus = underAgeCheckResult != UserUnderAge.Yes;
		if (telemetry is TelemetryService)
		{
			await (telemetry as TelemetryService).SetAgeCheckStatus(ageCheckStatus).ConfigureAwait(continueOnCapturedContext: false);
		}
		return underAgeCheckResult;
	}
}
