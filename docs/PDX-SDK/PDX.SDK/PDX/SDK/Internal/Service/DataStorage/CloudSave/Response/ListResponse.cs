using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.DataStorage.CloudSave.Response;

[Preserve]
internal class ListResponse : HTTPBodyBase
{
	[Preserve]
	public EntryResponse[] list { get; set; }
}
