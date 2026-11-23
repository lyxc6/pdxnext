using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class PublishUpdateResult : PublishResult
{
	[Preserve]
	public int? ModVersion { get; set; }

	[Preserve]
	public string UserModVersion { get; set; }
}
