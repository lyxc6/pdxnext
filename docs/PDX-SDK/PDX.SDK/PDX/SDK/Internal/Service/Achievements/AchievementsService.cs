using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Achievements;
using PDX.SDK.Contracts.Service.Achievements.Models;
using PDX.SDK.Contracts.Service.Achievements.Result;
using PDX.SDK.Contracts.StandardConstantValues;
using PDX.SDK.Internal.Service.Achievements.Request;
using PDX.SDK.Internal.Service.Achievements.Response;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Enums;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Achievements;

internal class AchievementsService : InternalService, IAchievementsService
{
	internal IAchievementsImageCache AchievementsImageCache { get; private set; }

	internal IAchievementsMetadataCache AchievementsDataCache { get; private set; }

	public AchievementsService(Context context, IAchievementsMetadataCache achievementsDataCache, IAchievementsImageCache achievementsImageCache)
		: base("Achievements", context)
	{
		AchievementsDataCache = achievementsDataCache;
		AchievementsImageCache = achievementsImageCache;
	}

	public async Task<Result> Register(Dictionary<string, ProgressModel> achievements)
	{
		return await Register(achievements, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Register(string id, ProgressModel progress)
	{
		return await Register(new Dictionary<string, ProgressModel> { { id, progress } }).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ListResult> List()
	{
		return await List(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<GetAllMetadataResult> GetAllMetadata()
	{
		return await GetAllMetadata(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<GetAllProgressResult> GetAllProgress()
	{
		return await GetAllProgress(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<Result> Register(Dictionary<string, ProgressModel> achievements, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Achievements.Register", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("register progress for an achievement");
		}
		List<RegisterUserDataRequest> list = new List<RegisterUserDataRequest>();
		foreach (KeyValuePair<string, ProgressModel> achievement in achievements)
		{
			list.Add(new RegisterUserDataRequest
			{
				id = achievement.Key,
				isAchieved = achievement.Value.IsAchieved,
				progress = achievement.Value.Value.GetValueOrDefault()
			});
		}
		PDXResponseParser<RegisterUserDataResponse> pDXResponseParser = CreateResponseParser<RegisterUserDataResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PUT).SetPath("achievements/userdata").AddNamespaceToPath()
			.SetAuthHeader()
			.SetBody(list)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		RegisterUserDataResponse response = pDXResponseParser.GetResponse();
		Result resultObject = pDXResponseParser.GetResultObject();
		if (!resultObject.Success || response.data == null || response.data.Count == 0)
		{
			return resultObject;
		}
		List<string> list2 = new List<string>();
		foreach (KeyValuePair<string, ProgressModel> achievement2 in achievements)
		{
			if (response.data[achievement2.Key]?.ToString() != "OK")
			{
				list2.Add(achievement2.Key);
			}
		}
		if (list2.Count > 0)
		{
			resultObject.Success = false;
			resultObject.Error = new Error(InternalError.InternalError, string.Format(Defaults.Culture, "The following achievements failed to register in the backend: {0}", string.Join(",", list2)), "");
		}
		return resultObject;
	}

	internal async Task<ListResult> List(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Achievements.List", LogLevel.L1_Debug, fd);
		GetAllMetadataResult getMetadata = await GetAllMetadata(fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!getMetadata.Success)
		{
			return getMetadata.CastTo<ListResult>();
		}
		GetAllProgressResult getAllProgressResult = await GetAllProgress(fd).ConfigureAwait(continueOnCapturedContext: false);
		Dictionary<string, Achievement> dictionary = new Dictionary<string, Achievement>();
		foreach (KeyValuePair<string, AchievementsMetadata> metadatum in getMetadata.Metadata)
		{
			Achievement achievement = new Achievement
			{
				Metadata = metadatum.Value
			};
			if (getAllProgressResult.Progress != null && getAllProgressResult.Progress.ContainsKey(metadatum.Key))
			{
				achievement.Progress = getAllProgressResult.Progress[metadatum.Key];
			}
			dictionary.Add(metadatum.Key, achievement);
		}
		return new ListResult
		{
			Success = true,
			Achievements = dictionary
		};
	}

	private async Task<GetAllProgressResult> GetAllProgress(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Achievements.GetAllProgress", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<GetAllProgressResult>("get all achievements progress");
		}
		PDXResponseParser<GetAllAchievementProgressResponse> pDXResponseParser = CreateResponseParser<GetAllAchievementProgressResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("achievements/userdata").AddNamespaceToPath()
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		GetAllAchievementProgressResponse response = pDXResponseParser.GetResponse();
		GetAllProgressResult resultObject = pDXResponseParser.GetResultObject<GetAllProgressResult>();
		if (!resultObject.Success || response.achievements == null || response.achievements.Count == 0)
		{
			if (resultObject.Error == NotFound.NoResource)
			{
				resultObject.Error = new Error(NotFound.NoAchievement, "No achievement progress has been registered for the current user", "");
			}
			return resultObject;
		}
		resultObject.Progress = new Dictionary<string, ProgressModel>();
		foreach (KeyValuePair<string, AchievementResponse> achievement in response.achievements)
		{
			ProgressModel value = new ProgressModel
			{
				IsAchieved = achievement.Value.isAchieved,
				UpdatedDate = ((achievement.Value.updated != null) ? ParseDateTime(achievement.Value.updated, "Achievements.PerformGetProgressCall", "achievement.updated", fd) : ((DateTime?)null)),
				AchievedDate = ((achievement.Value.achieved != null) ? ParseDateTime(achievement.Value.achieved, "Achievements.PerformGetProgressCall", "achievement.achieved", fd) : ((DateTime?)null)),
				Value = achievement.Value.progress
			};
			resultObject.Progress.Add(achievement.Key, value);
		}
		return resultObject;
	}

	private async Task<GetAllMetadataResult> GetAllMetadata(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Achievements.GetAllMetadata", LogLevel.L1_Debug, fd);
		Result result = await GetMetadata(fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.Success)
		{
			return result.CastTo<GetAllMetadataResult>();
		}
		Result result2 = await GetImagedata(fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!result2.Success)
		{
			return result2.CastTo<GetAllMetadataResult>();
		}
		GetAllMetadataResult getAllMetadataResult = new GetAllMetadataResult
		{
			Success = true
		};
		GetAllMetadataResult getAllMetadataResult2 = getAllMetadataResult;
		getAllMetadataResult2.Metadata = await AchievementsDataCache.GetAllMetadata(AchievementsImageCache).ConfigureAwait(continueOnCapturedContext: false);
		return getAllMetadataResult;
	}

	private async Task<Result> GetMetadata(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Achievements.GetMetadata", LogLevel.L0_Info, fd);
		string metadataEtag = (await AchievementsDataCache.GetAchievementsData().ConfigureAwait(continueOnCapturedContext: false)).MetadataEtag;
		PDX.SDK.Network.Models.Response response = await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("achievements/metadata").AddNamespaceToPath()
			.AddHeader("If-None-Match", metadataEtag)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false);
		if (response.StatusCode != 304)
		{
			PDXResponseParser<GetMetadataResponse> pDXResponseParser = CreateResponseParser<GetMetadataResponse>(response, fd);
			GetMetadataResponse response2 = pDXResponseParser.GetResponse();
			Result resultObject = pDXResponseParser.GetResultObject();
			if (!resultObject.Success || response2.achievements == null || response2.achievements.Count == 0)
			{
				return resultObject;
			}
			Dictionary<string, AchievementsMetadata> achievements = new Dictionary<string, AchievementsMetadata>();
			foreach (KeyValuePair<string, MetadataResponse> achievement in response2.achievements)
			{
				achievements.Add(achievement.Key, new AchievementsMetadata
				{
					Name = achievement.Value.name,
					Titles = achievement.Value.titles,
					Descriptions = achievement.Value.descriptions,
					ProgressMax = achievement.Value.progress_max,
					ProgressMin = achievement.Value.progress_min,
					Hidden = achievement.Value.hidden
				});
			}
			string key = "ETag";
			if (response.Headers.ContainsKey(key))
			{
				await AchievementsDataCache.SetMetadataEtag(response.Headers[key]).ConfigureAwait(continueOnCapturedContext: false);
			}
			await AchievementsDataCache.SetAllMetadata(achievements).ConfigureAwait(continueOnCapturedContext: false);
		}
		return new Result
		{
			Success = true
		};
	}

	private async Task<Result> GetImagedata(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Achievements.GetImagedata", LogLevel.L0_Info, fd);
		string imagedataEtag = (await AchievementsImageCache.GetAchievementsImageData().ConfigureAwait(continueOnCapturedContext: false)).ImagedataEtag;
		PDX.SDK.Network.Models.Response r = await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("/achievements/imagedata").AddNamespaceToPath()
			.AddHeader("If-None-Match", imagedataEtag)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false);
		if (r.StatusCode != 304)
		{
			if (r.StatusCode != 200)
			{
				return CreateResponseParser(r, fd).GetResultObject();
			}
			string key = "ETag";
			if (r.Headers.ContainsKey(key))
			{
				await AchievementsImageCache.SetImageDataEtag(r.Headers[key]).ConfigureAwait(continueOnCapturedContext: false);
			}
			await AchievementsImageCache.SetImages(Encoding.UTF8.GetString(r.Body)).ConfigureAwait(continueOnCapturedContext: false);
		}
		return new Result
		{
			Success = true
		};
	}
}
