using System.Collections.Generic;

namespace PDX.SDK.Contracts.Service.Telemetry.Models;

public class KeyValueEventBuilder : KeyValueEvent, IBuilder<KeyValueEventBuilder>
{
	private KeyValueBuilder Parent;

	public KeyValueEventBuilder(string name, KeyValueBuilder parent)
	{
		base.Name = name;
		Parent = parent;
		base.KeyValues = new List<KeyValuePair<string, string>>();
	}

	public KeyValueEventBuilder AddEvent(string name)
	{
		return Parent.AddEvent(name);
	}

	public KeyValueEventBuilder AddKeyValue(string key, string value)
	{
		base.KeyValues.Add(new KeyValuePair<string, string>(key, value));
		return this;
	}

	public List<KeyValueEventBuilder> Build()
	{
		return Parent.Build();
	}
}
