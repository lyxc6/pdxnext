using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.DataStorage.KeyValue.Response;

[Preserve]
public class EntryResponse
{
	[Preserve]
	public string path { get; set; }

	[Preserve]
	public string value { get; set; }

	[Preserve]
	public string timestamp { get; set; }
}
