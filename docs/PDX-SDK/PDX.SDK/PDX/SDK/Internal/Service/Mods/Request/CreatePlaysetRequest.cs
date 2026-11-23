using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class CreatePlaysetRequest
{
	[Preserve]
	public string game;

	[Preserve]
	public string name;
}
