using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Telemetry;
using PDX.SDK.Contracts.Service.Telemetry.Models;
using PDX.SDK.Contracts.Service.Telemetry.Result;
using PDX.SDK.Contracts.StandardConstantValues;
using PDX.SDK.Internal.Cache;
using PDX.SDK.Internal.Cache.Models;
using PDX.SDK.Internal.Service.Telemetry.Enums;
using PDX.SDK.Internal.Service.Telemetry.Request;
using PDX.SDK.Internal.Service.Telemetry.Response;
using PDX.SDK.Internal.Service.Telemetry.Result;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Enums;
using PDX.SDK.Util;

namespace PDX.SDK.Internal.Service.Telemetry;

internal class TelemetryService : InternalService, ITelemetryService
{
	private readonly List<RequiredTelemetryCall> _eventsQueuedWhileDisabled;

	private List<string> _whitelistedEventNames;

	public bool IsEnabled => TelemetryEnabler.IsEnabled;

	internal IPseudonymizationService PseudonymizationService { get; private set; }

	internal ITelemetryEnabler TelemetryEnabler { get; private set; }

	internal TelemetryService(Context context, IPseudonymizationService pseudonymizationService)
		: base("Telemetry", context)
	{
		_eventsQueuedWhileDisabled = new List<RequiredTelemetryCall>();
		_whitelistedEventNames = new List<string>();
		PseudonymizationService = pseudonymizationService;
		TelemetryEnabler = new TelemetryEnabler(this);
	}

	public Task<PDX.SDK.Contracts.Result> Start()
	{
		return Start(null);
	}

	public Task<PDX.SDK.Contracts.Result> Exit()
	{
		return Exit(null);
	}

	public Task<SendResult> Send<T>(IList<T> events) where T : IEvent
	{
		return Send(events, null);
	}

	public Task<SendResult> Send<T>(T @event) where T : IEvent
	{
		return Send(new List<T> { @event }, null);
	}

