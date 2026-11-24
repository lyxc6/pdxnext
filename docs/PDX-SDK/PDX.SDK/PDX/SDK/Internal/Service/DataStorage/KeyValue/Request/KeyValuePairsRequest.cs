using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.DataStorage.KeyValue.Request;

[Preserve]
internal class KeyValuePairsRequest
{
	[Preserve]
	public string path { get; set; }

	[Preserve]
	public string value { get; set; }
}
