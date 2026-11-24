using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Profile.Models;

namespace PDX.SDK.Contracts.Service.Profile.Result;

[Preserve]
public class GetProfileResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public UserProfile Social { get; set; }

	[Preserve]
	public UserProfile Game { get; set; }
}
