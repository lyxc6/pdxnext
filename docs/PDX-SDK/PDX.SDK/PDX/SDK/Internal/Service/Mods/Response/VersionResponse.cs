using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class VersionResponse
{
	[Preserve]
	public int id { get; set; }

	[Preserve]
	public string created { get; set; }

	[Preserve]
	public ulong size { get; set; }
}
