using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Achievements.Models;

namespace PDX.SDK.Contracts.Service.Achievements.Result;

[Preserve]
public class GetAllProgressResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public Dictionary<string, ProgressModel> Progress { get; set; }
}
