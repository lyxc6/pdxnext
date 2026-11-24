using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Enums;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class PublishResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public int ModId { get; set; }

	[Preserve]
	public ModState State { get; set; }
}
