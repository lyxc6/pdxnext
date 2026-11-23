using System.Collections.Generic;

namespace PDX.SDK.Contracts.Service.Telemetry.Models;

public class JsonEventBuilder : JsonEvent, IBuilder<JsonEventBuilder>
{
	private JsonBuilder Parent;

	public JsonEventBuilder(string name, JsonBuilder parent)
	{
		base.Name = name;
		Parent = parent;
		base.Payload = null;
		base.SchemaVersion = null;
	}

	public JsonEventBuilder(string name, object payload, JsonBuilder parent)
	{
		base.Name = name;
		Parent = parent;
		base.Payload = payload;
		base.SchemaVersion = null;
	}

	public JsonEventBuilder(string name, object payload, string schemaVersion, JsonBuilder parent)
	{
		base.Name = name;
		Parent = parent;
		base.Payload = payload;
		base.SchemaVersion = schemaVersion;
	}

	public JsonEventBuilder AddEvent(string name)
	{
		return Parent.AddEvent(name);
	}

	public JsonEventBuilder SetPayload(object payload)
	{
		base.Payload = payload;
		return this;
	}

	public JsonEventBuilder SetSchemaVersion(string schemaVersion)
	{
		base.SchemaVersion = schemaVersion;
		return this;
	}

	public List<JsonEventBuilder> Build()
	{
		return Parent.Build();
	}
}
