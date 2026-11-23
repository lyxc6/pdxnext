using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Internal.Service.Mods.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class GameDataResponse
{
	[Preserve]
	public string name { get; set; }

	[Preserve]
	public string appId { get; set; }

	[Preserve]
	public string id { get; set; }

	[Preserve]
	public string extension { get; set; }

	[Preserve]
	public InternalModTag[] tags { get; set; }

	[Preserve]
	public InternalModGameActions actions { get; set; }
}
