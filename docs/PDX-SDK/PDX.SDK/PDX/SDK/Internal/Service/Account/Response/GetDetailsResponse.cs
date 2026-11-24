using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Account.Response;

[Preserve]
internal class GetDetailsResponse : HTTPBodyBase
{
	[Preserve]
	public AccountResponse account { get; set; }

	[Preserve]
	public AccountDetailsResponse accountDetails { get; set; }
}
