using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class GetActivePlaysetResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public int PlaysetId { get; set; }
}