	internal async Task SendEventsQueuedWhileDisabled(FlowData fd)
	{
		_context.Log("Telemetry.SendEventsQueuedWhileDisabled", LogLevel.L0_Info);
		List<RequiredTelemetryCall> standardCallsCopy;
		lock (Context.StandardCallsLock)
		{
			standardCallsCopy = new List<RequiredTelemetryCall>(_eventsQueuedWhileDisabled);
			_eventsQueuedWhileDisabled.Clear();
		}
		await SendStandardTelemetry(standardCallsCopy, fd).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<EventsWhitelistResult> FetchWhitelist(FlowData fd)
	{
		EventsWhitelistResult eventsWhitelistResult = await GetEventsWhitelist(fd).ConfigureAwait(continueOnCapturedContext: false);
		if (eventsWhitelistResult.Success)
		{
			_whitelistedEventNames = eventsWhitelistResult.EventNames;
		}
		else if (eventsWhitelistResult.Error == Forbidden.TelemetryNotCollected)
		{
			_whitelistedEventNames = null;
		}
		return eventsWhitelistResult;
	}

	internal async Task<PseudonymizationResult> PerformPseudonymization()
	{
		PseudonymizationResult getPseudonymizedIdResult = await PseudonymizationService.GetPseudonymizedUserId().ConfigureAwait(continueOnCapturedContext: false);
		if (getPseudonymizedIdResult.Success)
		{
			await _context.SetTelemetryData(getPseudonymizedIdResult.PseudonymId, getPseudonymizedIdResult.PseudonymIdType).ConfigureAwait(continueOnCapturedContext: false);
		}
		return getPseudonymizedIdResult;
	}

	private async Task SendStandardTelemetry(List<RequiredTelemetryCall> standardCallsCopy, FlowData fd)
	{
		foreach (RequiredTelemetryCall c in standardCallsCopy)
		{
			PDX.SDK.Contracts.Result result = new PDX.SDK.Contracts.Result
			{
				Success = true
			};
			switch (c)
			{
			case RequiredTelemetryCall.Start:
				result = await Start(fd).ConfigureAwait(continueOnCapturedContext: false);
				break;
			case RequiredTelemetryCall.Exit:
				result = await Exit(fd).ConfigureAwait(continueOnCapturedContext: false);
				break;
			}
			if (!result.Success)
			{
				_context.Log(string.Format(Defaults.Culture, "Could not auto send Standard Telemetry {0}: \"{1}\"", c.ToString(), (result.Error != null) ? result.Error.Raw : ""), LogLevel.L3_Error, fd);
			}
		}
	}

	private double GetSessionLength()
	{
		return (DateTime.UtcNow - _context.SessionStart).TotalSeconds;
	}

	private async Task<PDX.SDK.Contracts.Result> Start(FlowData fd = null)
	{
		Dictionary<string, string> payload = new Dictionary<string, string> { { "loadtype", "cold" } };
		List<JsonEventBuilder> events = new JsonBuilder().AddEvent("start_game").SetPayload(payload).Build();
		return await SendRequiredEvent(RequiredTelemetryCall.Start, events, fd).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<PDX.SDK.Contracts.Result> Exit(FlowData fd = null)
	{
		Dictionary<string, object> payload = new Dictionary<string, object>
		{
			{ "exit_type", "exit" },
			{
				"session_length",
				GetSessionLength()
			}
		};
		List<JsonEventBuilder> events = new JsonBuilder().AddEvent("exit_game").SetPayload(payload).Build();
		return await SendRequiredEvent(RequiredTelemetryCall.Exit, events, fd).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<PDX.SDK.Contracts.Result> SendRequiredEvent(RequiredTelemetryCall telemetryCall, IList<JsonEventBuilder> events, FlowData fd = null)
	{
		lock (Context.StandardCallsLock)
		{
			if (!IsEnabled)
			{
				return OnTelemetryDisabledStandard(telemetryCall);
			}
		}
		Log($"Sending required telemetry event \"{telemetryCall}\"", LogLevel.L1_Debug, fd);
		return await SendTelemetryEvents(events, fd).ConfigureAwait(continueOnCapturedContext: false);
	}

	private PDX.SDK.Contracts.Result OnTelemetryDisabledStandard(RequiredTelemetryCall callType)
	{
		_eventsQueuedWhileDisabled.Add(callType);
		return new PDX.SDK.Contracts.Result
		{
			Success = false,
			Error = new Error(InvalidState.TelemetryDisabled, "Telemetry is disabled. This call will be sent when it is Enabled.", "")
		};
	}

	private async Task<SendResult> Send<T>(IList<T> events, FlowData flowData = null) where T : IEvent
	{
		if (!IsEnabled)
		{
			Error error = new Error(InvalidState.TelemetryDisabled, "Telemetry is disabled, enable it manually through the Login flow and viewing the Legal Documents", "");
			if (_whitelistedEventNames == null)
			{
				error = new Error(Forbidden.TelemetryNotCollected, "Telemetry is no longer collected for " + _context.Namespace + ". If it should not be the case, please contact the Data/Analytics team.", "");
			}
			return new SendResult
			{
				Success = false,
				Error = error
			};
		}
		FlowData flowData2 = new FlowData(flowData);
		Log("Telemetry.Send", LogLevel.L1_Debug, flowData2);
		List<T> obj = events?.Where((T e) => _whitelistedEventNames.Contains(e.Name)).ToList();
		List<T> rejectedEvents = events?.Where((T e) => !_whitelistedEventNames.Contains(e.Name)).ToList();
		new List<JsonEvent>();
		List<JsonEvent> events2 = obj?.ToEventsList();
		SendResult sendResult = await SendTelemetryEvents(events2, flowData2).ConfigureAwait(continueOnCapturedContext: false);
		if (_context.Config.TelemetryDebugEnabled == true)
		{
			foreach (T item in rejectedEvents)
			{
				sendResult.RejectedEvents.Add(new RejectedEvent
				{
					Field = item.Name,
					Error = "The event name " + item.Name + " is not whitelisted."
				});
			}
		}
		return sendResult;
	}

	internal async Task<SendResult> SendTelemetryEvents<T>(IList<T> events, FlowData flowData = null) where T : JsonEvent
	{
		FlowData fd = new FlowData(flowData);
		Log("Telemetry.SendTelemetryEvents", LogLevel.L0_Info, fd);
		if (events.Count < 1)
		{
			return new SendResult
			{
				Success = true,
				RejectedEvents = new List<RejectedEvent>(0)
			};
		}
		IPlayerData playerData = await _context.StateCache.GetPlayerData().ConfigureAwait(continueOnCapturedContext: false);
		TelemetrySendRequest telemetrySendRequest = new TelemetrySendRequest
		{
			paradox_account_id = playerData.UserId,
			user_id_type = _context.TelemetryUserIdType,
			user_id = _context.PseudonymizedId,
			app_session_id = _context.ApplicationSessionId,
			app_version = (_context.Config.GameVersion ?? ""),
			app_ecosystem = _context.Config.Ecosystem.ToEcosystemString(),
			app_checksum = _context.Config.GameChecksum,
			events = new TelemetryEventRequest[events.Count]
		};
		for (int i = 0; i < events.Count; i++)
		{
			telemetrySendRequest.events[i] = new TelemetryEventRequest
			{
				event_name = events[i].Name,
				event_schema_version = events[i].SchemaVersion,
				event_payload = events[i].Payload
			};
		}
		RequestBuilder requestBuilder = CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetHostType(HostTypes.HostType.Telemetry).AddToPath("v3")
			.AddNamespaceToPath()
			.SetBody(telemetrySendRequest)
			.SetContentType(ContentType.JSON);
		if (_context.Config.TelemetryDebugEnabled == true && _context.Config.Environment != BackendEnvironment.Live)
		{
			requestBuilder.AddHeader("x-pdx-development", "true");
		}
		PDXResponseParser<TelemetrySendResponse> pDXResponseParser = CreateResponseParser<TelemetrySendResponse>(await requestBuilder.Send().ConfigureAwait(continueOnCapturedContext: false), fd, hasJSONBody: true, strictHttpCode: true);
		TelemetrySendResponse response = pDXResponseParser.GetResponse();
		SendResult resultObject = pDXResponseParser.GetResultObject<SendResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		if (response != null && response.rejected?.Length > 0)
		{
			TelemetryRejectedResponse[] rejected = response.rejected;
			foreach (TelemetryRejectedResponse telemetryRejectedResponse in rejected)
			{
				resultObject.RejectedEvents.Add(new RejectedEvent
				{
					Field = telemetryRejectedResponse.field,
					Error = telemetryRejectedResponse.value
				});
			}
		}
		return resultObject;
	}

	internal async Task SetAgeCheckStatus(bool isUserNotUnderAge)
	{
		await TelemetryEnabler.SetAgeCheckStatus(isUserNotUnderAge).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task SetAllLegalDocumentsViewedDone()
	{
		await TelemetryEnabler.SetAllLegalDocumentsViewedDone().ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<bool> TrySetPseudonymizedUserIdFromCache()
	{
		PseudonymizedUserData pseudonymizedUserData = (await _context.StateCache.GetPlayerData().ConfigureAwait(continueOnCapturedContext: false)).PseudonymizedUserData;
		if (!string.IsNullOrEmpty(pseudonymizedUserData.PseudonymizedUserId))
		{
			await _context.SetTelemetryData(pseudonymizedUserData.PseudonymizedUserId, pseudonymizedUserData.IdType).ConfigureAwait(continueOnCapturedContext: false);
			return true;
		}
		return false;
	}

	internal async Task<EventsWhitelistResult> GetEventsWhitelist(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Telemetry.RetrieveEventsWhitelist", LogLevel.L0_Info, fd);
		PDXResponseParser<EventsWhitelistResponse> pDXResponseParser = CreateResponseParser<EventsWhitelistResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetHostType(HostTypes.HostType.Telemetry).SetPath("list")
			.AddNamespaceToPath()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd, hasJSONBody: true, strictHttpCode: true);
		EventsWhitelistResponse response = pDXResponseParser.GetResponse();
		EventsWhitelistResult resultObject = pDXResponseParser.GetResultObject<EventsWhitelistResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		if (response.events == null)
		{
			resultObject.Success = false;
			resultObject.Error = new Error(Forbidden.TelemetryNotCollected, "Telemetry is no longer relevant for " + _context.Namespace + ", it will be disabled", string.Empty);
			return resultObject;
		}
		resultObject.EventNames = new List<string>(response.events);
		return resultObject;
	}
}
