using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Telemetry.Response;

[Preserve]
internal class EventsWhitelistResponse : HTTPBodyBase
{
	[Preserve]
	public string[] events { get; set; }
}
