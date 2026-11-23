using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.ThirdParty.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class GetThirdPartyProfilesResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<ThirdPartyProfile> UserProfiles { get; set; }
}
