using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ScreenshotResponse
{
	[Preserve]
	public string image { get; set; }

	[Preserve]
	public string thumbnail { get; set; }
}
