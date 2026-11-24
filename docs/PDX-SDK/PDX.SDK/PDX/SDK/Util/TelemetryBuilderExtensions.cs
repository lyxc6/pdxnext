using System.Collections.Generic;
using PDX.SDK.Contracts.Service.Telemetry.Models;
using TinyJson;

namespace PDX.SDK.Util;

public static class TelemetryBuilderExtensions
{
	internal static JsonEvent ToJsonEvent(this JsonEventBuilder builderEvent)
	{
		return new JsonEvent
		{
			Name = builderEvent.Name,
			Payload = builderEvent.Payload,
			SchemaVersion = builderEvent.SchemaVersion
		};
	}

	internal static List<JsonEvent> ToJsonEventsList(this List<JsonEventBuilder> builderEvents)
	{
		List<JsonEvent> list = new List<JsonEvent>();
		foreach (JsonEventBuilder builderEvent in builderEvents)
		{
			list.Add(builderEvent.ToJsonEvent());
		}
		return list;
	}

	internal static List<JsonEvent> TransformV2EventsToV3Format<T>(this IList<T> v2Events) where T : IEvent
	{
		List<JsonEvent> list = new List<JsonEvent>();
		foreach (T v2Event in v2Events)
		{
			KeyValueEvent obj = v2Event as KeyValueEvent;
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			foreach (KeyValuePair<string, string> keyValue in obj.KeyValues)
			{
				object obj2 = keyValue.Value.FromJson<object>();
				if (obj2 == null)
				{
					dictionary.Add(keyValue.Key, keyValue.Value);
				}
				else
				{
					dictionary.Add(keyValue.Key, obj2);
				}
			}
			JsonEvent item = new JsonEvent
			{
				Name = v2Event.Name,
				Payload = dictionary
			};
			list.Add(item);
		}
		return list;
	}

	internal static List<JsonEvent> ToEventsList<T>(this IList<T> builderEvents) where T : IEvent
	{
		if (typeof(T) == typeof(JsonEventBuilder))
		{
			return (builderEvents as List<JsonEventBuilder>).ToJsonEventsList();
		}
		if (typeof(T).IsSubclassOf(typeof(KeyValueEvent)))
		{
			return builderEvents.TransformV2EventsToV3Format();
		}
		return builderEvents as List<JsonEvent>;
	}
}
