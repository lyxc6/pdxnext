using System.Threading.Tasks;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Telemetry;

namespace PDX.SDK.Internal.Service.Telemetry;

internal class TelemetryEnabler : ITelemetryEnabler
{
	private bool? _ageCheckStatus;

	private bool? _documentsViewed;

	private bool _pseudonymizationDone;

	private readonly ITelemetryService _telemetryService;

	public bool IsEnabled { get; private set; }

	public async Task SetAgeCheckStatus(bool bSuccess)
	{
		await SetAgeCheckStatus(bSuccess, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task SetAllLegalDocumentsViewedDone()
	{
		await SetAllLegalDocumentsViewedDone(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task SetAgeCheckStatus(bool bPositive, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		_ageCheckStatus = bPositive;
		if (_documentsViewed.HasValue && _documentsViewed.Value)
		{
			await PerformPseudonymizationAndWhitelistingSteps(fd).ConfigureAwait(continueOnCapturedContext: true);
		}
	}

	internal async Task SetAllLegalDocumentsViewedDone(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		_documentsViewed = true;
		if (_ageCheckStatus.HasValue && _ageCheckStatus.Value)
		{
			await PerformPseudonymizationAndWhitelistingSteps(fd).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private async Task PerformPseudonymizationAndWhitelistingSteps(FlowData fd = null)
	{
		if (_pseudonymizationDone)
		{
			return;
		}
		_pseudonymizationDone = true;
		if (_telemetryService is TelemetryService)
		{
			bool flag = (await (_telemetryService as TelemetryService).PerformPseudonymization().ConfigureAwait(continueOnCapturedContext: false)).Success;
			if (!flag)
			{
				flag = await (_telemetryService as TelemetryService).TrySetPseudonymizedUserIdFromCache().ConfigureAwait(continueOnCapturedContext: false);
			}
			if (flag && (await (_telemetryService as TelemetryService).FetchWhitelist(fd).ConfigureAwait(continueOnCapturedContext: false)).Success)
			{
				await EnableTelemetry(fd).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		else
		{
			IsEnabled = true;
		}
	}

	private async Task EnableTelemetry(FlowData fd)
	{
		IsEnabled = true;
		await (_telemetryService as TelemetryService).SendEventsQueuedWhileDisabled(fd).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal TelemetryEnabler(ITelemetryService telemetryService)
	{
		_telemetryService = telemetryService;
		IsEnabled = false;
		_pseudonymizationDone = false;
	}
}
