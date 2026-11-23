using System.Collections.Generic;
using PDX.SDK.Contracts.Service.Telemetry.Models;

namespace PDX.SDK.Contracts.Service.Telemetry;

public class JsonBuilder : IBuilder<JsonEventBuilder>
{
	public List<JsonEventBuilder> Events;

	public JsonBuilder()
	{
		Events = new List<JsonEventBuilder>();
	}

	public JsonEventBuilder AddEvent(string name)
	{
		JsonEventBuilder jsonEventBuilder = new JsonEventBuilder(name, this);
		Events.Add(jsonEventBuilder);
		return jsonEventBuilder;
	}

	public List<JsonEventBuilder> Build()
	{
		return Events;
	}
}
