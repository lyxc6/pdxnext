using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.DataStorage.CloudSave.Result;

[Preserve]
public class UploadResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public string Etag { get; set; }

	[Preserve]
	public string Url { get; set; }
}
