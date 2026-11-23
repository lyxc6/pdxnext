using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.DataStorage.CloudSave.Response;

[Preserve]
internal class DownloadResponse : HTTPBodyBase
{
	[Preserve]
	public string uri { get; set; }
}
