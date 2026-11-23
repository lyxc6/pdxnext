using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class GetForumThreadResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public ForumPost FirstPost { get; set; }

	[Preserve]
	public ForumPost[] Posts { get; set; }

	[Preserve]
	public int Count { get; set; }

	[Preserve]
	public bool CanPost { get; set; }
}
