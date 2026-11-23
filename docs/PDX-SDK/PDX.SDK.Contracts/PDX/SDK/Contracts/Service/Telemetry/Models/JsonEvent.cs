namespace PDX.SDK.Contracts.Service.Telemetry.Models;

public class JsonEvent : IEvent
{
	public string Name { get; set; }

	public object Payload { get; set; }

	public string SchemaVersion { get; set; }
}
