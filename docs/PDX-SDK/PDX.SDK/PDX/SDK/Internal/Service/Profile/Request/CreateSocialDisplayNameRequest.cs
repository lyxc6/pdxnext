using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Profile.Request;

[Preserve]
public class CreateSocialDisplayNameRequest
{
	[Preserve]
	public string username;

	[Preserve]
	public string @namespace;
}
