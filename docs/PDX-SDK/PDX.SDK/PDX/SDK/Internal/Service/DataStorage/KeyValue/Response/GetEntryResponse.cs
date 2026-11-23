using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.DataStorage.KeyValue.Response;

[Preserve]
internal class GetEntryResponse : HTTPBodyBase
{
	[Preserve]
	public EntryResponse[] entries { get; set; }
}
