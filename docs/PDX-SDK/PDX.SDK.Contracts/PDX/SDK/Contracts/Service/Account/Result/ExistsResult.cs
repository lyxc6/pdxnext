using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Account.Result;

[Preserve]
public class ExistsResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public bool AccountExists { get; set; }
}
