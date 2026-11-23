using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Account.Request;

[Preserve]
internal class AccountCreateDescRequest
{
	[Preserve]
	public string email;

	[Preserve]
	public string password;

	[Preserve]
	public string source;

	[Preserve]
	public string referer;

	[Preserve]
	public string sourceService;

	[Preserve]
	public string campaign;

	[Preserve]
	public string medium;

	[Preserve]
	public string channel;

	[Preserve]
	public AccountCreateDetailsRequest details;

	[Preserve]
	public AccountCreateOptionsRequest options;
}
