using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class PublishV1Request : PublishRequest
{
	[Preserve]
	public string backgroundColor;

	[Preserve]
	public string forumLink;
}
