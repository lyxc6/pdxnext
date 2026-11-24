using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Account.Response;

[Preserve]
internal class SessionResponse
{
	[Preserve]
	public string userid { get; set; }

	[Preserve]
	public string @namespace { get; set; }

	[Preserve]
	public string token { get; set; }

	[Preserve]
	public string timestamp { get; set; }

	[Preserve]
	public TrustLevelResponse trustLevel { get; set; }

	[Preserve]
	public string refresh_token { get; set; }
}
