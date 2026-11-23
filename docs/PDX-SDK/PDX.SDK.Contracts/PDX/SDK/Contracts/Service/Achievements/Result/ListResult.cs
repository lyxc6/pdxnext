using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Achievements.Models;

namespace PDX.SDK.Contracts.Service.Achievements.Result;

[Preserve]
public class ListResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public Dictionary<string, Achievement> Achievements { get; set; }
}
