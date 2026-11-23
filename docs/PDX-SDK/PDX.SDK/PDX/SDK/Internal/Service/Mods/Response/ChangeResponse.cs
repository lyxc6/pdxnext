using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ChangeResponse
{
	[Preserve]
	public int modVersion { get; set; }

	[Preserve]
	public string released { get; set; }

	[Preserve]
	public string details { get; set; }

	[Preserve]
	public ulong size { get; set; }

	[Preserve]
	public string userModVersion { get; set; }
}
