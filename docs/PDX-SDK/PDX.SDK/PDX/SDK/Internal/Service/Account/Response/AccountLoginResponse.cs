using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Account.Response;

[Preserve]
internal class AccountLoginResponse : HTTPBodyBase
{
	[Preserve]
	public SessionResponse session { get; set; }
}
