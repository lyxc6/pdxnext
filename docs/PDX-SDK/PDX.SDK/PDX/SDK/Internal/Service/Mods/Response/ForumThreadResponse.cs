using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

internal class ForumThreadResponse : HTTPBodyBase
{
	[Preserve]
	public ForumPostResponse[] posts { get; set; }

	[Preserve]
	public int count { get; set; }

	[Preserve]
	public bool canPost { get; set; }

	[Preserve]
	public ForumPostResponse firstPost { get; set; }
}
