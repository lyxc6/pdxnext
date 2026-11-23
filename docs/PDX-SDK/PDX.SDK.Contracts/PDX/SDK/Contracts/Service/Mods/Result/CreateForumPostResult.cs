using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class CreateForumPostResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public ForumPost Post { get; set; }
}
