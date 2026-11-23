using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.DataStorage.CloudSave.Response;

[Preserve]
public class UploadResponse : HTTPBodyBase
{
	[Preserve]
	public string uri { get; set; }
}
