using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Telemetry.Models;

namespace PDX.SDK.Contracts.Service.Telemetry.Result;

[Preserve]
public class SendResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<RejectedEvent> RejectedEvents { get; set; } = new List<RejectedEvent>();
}
