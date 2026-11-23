using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class GetDownloadUrlResponse : HTTPBodyBase
{
	[Preserve]
	public string prepend { get; set; }

	[Preserve]
	public string append { get; set; }

	[Preserve]
	public string ttl { get; set; }
}
