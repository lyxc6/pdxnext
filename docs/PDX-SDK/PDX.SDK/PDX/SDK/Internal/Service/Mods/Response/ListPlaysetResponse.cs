using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ListPlaysetResponse : HTTPBodyBase
{
	[Preserve]
	public int id { get; set; }

	[Preserve]
	public string name { get; set; }

	[Preserve]
	public string gameName { get; set; }

	[Preserve]
	public string displayImagePath { get; set; }

	[Preserve]
	public int modsCount { get; set; }

	[Preserve]
	public ulong modsSize { get; set; }

	[Preserve]
	public string updated { get; set; }

	[Preserve]
	public string latestModUpdate { get; set; }
}
