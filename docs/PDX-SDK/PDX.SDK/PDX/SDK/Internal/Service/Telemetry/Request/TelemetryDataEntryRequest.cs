using System;
using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Telemetry.Request;

[Preserve]
internal class TelemetryDataEntryRequest
{
	[Preserve]
	public string timestamp;

	[Preserve]
	public string @event;

	[Preserve]
	public List<Tuple<string, string>> key_values;
}
