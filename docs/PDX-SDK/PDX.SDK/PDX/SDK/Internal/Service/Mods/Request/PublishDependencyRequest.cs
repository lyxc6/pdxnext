using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class PublishDependencyRequest
{
	[Preserve]
	public string type;

	[Preserve]
	public string value;

	[Preserve]
	public string version;
}
