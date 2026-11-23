using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Loyalty;
using PDX.SDK.Contracts.Service.Loyalty.Enums;
using PDX.SDK.Contracts.Service.Loyalty.Models;
using PDX.SDK.Contracts.Service.Loyalty.Result;
using PDX.SDK.Internal.Service.Loyalty.Request;
using PDX.SDK.Internal.Service.Loyalty.Response;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Enums;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Loyalty;

internal class LoyaltyService : InternalService, ILoyaltyService
{
	internal LoyaltyService(Context context)
		: base("Loyalty", context)
	{
	}

	public async Task<Result> RegisterEvent(string @event, string type)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("register loyalty event");
		}
		FlowData flowData = new FlowData();
		Log("Loyalty.RegisterEvent", LogLevel.L1_Debug, flowData);
		RegisterLoyaltyEventRequest registerRequest = new RegisterLoyaltyEventRequest
		{
			@namespace = _context.Namespace,
			@event = @event,
			type = type
		};
		return await PerformRegisterEvent(registerRequest, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<UserPointsResult> GetPoints(string pointType = null)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<UserPointsResult>("get loyalty user points");
		}
		FlowData flowData = new FlowData();
		Log("Loyalty.GetUserPoints", LogLevel.L1_Debug, flowData);
		return await PerformGetUserPoints(pointType, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<LoyaltyStatusResult> GetStatus(string @event = null, string type = null)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<LoyaltyStatusResult>("get loyalty user status");
		}
		FlowData flowData = new FlowData();
		Log("Loyalty.GetUserStatus", LogLevel.L1_Debug, flowData);
		return await PerformGetStatus(@event, type, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<Result> PerformRegisterEvent(RegisterLoyaltyEventRequest registerRequest, FlowData fd = null)
	{
		FlowData flowData = fd ?? new FlowData();
		return CreateResponseParser(await CreateRequestBuilder(flowData).SetRestMethod(RESTMethod.PUT).SetPath("loyalty/event").SetAuthHeader()
			.SetBody(registerRequest)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), flowData).GetResultObject();
	}

	private async Task<UserPointsResult> PerformGetUserPoints(string pointType = null, FlowData flowData = null)
	{
		PDX.SDK.Network.Models.Response response = await CreateRequestBuilder(flowData).SetRestMethod(RESTMethod.GET).SetPath("loyalty/points/v2").SetAuthHeader()
			.AddNamespaceToPath()
			.AddToPath(pointType)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false);
		if (pointType == null)
		{
			PDXResponseParser<UserPointsResponse> pDXResponseParser = CreateResponseParser<UserPointsResponse>(response, flowData);
			UserPointsResponse response2 = pDXResponseParser.GetResponse();
			UserPointsResult resultObject = pDXResponseParser.GetResultObject<UserPointsResult>();
			resultObject.Points = new Dictionary<string, int>();
			if (!resultObject.Success)
			{
				return resultObject;
			}
			foreach (PointResponse point in response2.points)
			{
				resultObject.Points.Add(point.pointType, point.sum);
			}
			return resultObject;
		}
		PDXResponseParser<UserPointResponse> pDXResponseParser2 = CreateResponseParser<UserPointResponse>(response, flowData);
		UserPointResponse response3 = pDXResponseParser2.GetResponse();
		UserPointsResult resultObject2 = pDXResponseParser2.GetResultObject<UserPointsResult>();
		resultObject2.Points = new Dictionary<string, int>();
		resultObject2.Points.Add(response3.pointType, response3.sum);
		return resultObject2;
	}

	private async Task<LoyaltyStatusResult> PerformGetStatus(string @event, string type, FlowData flowData = null)
	{
		PDXResponseParser<UserStatusResponse> pDXResponseParser = CreateResponseParser<UserStatusResponse>(await CreateRequestBuilder(flowData).SetRestMethod(RESTMethod.GET).SetPath("loyalty/user").SetAuthHeader()
			.AddParameterToPath("namespace", _context.Namespace)
			.AddParameterToPath("event", @event)
			.AddParameterToPath("type", type)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), flowData);
		UserStatusResponse response = pDXResponseParser.GetResponse();
		LoyaltyStatusResult resultObject = pDXResponseParser.GetResultObject<LoyaltyStatusResult>();
		resultObject.Campaigns = new List<Campaign>();
		resultObject.ThresholdsReached = new List<ThresholdReached>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.Campaigns = response.campaigns.Select((CampaignResponse campaign) => new Campaign
		{
			Name = campaign.name,
			Description = campaign.description,
			Start = ParseDateTime(campaign.startDate, "Loyalty.UserStatus", "campaign.start", flowData),
			End = ParseDateTime(campaign.endDate, "Loyalty.UserStatus", "campaign.end", flowData),
			Rules = campaign.rules.Select((RuleResponse rule) => new Rule
			{
				Description = rule.description,
				Event = rule.@event,
				Type = rule.type,
				Metadata = rule.metadata,
				Rewards = rule.rewards.Select((RewardResponse reward) => new Reward
				{
					Received = reward.received,
					Type = Enum<RewardType>.ParseOrDefault(reward.type)
				}).ToList()
			}).ToList()
		}).ToList();
		if (response.userThresholdsReached != null)
		{
			resultObject.ThresholdsReached = response.userThresholdsReached.Select((ThreasholdReachedResponse thresholdReached) => new ThresholdReached
			{
				PointType = thresholdReached.pointType,
				Thresholds = thresholdReached.thresholds.Select((ThresholdResponse threshold) => new Threshold
				{
					Limit = threshold.limit,
					Rewards = threshold.rewards.Select((RewardResponse reward) => new Reward
					{
						Received = reward.received,
						Type = Enum<RewardType>.ParseOrDefault(reward.type)
					}).ToList()
				}).ToList()
			}).ToList();
		}
		return resultObject;
	}
}
