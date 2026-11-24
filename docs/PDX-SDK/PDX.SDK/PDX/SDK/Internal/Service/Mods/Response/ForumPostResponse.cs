using System;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ForumPostResponse
{
	[Preserve]
	public string username { get; set; }

	[Preserve]
	public int userId { get; set; }

	[Preserve]
	public string userTitle { get; set; }

	[Preserve]
	public string url { get; set; }

	[Preserve]
	public int postId { get; set; }

	[Preserve]
	public string message { get; set; }

	[Preserve]
	public DateTime created { get; set; }

	[Preserve]
	public string avatar { get; set; }
}
