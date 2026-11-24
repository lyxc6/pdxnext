using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.ThirdParty.Models;

namespace PDX.SDK.Contracts.Service.ThirdParty.Result;

[Preserve]
public class GetAccountLinksResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<AccountLink> AccountLinks { get; set; }
}
