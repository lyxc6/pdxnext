using System;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;

namespace PDX.SDK.Internal.Service.Account.Result;

[Preserve]
internal class InternalLoginResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public string UserId { get; set; }

	[Preserve]
	public TrustLevel TrustLevel { get; set; }

	[Preserve]
	public string Namespace { get; set; }

	[Preserve]
	public string Token { get; set; }

	[Preserve]
	public DateTime? Timestamp { get; set; }

	[Preserve]
	public string RefreshToken { get; set; }
}
