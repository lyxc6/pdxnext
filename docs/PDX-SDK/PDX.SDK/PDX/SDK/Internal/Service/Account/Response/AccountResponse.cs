using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Account.Response;

[Preserve]
internal class AccountResponse
{
	[Preserve]
	public string email { get; set; }

	[Preserve]
	public bool isEmailVerified { get; set; }

	[Preserve]
	public string creationDate { get; set; }

	[Preserve]
	public string referer { get; set; }

	[Preserve]
	public string source { get; set; }
}
