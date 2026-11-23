using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.DataStorage.CloudSave.Result;

[Preserve]
public class DownloadResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public int Size { get; set; }

	[Preserve]
	public string Etag { get; set; }

	[Preserve]
	public string RelativePath { get; set; }
}
