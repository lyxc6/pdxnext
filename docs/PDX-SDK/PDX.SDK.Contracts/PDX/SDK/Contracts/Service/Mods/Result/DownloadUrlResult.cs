using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class DownloadUrlResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public string Url { get; set; }
}
