using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Service.Telemetry.Request;
using PDX.SDK.Internal.Service.Telemetry.Response;
using PDX.SDK.Internal.Service.Telemetry.Result;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Enums;

namespace PDX.SDK.Internal.Service.Telemetry;

internal class PseudonymizationService : InternalService, IPseudonymizationService
{
	private const string XboxIdType = "xsts_token";

	internal PseudonymizationService(Context context)
		: base("Pseudonymization", context)
	{
	}

	public async Task<PseudonymizationResult> GetPseudonymizedUserId()
	{
		return await GetPseudonymizedUserId(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<PseudonymizationResult> GetPseudonymizedUserId(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Telemetry.GetPseudonymizedUserId", LogLevel.L1_Debug, fd);
		TelemetryPseudonymizationRequest telemetryPseudonymizationRequest = await RetrievePseudonymizationRequestParameter(fd).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrEmpty(telemetryPseudonymizationRequest.idType) || string.IsNullOrEmpty(telemetryPseudonymizationRequest.thirdPartyId))
		{
			Log("No valid user id could be retrieved, return before pseudonymization call.", LogLevel.L3_Error);
			return new PseudonymizationResult
			{
				Success = false,
				Error = new Error(InvalidInput.MissingParameter, "Failed to get a pseudonymized id because the user id or id type is missing.", string.Empty)
			};
		}
		return await PerformGetPseudonymizedUserId(telemetryPseudonymizationRequest, fd).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<PseudonymizationResult> PerformGetPseudonymizedUserId(TelemetryPseudonymizationRequest requestBody, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Telemetry.PerformGetPseudonymizedUserId", LogLevel.L0_Info, fd);
		PDXResponseParser<PseudonymizationResponse> pDXResponseParser = CreateResponseParser<PseudonymizationResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetHostType(HostTypes.HostType.Telemetry).SetPath("/pseudonymise/single")
			.SetBody(requestBody)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd, hasJSONBody: true, strictHttpCode: true);
		PseudonymizationResponse response = pDXResponseParser.GetResponse();
		PseudonymizationResult resultObject = pDXResponseParser.GetResultObject<PseudonymizationResult>();
		if (!resultObject.Success || response == null || string.IsNullOrEmpty(response.pseudonymId))
		{
			resultObject.Success = false;
			if (resultObject.Error == null)
			{
				resultObject.Error = new Error(InternalError.InternalError, "Could not pseudonymize the user id", string.Empty);
			}
			return resultObject;
		}
		resultObject.PseudonymId = response.pseudonymId;
		resultObject.PseudonymIdType = requestBody.idType;
		if (requestBody.idType == "xsts_token")
		{
			resultObject.PseudonymIdType = "xbox_xsts";
		}
		return resultObject;
	}

	internal async Task<TelemetryPseudonymizationRequest> RetrievePseudonymizationRequestParameter(FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Telemetry.RetrievePseudonymizationRequestParameter", LogLevel.L0_Info, flowData2);
		string userId = _context.Config.UserId;
		string userIdType = _context.Config.UserIdType;
		return GetPseudonymizationRequestBody(userId, userIdType);
	}

	private TelemetryPseudonymizationRequest GetPseudonymizationRequestBody(string userId, string idType)
	{
		if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(idType))
		{
			return RetrieveFallbackUserIdAndIdType();
		}
		return new TelemetryPseudonymizationRequest
		{
			idType = idType,
			thirdPartyId = userId
		};
	}

	private TelemetryPseudonymizationRequest RetrieveFallbackUserIdAndIdType()
	{
		return new TelemetryPseudonymizationRequest
		{
			idType = "sdk_generated_id",
			thirdPartyId = (_context.MachineUId ?? _context.MachineGeneratedUId)
		};
	}
}
