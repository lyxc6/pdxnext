using System.Collections.Generic;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Telemetry.Result;

[Preserve]
internal class EventsWhitelistResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<string> EventNames { get; set; } = new List<string>();
}
