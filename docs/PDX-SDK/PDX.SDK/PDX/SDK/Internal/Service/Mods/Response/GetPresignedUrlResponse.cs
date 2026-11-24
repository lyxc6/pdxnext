using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class GetPresignedUrlResponse : HTTPBodyBase
{
	[Preserve]
	public string modName { get; set; }

	[Preserve]
	public string fileName { get; set; }

	[Preserve]
	public string presignedUrl { get; set; }

	[Preserve]
	public string contentType { get; set; }
}
