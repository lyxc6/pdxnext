using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class GetPresignedUrlRequest
{
	[Preserve]
	public string gameName { get; set; }

	[Preserve]
	public string modName { get; set; }

	[Preserve]
	public string fileName { get; set; }
}
