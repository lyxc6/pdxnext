using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Service.Achievements.Models;
using PDX.SDK.Contracts.Service.Achievements.Result;

namespace PDX.SDK.Contracts.Service.Achievements;

public interface IAchievementsService
{
	[Obsolete("Achievements are no longer supported")]
	Task<ListResult> List();

	[Obsolete("Achievements are no longer supported")]
	Task<PDX.SDK.Contracts.Result> Register(Dictionary<string, ProgressModel> achievements);

	[Obsolete("Achievements are no longer supported")]
	Task<PDX.SDK.Contracts.Result> Register(string id, ProgressModel progress);
}
