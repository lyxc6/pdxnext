using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Internal.Service.Mods.Response;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Contracts.Service.Mods.Response;

internal class CreateForumPostResponse : HTTPBodyBase
{
	[Preserve]
	public ForumPostResponse post { get; set; }
}
