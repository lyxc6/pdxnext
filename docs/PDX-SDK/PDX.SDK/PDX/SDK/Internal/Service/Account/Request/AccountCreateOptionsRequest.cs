using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Account.Request;

[Preserve]
internal class AccountCreateOptionsRequest
{
	[Preserve]
	public string emailTemplate;

	[Preserve]
	public string landingUrl;

	[Preserve]
	public string refererAccount;

	[Preserve]
	public bool marketingPermission;
}
