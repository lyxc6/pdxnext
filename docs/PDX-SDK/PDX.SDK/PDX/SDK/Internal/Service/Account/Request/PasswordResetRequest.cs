using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Account.Request;

[Preserve]
internal class PasswordResetRequest
{
	[Preserve]
	public string email;

	[Preserve]
	public string emailTemplate;
}
