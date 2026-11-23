using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Enums;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal class GetPresignedUrlResult : Result
{
	[Preserve]
	public string ModGuid { get; set; }

	[Preserve]
	public string FileName { get; set; }

	[Preserve]
	public string PresignedUrl { get; set; }

	[Preserve]
	public ContentType ContentType { get; set; }
}
