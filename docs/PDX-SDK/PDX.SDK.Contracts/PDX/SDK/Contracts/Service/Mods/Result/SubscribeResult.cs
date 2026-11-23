using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class SubscribeResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<ModSubscribeStatus> ModsSubscribedStatus { get; set; }

	[Preserve]
	public int PlaysetId { get; set; }
}
