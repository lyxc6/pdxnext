using System.Collections.Generic;
using PDX.SDK.Contracts.Service.Telemetry.Models;

namespace PDX.SDK.Contracts.Service.Telemetry;

public class KeyValueBuilder : IBuilder<KeyValueEventBuilder>
{
	public List<KeyValueEventBuilder> Events;

	public KeyValueBuilder()
	{
		Events = new List<KeyValueEventBuilder>();
	}

	public KeyValueEventBuilder AddEvent(string name)
	{
		KeyValueEventBuilder keyValueEventBuilder = new KeyValueEventBuilder(name, this);
		Events.Add(keyValueEventBuilder);
		return keyValueEventBuilder;
	}

	public List<KeyValueEventBuilder> Build()
	{
		return Events;
	}
}
