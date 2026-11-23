using System.Collections.Generic;

namespace PDX.SDK.Contracts.Service.Telemetry.Models;

public class KeyValueEvent : IEvent
{
	public string Name { get; set; }

	public IList<KeyValuePair<string, string>> KeyValues { get; set; }
}
