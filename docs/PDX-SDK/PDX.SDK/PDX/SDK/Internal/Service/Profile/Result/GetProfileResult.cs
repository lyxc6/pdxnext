using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Profile.Result;

[Preserve]
internal class GetProfileResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public string DisplayName { get; set; }

	[Preserve]
	public bool IsBanned { get; set; }

	[Preserve]
	public bool Developer { get; set; }
}
