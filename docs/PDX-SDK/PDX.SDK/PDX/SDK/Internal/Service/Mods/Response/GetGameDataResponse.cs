using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class GetGameDataResponse
{
	[Preserve]
	public GameDataResponse game { get; set; }
}
