using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class PlaysetResponse
{
	[Preserve]
	public int id { get; set; }

	[Preserve]
	public string installed { get; set; }

	[Preserve]
	public string preferredVersion { get; set; }

	[Preserve]
	public bool enabled { get; set; }
}
