using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Loyalty.Request;

[Preserve]
public class RegisterLoyaltyEventRequest
{
	[Preserve]
	public string @namespace;

	[Preserve]
	public string @event;

	[Preserve]
	public string type;
}
